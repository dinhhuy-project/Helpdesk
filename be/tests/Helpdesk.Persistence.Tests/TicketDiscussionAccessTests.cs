using System.Net;
using System.Net.Http.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class TicketDiscussionAccessTests(PostgresFixture fixture)
{
    [PostgresTheory]
    [InlineData("requester-admin", TicketStatus.New, false, true, false)]
    [InlineData("member", TicketStatus.New, true, true, true)]
    [InlineData("member", TicketStatus.Closed, true, false, false)]
    [InlineData("member", TicketStatus.Cancelled, true, false, false)]
    [InlineData("member", TicketStatus.Resolved, true, true, true)]
    [InlineData("internal-only", TicketStatus.New, true, false, true)]
    [InlineData("public-only", TicketStatus.New, false, true, false)]
    [InlineData("read-only", TicketStatus.New, false, false, false)]
    public async Task CapabilitiesUseResourceAclAndMatchActualCommentWrites(string relation, TicketStatus status,
        bool internalRead, bool publicWrite, bool internalWrite)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var user = relation switch
        {
            "requester-admin" => test.Accounts.AdminUser,
            "member" => await test.UserAsync(RoleNames.SupportAgent),
            "internal-only" => await test.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketCommentInternal),
            "public-only" => await test.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketCommentPublic),
            _ => await test.CustomUserAsync(PermissionCodes.TicketReadAll)
        };
        await test.MemberAsync(user.Id, test.TeamA);
        var ticket = await test.TicketAsync(relation == "requester-admin" ? user.Id : test.Accounts.AdminUser.Id, test.TeamA, status: status);
        using var client = await test.Accounts.ClientAsync(user);
        var flags = await client.GetFromJsonAsync<TicketDiscussionAccess>($"/api/v1/tickets/{ticket.Id}/discussion-access");
        Assert.Equal(new TicketDiscussionAccess(internalRead, publicWrite, internalWrite), flags);
        foreach (var isInternal in new[] { false, true })
        {
            using var result = await client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new { Content = "Capability check", IsInternal = isInternal });
            Assert.Equal((isInternal ? internalWrite : publicWrite) ? HttpStatusCode.Created : HttpStatusCode.Forbidden, result.StatusCode);
        }
    }

    [PostgresFact]
    public async Task CurrentJwtLosesInternalCapabilityAndHistoryAfterGrantIsRevoked()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.UserAsync(RoleNames.Requester);
        var user = await test.CustomUserAsync(PermissionCodes.TicketReadAll, PermissionCodes.TicketCommentPublic, PermissionCodes.TicketCommentInternal);
        var ticket = await test.TicketAsync(owner.Id, test.TeamA);
        using var client = await test.Accounts.ClientAsync(user);
        using var posted = await client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new { Content = "internal-marker", IsInternal = true });
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        Assert.True((await client.GetFromJsonAsync<TicketDiscussionAccess>($"/api/v1/tickets/{ticket.Id}/discussion-access"))!.CanReadInternal);
        await using var database = test.Accounts.Database();
        var roleIds = database.UserRoles.Where(x => x.UserId == user.Id).Select(x => x.RoleId);
        var permissionIds = database.Permissions.Where(x => x.Code == PermissionCodes.TicketCommentInternal).Select(x => x.Id);
        await database.RolePermissions.Where(x => roleIds.Contains(x.RoleId) && permissionIds.Contains(x.PermissionId)).ExecuteDeleteAsync();
        Assert.Equal(new TicketDiscussionAccess(false, true, false), await client.GetFromJsonAsync<TicketDiscussionAccess>($"/api/v1/tickets/{ticket.Id}/discussion-access"));
        var page = await client.GetFromJsonAsync<PagedResult<TicketCommentDetails>>($"/api/v1/tickets/{ticket.Id}/comments?isInternal=true");
        Assert.Empty(page!.Items); Assert.Equal(0, page.TotalCount);
    }

    [PostgresFact]
    public async Task DiscussionCapabilitiesDoNotRevealAnOutOfScopeTicketOrGrantReadToAnAssignee()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var user = await test.UserAsync(RoleNames.SupportAgent);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA, user.Id);
        using var client = await test.Accounts.ClientAsync(user);
        using var outside = await client.GetAsync($"/api/v1/tickets/{ticket.Id}/discussion-access");
        using var missing = await client.GetAsync($"/api/v1/tickets/{Guid.NewGuid()}/discussion-access");
        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        Assert.Equal(outside.StatusCode, missing.StatusCode);
        Assert.DoesNotContain(ticket.Title, await outside.Content.ReadAsStringAsync());
    }
}
