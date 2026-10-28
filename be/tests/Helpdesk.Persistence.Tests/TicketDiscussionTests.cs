using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class TicketDiscussionTests(PostgresFixture fixture)
{
    private static string Comments(Guid ticket) => $"/api/v1/tickets/{ticket}/comments";
    private static string Activities(Guid ticket) => $"/api/v1/tickets/{ticket}/activities";
    private static async Task<TicketCommentDetails> Add(HttpClient client, Guid ticket, string content, bool isInternal = false)
    {
        using var response = await client.PostAsJsonAsync(Comments(ticket), new { Content = content, IsInternal = isInternal });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var comment = (await response.Content.ReadFromJsonAsync<TicketCommentDetails>())!;
        Assert.EndsWith(Comments(ticket) + "/" + comment.Id, response.Headers.Location?.ToString());
        using var get = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(comment, await get.Content.ReadFromJsonAsync<TicketCommentDetails>());
        return comment;
    }

    [PostgresFact]
    public async Task RequesterSeesOnlyPublicCommentsAndActivitiesWhileAgentReadsInternalNotes()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.UserAsync(RoleNames.Requester);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(agent.Id, test.TeamA);
        var ticket = await test.TicketAsync(owner.Id, test.TeamA, agent.Id, status: TicketStatus.WaitingForRequester);
        using var requester = await test.Accounts.ClientAsync(owner);
        using var support = await test.Accounts.ClientAsync(agent);
        requester.DefaultRequestHeaders.Add("X-User-Id", agent.Id.ToString());
        var publicComment = await Add(requester, ticket.Id, "  Public answer  ");
        Assert.Equal(owner.Id, publicComment.AuthorId);
        Assert.Equal("Public answer", publicComment.Content);
        Assert.False(publicComment.IsInternal);
        Assert.Null(publicComment.UpdatedAt);
        var note = await Add(support, ticket.Id, "internal-secret-marker", true);
        Assert.Equal(agent.Id, note.AuthorId);
        await using var database = test.Accounts.Database();
        var internalActivity = await database.TicketActivities.SingleAsync(x => x.TicketId == ticket.Id && x.Action == TicketActivityActions.InternalNoteAdded);
        var publicActivity = await database.TicketActivities.SingleAsync(x => x.TicketId == ticket.Id && x.Action == TicketActivityActions.PublicCommentAdded);
        Assert.Equal(publicComment.AuthorId, publicActivity.ActorId);
        Assert.Equal(publicComment.CreatedAt, publicActivity.CreatedAt);
        Assert.Null(publicActivity.OldValue);
        Assert.Equal(publicComment.Id, JsonDocument.Parse(publicActivity.NewValue!).RootElement.GetProperty("CommentId").GetGuid());
        Assert.DoesNotContain(publicComment.Content, publicActivity.NewValue);
        Assert.DoesNotContain(note.Content, internalActivity.NewValue);
        var list = (await requester.GetFromJsonAsync<PagedResult<TicketCommentDetails>>(Comments(ticket.Id)))!;
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(publicComment.Id, Assert.Single(list.Items).Id);
        Assert.Empty((await requester.GetFromJsonAsync<PagedResult<TicketCommentDetails>>(Comments(ticket.Id) + "?isInternal=true"))!.Items);
        var events = (await requester.GetFromJsonAsync<PagedResult<TicketActivityDetails>>(Activities(ticket.Id)))!;
        Assert.Equal(1, events.TotalCount);
        Assert.Equal(publicComment.Id, Assert.Single(events.Items).NewValue!.CommentId);
        foreach (var path in new[] { Comments(ticket.Id) + "/" + note.Id, Activities(ticket.Id) + "/" + internalActivity.Id })
        {
            using var denied = await requester.GetAsync(path);
            await ErrorAsync(denied, HttpStatusCode.NotFound, "NotFound");
        }
        foreach (var path in new[] { Comments(ticket.Id), Activities(ticket.Id), $"/api/v1/tickets/{ticket.Id}", $"/api/v1/tickets?ticketNumber={ticket.TicketNumber}" })
        {
            using var response = await requester.GetAsync(path);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("internal-secret-marker", json);
            Assert.DoesNotContain(note.Id.ToString(), json);
            Assert.DoesNotContain(internalActivity.Id.ToString(), json);
            Assert.DoesNotContain(TicketActivityActions.InternalNoteAdded, json);
        }
        var visibleToAgent = (await support.GetFromJsonAsync<PagedResult<TicketCommentDetails>>(Comments(ticket.Id)))!;
        Assert.Equal(2, visibleToAgent.TotalCount);
        Assert.Equal(note.Id, Assert.Single((await support.GetFromJsonAsync<PagedResult<TicketCommentDetails>>(Comments(ticket.Id) + "?isInternal=true"))!.Items).Id);
        Assert.Equal(2, (await support.GetFromJsonAsync<PagedResult<TicketActivityDetails>>(Activities(ticket.Id)))!.TotalCount);
        using var eventDetail = await support.GetAsync(Activities(ticket.Id) + "/" + internalActivity.Id);
        Assert.Equal(HttpStatusCode.OK, eventDetail.StatusCode);
        Assert.DoesNotContain(note.Content, await eventDetail.Content.ReadAsStringAsync());
        var stored = await test.StoredAsync(ticket.Id);
        Assert.Equal(ticket.Version, stored.Version);
        Assert.Equal(ticket.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(TicketStatus.WaitingForRequester, stored.Status);
    }

    [PostgresTheory]
    [InlineData(RoleNames.SupportAgent)]
    [InlineData(RoleNames.SystemAdmin)]
    public async Task RequesterOfTicketCannotUseInternalScopeEvenWithPrivilegedRole(string role)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.UserAsync(role);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(owner.Id, test.TeamA);
        await test.MemberAsync(agent.Id, test.TeamA);
        var ticket = await test.TicketAsync(owner.Id, test.TeamA, agent.Id);
        using var ownerClient = await test.Accounts.ClientAsync(owner);
        using var support = await test.Accounts.ClientAsync(agent);
        var note = await Add(support, ticket.Id, "secret", true);
        using var read = await ownerClient.GetAsync(Comments(ticket.Id) + "/" + note.Id);
        await ErrorAsync(read, HttpStatusCode.NotFound, "NotFound");
        using var write = await ownerClient.PostAsJsonAsync(Comments(ticket.Id), new { Content = "Cannot bypass", IsInternal = true });
        await ErrorAsync(write, HttpStatusCode.Forbidden, "Forbidden");
        Assert.Equal(0, (await ownerClient.GetFromJsonAsync<PagedResult<TicketCommentDetails>>(Comments(ticket.Id)))!.TotalCount);
        Assert.Equal(0, (await ownerClient.GetFromJsonAsync<PagedResult<TicketActivityDetails>>(Activities(ticket.Id)))!.TotalCount);
    }

    [PostgresFact]
    public async Task PublicReadDoesNotNeedWriteGrantAndInternalAndPublicWritePermissionsAreIndependent()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.UserAsync(RoleNames.Requester);
        var ticket = await test.TicketAsync(owner.Id, test.TeamA);
        using var requester = await test.Accounts.ClientAsync(owner);
        var comment = await Add(requester, ticket.Id, "Public");
        var reader = await test.CustomUserAsync(PermissionCodes.TicketReadAll);
        using var readOnly = await test.Accounts.ClientAsync(reader);
        Assert.Single((await readOnly.GetFromJsonAsync<PagedResult<TicketCommentDetails>>(Comments(ticket.Id)))!.Items);
        foreach (var isInternal in new[] { false, true })
        {
            using var denied = await readOnly.PostAsJsonAsync(Comments(ticket.Id), new { Content = "No write grant", IsInternal = isInternal });
            await ErrorAsync(denied, HttpStatusCode.Forbidden, "Forbidden");
        }
        var internalOnly = await test.CustomUserAsync(PermissionCodes.TicketReadAll, PermissionCodes.TicketCommentInternal);
        using var internalClient = await test.Accounts.ClientAsync(internalOnly);
        var note = await Add(internalClient, ticket.Id, "Private", true);
        using var noPublic = await internalClient.PostAsJsonAsync(Comments(ticket.Id), new { Content = "Public denied" });
        await ErrorAsync(noPublic, HttpStatusCode.Forbidden, "Forbidden");
        var publicOnly = await test.CustomUserAsync(PermissionCodes.TicketReadAll, PermissionCodes.TicketCommentPublic);
        using var publicClient = await test.Accounts.ClientAsync(publicOnly);
        await Add(publicClient, ticket.Id, "Allowed public");
        using var noInternalRead = await publicClient.GetAsync(Comments(ticket.Id) + "/" + note.Id);
        await ErrorAsync(noInternalRead, HttpStatusCode.NotFound, "NotFound");
        var noRead = await test.CustomUserAsync(PermissionCodes.TicketCommentInternal, PermissionCodes.TicketCommentPublic);
        using var noReadClient = await test.Accounts.ClientAsync(noRead);
        using var noReadWrite = await noReadClient.PostAsJsonAsync(Comments(ticket.Id), new { Content = "No read scope" });
        await ErrorAsync(noReadWrite, HttpStatusCode.Forbidden, "Forbidden");
    }

    [PostgresFact]
    public async Task NestedIdsMustBelongToReadableTicketAndAuthorDoesNotBypassCurrentScope()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(agent.Id, test.TeamA);
        var first = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA, agent.Id);
        var second = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamB);
        using var client = await test.Accounts.ClientAsync(agent);
        var note = await Add(client, first.Id, "hidden-marker", true);
        await using var database = test.Accounts.Database();
        var activity = await database.TicketActivities.SingleAsync(x => x.TicketId == first.Id);
        // Even a global reader of both Tickets cannot move a nested ID between parents.
        var global = await test.CustomUserAsync(PermissionCodes.TicketReadAll, PermissionCodes.TicketCommentInternal);
        using var globalClient = await test.Accounts.ClientAsync(global);
        foreach (var path in new[] { Comments(second.Id) + "/" + note.Id, Activities(second.Id) + "/" + activity.Id,
            Comments(first.Id) + "/" + Guid.NewGuid(), Activities(first.Id) + "/" + Guid.NewGuid() })
        {
            using var denied = await globalClient.GetAsync(path);
            await ErrorAsync(denied, HttpStatusCode.NotFound, "NotFound");
        }
        foreach (var path in new[] { Comments(second.Id), Activities(second.Id) })
        {
            using var outside = await client.GetAsync(path);
            await ErrorAsync(outside, HttpStatusCode.NotFound, "NotFound");
        }
        using var outsideWrite = await client.PostAsJsonAsync(Comments(second.Id), new { Content = "Cannot claim Ticket", IsInternal = true });
        await ErrorAsync(outsideWrite, HttpStatusCode.NotFound, "NotFound");
        // Move the source away: the original author's ID is not an access grant.
        await database.Tickets.Where(x => x.Id == first.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AssignedTeamId, test.TeamB)
            .SetProperty(x => x.AssignedAgentId, (Guid?)null));
        using var lost = await client.GetAsync(Comments(first.Id) + "/" + note.Id);
        await ErrorAsync(lost, HttpStatusCode.NotFound, "NotFound");
    }

    [PostgresFact]
    public async Task VisibilityIsAppliedBeforeCountsPagingFiltersAndStableOrdering()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.UserAsync(RoleNames.Requester);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(agent.Id, test.TeamA);
        var ticket = await test.TicketAsync(owner.Id, test.TeamA);
        using var requester = await test.Accounts.ClientAsync(owner);
        using var support = await test.Accounts.ClientAsync(agent);
        var publicIds = new List<Guid>();
        for (var index = 0; index < 6; index++)
        {
            var internalNote = index % 3 == 0;
            var item = await Add(internalNote ? support : requester, ticket.Id, "Comment " + index, internalNote);
            if (!internalNote) publicIds.Add(item.Id);
        }
        await using var database = test.Accounts.Database();
        var sameTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await database.TicketComments.Where(x => x.TicketId == ticket.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, sameTime));
        var first = (await requester.GetFromJsonAsync<PagedResult<TicketCommentDetails>>(Comments(ticket.Id) + "?pageSize=2"))!;
        var second = (await requester.GetFromJsonAsync<PagedResult<TicketCommentDetails>>(Comments(ticket.Id) + "?pageSize=2&page=2"))!;
        Assert.Equal(4, first.TotalCount);
        Assert.Equal(4, second.TotalCount);
        Assert.Equal(4, first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count());
        var repeated = (await requester.GetFromJsonAsync<PagedResult<TicketCommentDetails>>(Comments(ticket.Id) + "?pageSize=2"))!;
        Assert.Equal(first.Items, repeated.Items);
        Assert.Equal(publicIds.Order(), first.Items.Concat(second.Items).Select(x => x.Id).Order());
        Assert.Empty((await requester.GetFromJsonAsync<PagedResult<TicketCommentDetails>>(Comments(ticket.Id) + "?pageSize=2&page=3"))!.Items);
        foreach (var path in new[] { Comments(ticket.Id), Activities(ticket.Id) })
        {
            var hidden = (await requester.GetFromJsonAsync<JsonElement>(path + "?isInternal=true"));
            Assert.Equal(0, hidden.GetProperty("totalCount").GetInt32());
            Assert.Empty(hidden.GetProperty("items").EnumerateArray());
            var visible = await support.GetFromJsonAsync<JsonElement>(path + "?isInternal=true");
            Assert.Equal(2, visible.GetProperty("totalCount").GetInt32());
            var descending = await requester.GetFromJsonAsync<JsonElement>(path + "?descending=true&pageSize=1");
            Assert.Equal(4, descending.GetProperty("totalCount").GetInt32());
        }
    }

    [PostgresTheory]
    [InlineData("grant")]
    [InlineData("membership")]
    [InlineData("team")]
    [InlineData("department")]
    [InlineData("inactive-user")]
    [InlineData("session")]
    public async Task SameJwtLosesInternalReadsAndWritesAfterAuthorityRevocation(string change)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var agent = await test.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketCommentInternal);
        await test.MemberAsync(agent.Id, test.TeamA);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA);
        using var client = await test.Accounts.ClientAsync(agent);
        var note = await Add(client, ticket.Id, "revocation-secret-marker", true);
        var actor = Actor(client);
        await using var database = test.Accounts.Database();
        var activityId = await database.TicketActivities.Where(x => x.TicketId == ticket.Id).Select(x => x.Id).SingleAsync();
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)");
        if (change == "membership") await database.SupportTeamMembers.Where(x => x.UserId == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else if (change == "team") await database.SupportTeams.Where(x => x.Id == test.TeamA).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else if (change == "department") await database.Departments.Where(x => x.Id == test.DepartmentA).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else if (change == "inactive-user") await database.Users.Where(x => x.Id == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else if (change == "session") await database.RefreshTokens.Where(x => x.Id == actor.SessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        else
        {
            var roleIds = database.UserRoles.Where(x => x.UserId == agent.Id).Select(x => x.RoleId);
            var permission = await database.Permissions.Where(x => x.Code == PermissionCodes.TicketCommentInternal).Select(x => x.Id).SingleAsync();
            await database.RolePermissions.Where(x => roleIds.Contains(x.RoleId) && x.PermissionId == permission).ExecuteDeleteAsync();
        }
        var pending = client.PostAsJsonAsync(Comments(ticket.Id), new { Content = "Must not commit", IsInternal = true });
        await transaction.CommitAsync();
        using var write = await pending;
        Assert.Equal(change is "inactive-user" or "session" ? HttpStatusCode.Unauthorized : change == "grant" ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound, write.StatusCode);
        foreach (var path in new[] { Comments(ticket.Id) + "/" + note.Id, Activities(ticket.Id) + "/" + activityId })
        {
            using var read = await client.GetAsync(path);
            Assert.Equal(change is "inactive-user" or "session" ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound, read.StatusCode);
            Assert.DoesNotContain(note.Content, await read.Content.ReadAsStringAsync());
        }
        Assert.Equal(1, await database.TicketComments.CountAsync(x => x.TicketId == ticket.Id));
        Assert.Equal(1, await database.TicketActivities.CountAsync(x => x.TicketId == ticket.Id));
    }

    [PostgresTheory]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public async Task TerminalTicketsKeepVisibleHistoryButRejectNewComments(TicketStatus terminal)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(agent.Id, test.TeamA);
        var owner = await test.UserAsync(RoleNames.Requester);
        var ticket = await test.TicketAsync(owner.Id, test.TeamA);
        using var client = await test.Accounts.ClientAsync(agent);
        var note = await Add(client, ticket.Id, "Old note", true);
        await using var database = test.Accounts.Database();
        await database.Tickets.Where(x => x.Id == ticket.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, terminal)
            .SetProperty(x => x.ResolutionSummary, "Result").SetProperty(x => x.ResolvedAt, DateTime.UtcNow).SetProperty(x => x.ClosedAt, DateTime.UtcNow));
        foreach (var isInternal in new[] { false, true })
        {
            using var denied = await client.PostAsJsonAsync(Comments(ticket.Id), new { Content = "Too late", IsInternal = isInternal });
            await ErrorAsync(denied, HttpStatusCode.Forbidden, "Forbidden");
        }
        using var history = await client.GetAsync(Comments(ticket.Id) + "/" + note.Id);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
    }

    [PostgresTheory]
    [InlineData("blank")]
    [InlineData("long")]
    [InlineData("authorId")]
    [InlineData("ticketId")]
    [InlineData("createdAt")]
    [InlineData("updatedAt")]
    [InlineData("version")]
    [InlineData("attachments")]
    public async Task InvalidOrOverpostedFieldsCannotCreateComment(string failure)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id);
        var body = new Dictionary<string, object?> { ["content"] = "private-body-marker" };
        if (failure == "blank") body["content"] = "   ";
        else if (failure == "long") body["content"] = new string('x', 20_001);
        else body[failure] = "forged-value-marker";
        using var response = await test.Accounts.Admin.PostAsJsonAsync(Comments(ticket.Id), body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-body-marker", error);
        Assert.DoesNotContain("forged-value-marker", error);
        await using var database = test.Accounts.Database();
        Assert.False(await database.TicketComments.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.False(await database.TicketActivities.AnyAsync(x => x.TicketId == ticket.Id));
    }

    [PostgresTheory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("isInternal=not-bool")]
    public async Task PagingValidationAndAnonymousRequestsAreHandledForBothResources(string query)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id);
        using var anonymous = test.Accounts.Factory.CreateClient();
        foreach (var path in new[] { Comments(ticket.Id), Activities(ticket.Id) })
        {
            using var invalid = await test.Accounts.Admin.GetAsync(path + "?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var missingToken = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);
        }
        using var post = await anonymous.PostAsJsonAsync(Comments(ticket.Id), new { Content = "No token" });
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [PostgresFact]
    public async Task ConcurrentImmutableAppendsDoNotLoseCommentsOrTouchTicketVersion()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => Add(test.Accounts.Admin, ticket.Id, "Concurrent " + index)));
        Assert.Equal(6, results.Select(x => x.Id).Distinct().Count());
        await using var database = test.Accounts.Database();
        Assert.Equal(6, await database.TicketComments.CountAsync(x => x.TicketId == ticket.Id));
        Assert.Equal(6, await database.TicketActivities.CountAsync(x => x.TicketId == ticket.Id));
        Assert.Equal(ticket.Version, (await test.StoredAsync(ticket.Id)).Version);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAfterSqlSaveRollsBackCommentAndActivityWithoutLeakingBody(bool isInternal)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.UserAsync(RoleNames.Requester);
        var ticket = await test.TicketAsync(owner.Id);
        var probe = new FailAfterDiscussionSave();
        await using var factory = test.Accounts.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Scoped<HelpdeskDbContext>(_ => new(new DbContextOptionsBuilder<HelpdeskDbContext>()
                .UseNpgsql(fixture.ConnectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk"))
                .AddInterceptors(probe).Options)))));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = test.Accounts.Admin.DefaultRequestHeaders.Authorization;
        using var response = await client.PostAsJsonAsync(Comments(ticket.Id), new { Content = "rollback-private-marker", IsInternal = isInternal });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(probe.Saved);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("rollback-private-marker", body);
        Assert.DoesNotContain("secret-marker", body);
        await using var database = test.Accounts.Database();
        Assert.False(await database.TicketComments.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.False(await database.TicketActivities.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.Equal(ticket.Version, (await test.StoredAsync(ticket.Id)).Version);
    }

    private sealed class FailAfterDiscussionSave : SaveChangesInterceptor
    {
        public bool Saved { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Saved = eventData.Context!.ChangeTracker.Entries<TicketComment>().Any(x => x.State == EntityState.Unchanged);
            if (Saved) throw new InvalidOperationException("secret-marker: discussion save failed before commit");
            return ValueTask.FromResult(result);
        }
    }
}
