using System.Net;
using System.Net.Http.Json;
using System.Text;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Persistence;
using be.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Helpdesk.Persistence.Tests.AttachmentTestContext;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class TicketAttachmentTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task PublicAndInternalFilesHaveServerMetadataPrivateStorageAndMatchingTimelineVisibility()
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        var ticket = await test.Data.TicketAsync(owner.Id, test.Data.TeamA, agent.Id);
        using var requester = await test.ClientAsync(owner);
        using var support = await test.ClientAsync(agent);
        requester.DefaultRequestHeaders.Add("X-User-Id", agent.Id.ToString());
        var bytes = Encoding.UTF8.GetBytes("Nội dung file public");
        using var create = await SendFileAsync(requester, ticket.Id, bytes: bytes);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var file = (await create.Content.ReadFromJsonAsync<TicketAttachmentDetails>())!;
        Assert.Equal(owner.Id, file.UploadedByUserId);
        Assert.Equal(bytes.Length, file.SizeBytes);
        Assert.False(file.IsInternal);
        Assert.EndsWith(Path(ticket.Id) + "/" + file.Id, create.Headers.Location?.ToString());
        Assert.DoesNotContain("objectKey", await create.Content.ReadAsStringAsync());
        Assert.DoesNotContain("bucket", await create.Content.ReadAsStringAsync());
        using var internalCreate = await SendFileAsync(support, ticket.Id, name: "internal-secret-name.txt", isInternal: true);
        Assert.Equal(HttpStatusCode.Created, internalCreate.StatusCode);
        var note = (await internalCreate.Content.ReadFromJsonAsync<TicketAttachmentDetails>())!;
        using var download = await requester.GetAsync(Path(ticket.Id) + "/" + file.Id + "/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("text/plain", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(download.Headers.CacheControl?.NoStore);
        foreach (var suffix in new[] { "", "/download" })
        {
            using var hidden = await requester.GetAsync(Path(ticket.Id) + "/" + note.Id + suffix);
            await ErrorAsync(hidden, HttpStatusCode.NotFound, "NotFound");
            using var allowed = await support.GetAsync(Path(ticket.Id) + "/" + note.Id + suffix);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        var page = (await requester.GetFromJsonAsync<PagedResult<TicketAttachmentDetails>>(Path(ticket.Id)))!;
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(file.Id, Assert.Single(page.Items).Id);
        Assert.Empty((await requester.GetFromJsonAsync<PagedResult<TicketAttachmentDetails>>(Path(ticket.Id) + "?isInternal=true"))!.Items);
        using var timeline = await requester.GetAsync($"/api/v1/tickets/{ticket.Id}/activities");
        var json = await timeline.Content.ReadAsStringAsync();
        Assert.DoesNotContain(note.Id.ToString(), json);
        Assert.DoesNotContain("internal-secret-name", json);
        Assert.Contains(file.Id.ToString(), json);
        await using var database = test.Data.Accounts.Database();
        var metadata = await database.TicketAttachments.SingleAsync(x => x.Id == file.Id);
        Assert.Matches("^attachments/[a-f0-9]{32}$", metadata.ObjectKey);
        Assert.DoesNotContain(metadata.FileName, metadata.ObjectKey);
        Assert.False(await database.AttachmentUploadIntents.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.Equal(2, await database.TicketActivities.CountAsync(x => x.TicketId == ticket.Id));
        Assert.Equal(ticket.Version, (await test.Data.StoredAsync(ticket.Id)).Version);
    }

    [PostgresFact]
    public async Task CommentFilesDeriveVisibilityAndRequireSameTicketAndCommentAuthor()
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        using var requester = await test.ClientAsync(owner);
        using var support = await test.ClientAsync(agent);
        var ticket = await test.Data.TicketAsync(owner.Id, test.Data.TeamA);
        var other = await test.Data.TicketAsync(owner.Id, test.Data.TeamA);
        using var noteResponse = await support.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new { Content = "Internal", IsInternal = true });
        var note = (await noteResponse.Content.ReadFromJsonAsync<TicketCommentDetails>())!;
        using var upload = await SendFileAsync(support, ticket.Id, commentId: note.Id);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var file = (await upload.Content.ReadFromJsonAsync<TicketAttachmentDetails>())!;
        Assert.True(file.IsInternal);
        Assert.Equal(note.Id, file.CommentId);
        using var mismatch = await SendFileAsync(support, ticket.Id, commentId: note.Id, isInternal: false);
        await ErrorAsync(mismatch, HttpStatusCode.BadRequest, "InvalidInput");
        using var wrongTicket = await SendFileAsync(support, other.Id, commentId: note.Id);
        await ErrorAsync(wrongTicket, HttpStatusCode.NotFound, "NotFound");
        using var hiddenNote = await SendFileAsync(requester, ticket.Id, commentId: note.Id, isInternal: false);
        await ErrorAsync(hiddenNote, HttpStatusCode.NotFound, "NotFound");
        using var hiddenFilter = await requester.GetAsync(Path(ticket.Id) + "?commentId=" + note.Id);
        await ErrorAsync(hiddenFilter, HttpStatusCode.NotFound, "NotFound");
        using var comment = await requester.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new { Content = "Public" });
        var publicComment = (await comment.Content.ReadFromJsonAsync<TicketCommentDetails>())!;
        using var notAuthor = await SendFileAsync(support, ticket.Id, commentId: publicComment.Id);
        await ErrorAsync(notAuthor, HttpStatusCode.Forbidden, "Forbidden");
        using var own = await SendFileAsync(requester, ticket.Id, commentId: publicComment.Id);
        Assert.Equal(HttpStatusCode.Created, own.StatusCode);
        var count = (await support.GetFromJsonAsync<PagedResult<TicketAttachmentDetails>>(Path(ticket.Id) + "?commentId=" + note.Id))!;
        Assert.Equal(file.Id, Assert.Single(count.Items).Id);
        using var nested = await support.GetAsync(Path(other.Id) + "/" + file.Id + "/download");
        await ErrorAsync(nested, HttpStatusCode.NotFound, "NotFound");
    }

    [PostgresTheory]
    [InlineData("grant")]
    [InlineData("membership")]
    [InlineData("terminal")]
    public async Task PermissionOrStateChangedDuringPutCannotFinalizeAndLeavesDurableCleanupIntent(string change)
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString);
        var agent = await test.Data.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketCommentInternal);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, test.Data.TeamA);
        using var client = await test.ClientAsync(agent);
        test.Storage.AfterPut = async () =>
        {
            await using var database = test.Data.Accounts.Database();
            await using var transaction = await database.Database.BeginTransactionAsync();
            var canLock = await database.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock(734205001) AS \"Value\"").SingleAsync();
            Assert.True(canLock); // Storage I/O must not hold the application's database transaction.
            if (change == "membership") await database.SupportTeamMembers.Where(x => x.UserId == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
            else if (change == "terminal") await database.Tickets.Where(x => x.Id == ticket.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, TicketStatus.Cancelled));
            else
            {
                var roleIds = database.UserRoles.Where(x => x.UserId == agent.Id).Select(x => x.RoleId);
                var permissionId = await database.Permissions.Where(x => x.Code == PermissionCodes.TicketCommentInternal).Select(x => x.Id).SingleAsync();
                await database.RolePermissions.Where(x => roleIds.Contains(x.RoleId) && x.PermissionId == permissionId).ExecuteDeleteAsync();
            }
            await transaction.CommitAsync();
        };
        using var response = await SendFileAsync(client, ticket.Id, isInternal: true);
        Assert.Equal(change == "membership" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, response.StatusCode);
        await using var check = test.Data.Accounts.Database();
        Assert.False(await check.TicketAttachments.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.False(await check.TicketActivities.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.Single(await check.AttachmentUploadIntents.Where(x => x.TicketId == ticket.Id).ToArrayAsync());
        Assert.Single(test.Storage.Objects);
        await Expire(test, ticket.Id);
        await using var cleanupScope = test.Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await cleanupScope.ServiceProvider.GetRequiredService<AttachmentCleanup>().RunOnceAsync(CancellationToken.None));
        Assert.Empty(test.Storage.Objects);
    }

    [PostgresFact]
    public async Task DownloadRechecksAuthorizationAfterStorageCopyAndNeverCallsStorageForHiddenIds()
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString);
        var agent = await test.Data.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketCommentInternal);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, test.Data.TeamA);
        using var client = await test.ClientAsync(agent);
        using var response = await SendFileAsync(client, ticket.Id, isInternal: true);
        var file = (await response.Content.ReadFromJsonAsync<TicketAttachmentDetails>())!;
        test.Storage.BeforeCopy = async () =>
        {
            await using var database = test.Data.Accounts.Database();
            await database.SupportTeamMembers.Where(x => x.UserId == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        };
        using var revoked = await client.GetAsync(Path(ticket.Id) + "/" + file.Id + "/download");
        await ErrorAsync(revoked, HttpStatusCode.NotFound, "NotFound");
        Assert.DoesNotContain("Private file body", await revoked.Content.ReadAsStringAsync());
        Assert.Equal(1, test.Storage.DownloadCalls);
        using var hidden = await client.GetAsync(Path(ticket.Id) + "/" + file.Id + "/download");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(1, test.Storage.DownloadCalls);
    }

    [PostgresFact]
    public async Task PendingReservationsCountAgainstQuotasAndCleanupRetriesStorageFailure()
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString, new() { ["Minio:MaxFilesPerVisibility"] = "1" });
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id);
        using var client = await test.ClientAsync(test.Data.Accounts.AdminUser);
        test.Storage.FailPut = true;
        using var failed = await SendFileAsync(client, ticket.Id);
        await ErrorAsync(failed, HttpStatusCode.ServiceUnavailable, "StorageUnavailable");
        test.Storage.FailPut = false;
        using var full = await SendFileAsync(client, ticket.Id);
        await ErrorAsync(full, HttpStatusCode.Conflict, "AttachmentQuotaExceeded");
        await Expire(test, ticket.Id);
        test.Storage.FailDelete = true;
        await using (var scope = test.Factory.Services.CreateAsyncScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AttachmentCleanup>().RunOnceAsync(CancellationToken.None));
        await using (var database = test.Data.Accounts.Database())
            Assert.True((await database.AttachmentUploadIntents.SingleAsync(x => x.TicketId == ticket.Id)).IsDeleting);
        test.Storage.FailDelete = false;
        await Expire(test, ticket.Id);
        await using (var scope = test.Factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AttachmentCleanup>().RunOnceAsync(CancellationToken.None));
        using var retry = await SendFileAsync(client, ticket.Id);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        // Committed metadata must never become a cleanup candidate.
        await using (var scope = test.Factory.Services.CreateAsyncScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AttachmentCleanup>().RunOnceAsync(CancellationToken.None));
        Assert.Single(test.Storage.Objects);
    }

    [PostgresFact]
    public async Task CleanupClaimPermanentlyBlocksLateFinalizationWithoutHoldingIoTransaction()
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString);
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id);
        using var client = await test.ClientAsync(test.Data.Accounts.AdminUser);
        await using var reservationScope = test.Factory.Services.CreateAsyncScope();
        var store = reservationScope.ServiceProvider.GetRequiredService<ITicketAttachmentStore>();
        var reservation = await store.ReserveAsync(Actor(client), ticket.Id, new("report.txt", "text/plain", 5), test.Storage.Bucket, new(20, 1000), CancellationToken.None);
        test.Storage.Objects[reservation.ObjectKey] = Encoding.UTF8.GetBytes("hello");
        await Expire(test, ticket.Id);
        test.Storage.BeforeDelete = async () =>
        {
            await using var finalizeScope = test.Factory.Services.CreateAsyncScope();
            var error = await Assert.ThrowsAsync<TicketOperationException>(() => finalizeScope.ServiceProvider.GetRequiredService<ITicketAttachmentStore>()
                .CompleteAsync(Actor(client), ticket.Id, reservation.Id, CancellationToken.None));
            Assert.Equal(TicketError.UploadExpired, error.Error);
        };
        await using var scope = test.Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AttachmentCleanup>().RunOnceAsync(CancellationToken.None));
        await using var check = test.Data.Accounts.Database();
        Assert.False(await check.TicketAttachments.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.Empty(test.Storage.Objects);
    }

    [PostgresTheory]
    [InlineData("../report.txt", "text/plain", "hello", 400)]
    [InlineData("C:\\report.txt", "text/plain", "hello", 400)]
    [InlineData(".hidden.txt", "text/plain", "hello", 400)]
    [InlineData("report.exe", "application/octet-stream", "hello", 415)]
    [InlineData("report.svg", "image/svg+xml", "<svg/>", 415)]
    [InlineData("report.txt", "text/html", "hello", 415)]
    [InlineData("report.pdf", "application/pdf", "wrong header", 400)]
    [InlineData("report.png", "image/png", "wrong header", 400)]
    [InlineData("report.jpg", "image/jpeg", "wrong header", 400)]
    [InlineData("report.txt", "text/plain", "a\0b", 400)]
    [InlineData("report.txt", "text/plain", "", 400)]
    public async Task InvalidFilePathMimeOrContentIsRejected(string name, string type, string content, int status)
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString);
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id);
        using var client = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var response = await SendFileAsync(client, ticket.Id, name, type, Encoding.UTF8.GetBytes(content));
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Empty(test.Storage.Objects);
        await using var database = test.Data.Accounts.Database();
        Assert.False(await database.TicketAttachments.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.False(await database.TicketActivities.AnyAsync(x => x.TicketId == ticket.Id));
    }

    [PostgresTheory]
    [InlineData("uploadedByUserId")]
    [InlineData("objectKey")]
    [InlineData("bucket")]
    [InlineData("sizeBytes")]
    [InlineData("version")]
    public async Task MultipartServerFieldsCannotBeOverposted(string field)
    {
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString);
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id);
        using var client = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var response = await SendFileAsync(client, ticket.Id, extraField: field);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("forged-marker", await response.Content.ReadAsStringAsync());
        Assert.Empty(test.Storage.Objects);
    }

    [PostgresFact]
    public async Task MetadataAndActivityRollbackLeavesUploadedObjectJournaledForCleanup()
    {
        var saved = new FailMetadataSave();
        await using var test = await AttachmentTestContext.CreateAsync(fixture.ConnectionString, configure: services =>
            services.Replace(ServiceDescriptor.Scoped<HelpdeskDbContext>(_ => new(new DbContextOptionsBuilder<HelpdeskDbContext>()
                .UseNpgsql(fixture.ConnectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk"))
                .AddInterceptors(saved).Options))));
        var ticket = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id);
        using var client = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var response = await SendFileAsync(client, ticket.Id);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(saved.Saved);
        await using var database = test.Data.Accounts.Database();
        Assert.False(await database.TicketAttachments.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.False(await database.TicketActivities.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.Single(await database.AttachmentUploadIntents.Where(x => x.TicketId == ticket.Id).ToListAsync());
        Assert.Single(test.Storage.Objects);
        await Expire(test, ticket.Id);
        await using var scope = test.Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AttachmentCleanup>().RunOnceAsync(CancellationToken.None));
        Assert.Empty(test.Storage.Objects);
    }
    private sealed class FailMetadataSave : SaveChangesInterceptor
    {
        internal bool Saved { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TicketAttachment>().Any(x => x.State == EntityState.Unchanged))
            {
                Saved = true;
                throw new InvalidOperationException("secret-marker: fail after attachment metadata save");
            }
            return ValueTask.FromResult(result);
        }
    }
    private static async Task Expire(AttachmentTestContext test, Guid ticketId)
    {
        await using var database = test.Data.Accounts.Database();
        // Parameterize the application's clock. Inline UtcNow is translated to PostgreSQL
        // now(), whose Docker VM clock can differ from the host clock used by cleanup.
        var expiredAt = DateTime.UtcNow.AddMinutes(-1);
        var createdAt = expiredAt.AddHours(-1);
        await database.AttachmentUploadIntents.Where(x => x.TicketId == ticketId).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.CreatedAt, createdAt).SetProperty(x => x.ExpiresAt, expiredAt));
    }
}
