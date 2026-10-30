using System.Net;
using System.Net.Http.Json;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Helpdesk.Persistence.Tests.AttachmentTestContext;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class AttachmentOptionsTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task OptionsExposeConfiguredLimitsAndCurrentTicketCapabilitiesWithoutStorageCoordinates()
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString, new() {
            ["Minio:MaxFileSizeMiB"] = "1", ["Minio:MaxFilesPerVisibility"] = "2", ["Minio:MaxTotalSizeMiBPerVisibility"] = "3" });
        var owner = await test.Data.CustomUserAsync(PermissionCodes.TicketReadAll, PermissionCodes.TicketCommentPublic, PermissionCodes.TicketCommentInternal);
        var ticket = await test.Data.TicketAsync(owner.Id);
        using var client = await test.ClientAsync(owner);
        using var response = await client.GetAsync(Path(ticket.Id) + "/options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<TicketAttachmentOptions>())!;
        Assert.True(result.Policy.Enabled); Assert.Equal(1048576, result.Policy.MaxFileSizeBytes);
        Assert.Equal(2, result.Policy.MaxFilesPerVisibility); Assert.Equal(3145728, result.Policy.MaxTotalSizeBytesPerVisibility);
        Assert.Equal(new[] { ".txt", ".pdf", ".png", ".jpg", ".jpeg" }, result.Policy.FileTypes.Select(x => x.Extension));
        Assert.Equal(new TicketAttachmentAccess(false, true, false), result.Access); // Requester, even with read.all/internal grant.
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "endpoint", "objectKey", "bucket", "accessKey", "secretKey", "password" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task CommentOptionsUseOwnershipAndFixedVisibilityAndRefreshAfterGrantAndStatusChanges()
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString);
        var author = await test.Data.UserAsync(RoleNames.SupportAgent);
        var other = await test.Data.UserAsync(RoleNames.SupportAgent);
        await test.Data.MemberAsync(author.Id, test.Data.TeamA); await test.Data.MemberAsync(other.Id, test.Data.TeamA);
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, test.Data.TeamA, author.Id);
        using var client = await test.ClientAsync(author); using var colleague = await test.ClientAsync(other);
        foreach (var isInternal in new[] { false, true })
        {
            using var created = await client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new { Content = "Comment attachment fixture", IsInternal = isInternal });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var comment = (await created.Content.ReadFromJsonAsync<TicketCommentDetails>())!;
            var url = Path(ticket.Id) + $"/options?commentId={comment.Id}";
            var own = (await client.GetFromJsonAsync<TicketAttachmentOptions>(url))!;
            Assert.True(own.Access.CanReadInternal);
            Assert.Equal(!isInternal, own.Access.CanUploadPublic); Assert.Equal(isInternal, own.Access.CanUploadInternal);
            var notAuthor = (await colleague.GetFromJsonAsync<TicketAttachmentOptions>(url))!;
            Assert.False(notAuthor.Access.CanUploadPublic); Assert.False(notAuthor.Access.CanUploadInternal);
        }
        await using var database = test.Data.Accounts.Database();
        // Use a dedicated role, so other default-role fixtures are unaffected.
        var custom = await test.Data.Accounts.CustomRoleAsync(PermissionCodes.TicketReadTeam);
        await database.UserRoles.Where(x => x.UserId == author.Id).ExecuteDeleteAsync();
        await test.Data.Accounts.AssignAsync(author.Id, custom);
        var readOnly = (await client.GetFromJsonAsync<TicketAttachmentOptions>(Path(ticket.Id) + "/options"))!;
        Assert.Equal(new TicketAttachmentAccess(false, false, false), readOnly.Access);
        await database.Tickets.Where(x => x.Id == ticket.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, TicketStatus.Cancelled));
        var terminal = (await colleague.GetFromJsonAsync<TicketAttachmentOptions>(Path(ticket.Id) + "/options"))!;
        Assert.False(terminal.Access.CanUploadPublic); Assert.False(terminal.Access.CanUploadInternal);
    }

    [PostgresFact]
    public async Task SourceCommentAndMembershipAreCheckedBeforeRevealingOptions()
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        var first = await test.Data.TicketAsync(owner.Id, test.Data.TeamA, agent.Id);
        var outside = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, test.Data.TeamB);
        using var requester = await test.ClientAsync(owner); using var support = await test.ClientAsync(agent);
        using var created = await support.PostAsJsonAsync($"/api/v1/tickets/{first.Id}/comments", new { Content = "Internal fixture", IsInternal = true });
        var comment = (await created.Content.ReadFromJsonAsync<TicketCommentDetails>())!;
        foreach (var url in new[] { Path(outside.Id) + "/options", Path(first.Id) + $"/options?commentId={comment.Id}" })
        {
            using var hidden = await requester.GetAsync(url); await ErrorAsync(hidden, HttpStatusCode.NotFound, "NotFound");
        }
        using var mismatch = await support.GetAsync(Path(first.Id) + $"/options?commentId={Guid.NewGuid()}");
        await ErrorAsync(mismatch, HttpStatusCode.NotFound, "NotFound");
        await using var database = test.Data.Accounts.Database();
        await database.SupportTeamMembers.Where(x => x.UserId == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        using var revoked = await support.GetAsync(Path(first.Id) + "/options");
        await ErrorAsync(revoked, HttpStatusCode.NotFound, "NotFound");
    }

    [PostgresFact]
    public async Task DisabledStorageKeepsMetadataReadsButDoesNotOfferUploads()
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString, new() { ["Minio:Enabled"] = "false" });
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id);
        using var client = await test.ClientAsync(test.Data.Accounts.AdminUser);
        var options = (await client.GetFromJsonAsync<TicketAttachmentOptions>(Path(ticket.Id) + "/options"))!;
        Assert.False(options.Policy.Enabled); Assert.False(options.Access.CanUploadPublic); Assert.False(options.Access.CanUploadInternal);
        using var list = await client.GetAsync(Path(ticket.Id)); Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }
}
