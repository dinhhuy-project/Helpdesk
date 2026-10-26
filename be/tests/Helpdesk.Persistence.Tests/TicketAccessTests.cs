using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Enums;
using be.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Helpdesk.Persistence.Tests.TicketAccessTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class TicketAccessTests(PostgresFixture fixture)
{
    [PostgresTheory]
    [InlineData("requester", true, false, false, 404)]
    [InlineData("creator", false, false, false, 404)]
    [InlineData("assignee", true, true, false, 404)]
    [InlineData("teammate", true, false, false, 404)]
    [InlineData("leader", true, true, true, 404)]
    [InlineData("leader-role-only", true, false, false, 404)]
    [InlineData("team-manager-only", false, false, false, 403)]
    [InlineData("outsider", false, false, false, 404)]
    [InlineData("same-department", false, false, false, 404)]
    [InlineData("assignee-without-membership", false, false, false, 404)]
    [InlineData("inactive-member", false, false, false, 404)]
    [InlineData("read-all", true, false, false, 404)]
    [InlineData("global-operator", true, true, true, 404)]
    [InlineData("operation-without-read", false, false, false, 403)]
    [InlineData("admin", true, true, true, 404)]
    public async Task MatrixMatchesCheckerListDetailAndActualGuardedWrites(string relation, bool read, bool update, bool assign, int denied)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var user = relation switch
        {
            "admin" => test.Accounts.AdminUser,
            "read-all" => await test.CustomUserAsync(PermissionCodes.TicketReadAll),
            "global-operator" => await test.CustomUserAsync(PermissionCodes.TicketReadAll, PermissionCodes.TicketUpdateStatus, PermissionCodes.TicketAssign),
            "operation-without-read" => await test.CustomUserAsync(PermissionCodes.TicketUpdateStatus, PermissionCodes.TicketAssign),
            "team-manager-only" => await test.CustomUserAsync(PermissionCodes.TeamManage),
            "requester" or "creator" => await test.UserAsync(RoleNames.Requester),
            "leader" or "leader-role-only" => await test.UserAsync(RoleNames.TeamLeader),
            _ => await test.UserAsync(RoleNames.SupportAgent)
        };
        if (relation is "assignee" or "teammate" or "leader" or "leader-role-only" or "team-manager-only" or "operation-without-read" or "inactive-member")
            await test.MemberAsync(user.Id, test.TeamA, relation is "leader" or "team-manager-only" or "operation-without-read", relation != "inactive-member");
        if (relation == "outsider") await test.MemberAsync(user.Id, test.TeamB);
        if (relation == "same-department")
        {
            await using var database = test.Accounts.Database();
            await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DepartmentId, test.DepartmentA));
        }
        var ticket = await test.TicketAsync(relation == "requester" ? user.Id : test.Accounts.AdminUser.Id, test.TeamA,
            relation is "assignee" or "assignee-without-membership" or "inactive-member" ? user.Id : test.Accounts.AdminUser.Id,
            relation == "creator" ? user.Id : test.Accounts.AdminUser.Id);
        using var client = await test.Accounts.ClientAsync(user);
        await using var scope = test.Accounts.Factory.Services.CreateAsyncScope();
        var checker = scope.ServiceProvider.GetRequiredService<ITicketAccessChecker>();
        Assert.Equal(new TicketAccess(read, update, assign), await checker.GetAsync(Actor(client), ticket.Id, CancellationToken.None));
        using var detail = await client.GetAsync($"/api/v1/tickets/{ticket.Id}");
        Assert.Equal(read ? HttpStatusCode.OK : (HttpStatusCode)denied, detail.StatusCode);
        using var flags = await client.GetAsync($"/api/v1/tickets/{ticket.Id}/access");
        Assert.Equal(read ? HttpStatusCode.OK : (HttpStatusCode)denied, flags.StatusCode);
        if (read) Assert.Equal(new TicketAccess(read, update, assign), await flags.Content.ReadFromJsonAsync<TicketAccess>());
        else Assert.DoesNotContain(ticket.Description, await detail.Content.ReadAsStringAsync());
        using var list = await client.GetAsync($"/api/v1/tickets?search={test.Prefix}");
        if (denied == 403) Assert.Equal(read ? HttpStatusCode.OK : HttpStatusCode.Forbidden, list.StatusCode);
        else Assert.Equal(read ? 1 : 0, (await list.Content.ReadFromJsonAsync<PagedResult<TicketSummary>>())!.TotalCount);

        using var process = await ProcessAsync(client, ticket);
        Assert.Equal(update ? HttpStatusCode.NoContent : read ? HttpStatusCode.Forbidden : (HttpStatusCode)denied, process.StatusCode);
        var afterProcess = await test.StoredAsync(ticket.Id);
        Assert.Equal(update ? TicketStatus.InProgress : TicketStatus.New, afterProcess.Status);
        if (!update) Assert.Equal(ticket.Version, afterProcess.Version);
        using var assignment = await AssignAsync(client, afterProcess, test.TeamA);
        Assert.Equal(assign ? HttpStatusCode.NoContent : read ? HttpStatusCode.Forbidden : (HttpStatusCode)denied, assignment.StatusCode);
        var afterAssign = await test.StoredAsync(ticket.Id);
        if (!assign) Assert.Equal(afterProcess.Version, afterAssign.Version);
        await using var verify = test.Accounts.Database();
        Assert.Equal((update ? 1 : 0) + (assign ? 1 : 0), await verify.TicketActivities.CountAsync(x => x.TicketId == ticket.Id));
    }

    [PostgresFact]
    public async Task ReadScopesCombineWithOrBeforeFiltersCountAndPagingWithoutDuplicateTickets()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var user = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(user.Id, test.TeamA);
        var ownAndTeam = await test.TicketAsync(user.Id, test.TeamA, user.Id);
        var teamOnly = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA);
        var hidden = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamB);
        await test.TicketAsync(test.Accounts.AdminUser.Id);
        using var client = await test.Accounts.ClientAsync(user);
        var page = await client.GetFromJsonAsync<PagedResult<TicketSummary>>($"/api/v1/tickets?search={test.Prefix}&pageSize=1");
        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(teamOnly.Id, Assert.Single(page.Items).Id);
        var second = await client.GetFromJsonAsync<PagedResult<TicketSummary>>($"/api/v1/tickets?search={test.Prefix}&pageSize=1&page=2");
        Assert.Equal(ownAndTeam.Id, Assert.Single(second!.Items).Id);
        var inaccessibleFilter = await client.GetFromJsonAsync<PagedResult<TicketSummary>>($"/api/v1/tickets?search={test.Prefix}&teamId={test.TeamB}");
        Assert.Equal(0, inaccessibleFilter!.TotalCount);
        using var denied = await client.GetAsync($"/api/v1/tickets/{hidden.Id}");
        await ErrorAsync(denied, HttpStatusCode.NotFound, "NotFound");
        using var missing = await client.GetAsync($"/api/v1/tickets/{Guid.NewGuid()}");
        await ErrorAsync(missing, HttpStatusCode.NotFound, "NotFound");
        client.DefaultRequestHeaders.Add("X-User-Id", test.Accounts.AdminUser.Id.ToString());
        client.DefaultRequestHeaders.Add("X-Role", RoleNames.SystemAdmin);
        using var forged = await client.GetAsync($"/api/v1/tickets/{hidden.Id}?requesterId={user.Id}&permission=ticket.read.all");
        Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
    }

    [PostgresTheory]
    [InlineData("membership", 404)]
    [InlineData("team", 404)]
    [InlineData("department", 404)]
    [InlineData("read-grant", 404)]
    [InlineData("write-grant", 403)]
    [InlineData("assignee", 403)]
    [InlineData("reassigned-team", 404)]
    [InlineData("user", 401)]
    [InlineData("session", 401)]
    public async Task CurrentStateChangesRevokeAccessOnSameJwtAndInsideWriteGuard(string change, int denied)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var user = await test.CustomUserAsync(PermissionCodes.TicketReadOwn, PermissionCodes.TicketReadTeam, PermissionCodes.TicketUpdateStatus);
        await test.MemberAsync(user.Id, test.TeamA);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA, user.Id);
        using var client = await test.Accounts.ClientAsync(user);
        var actor = Actor(client);
        await using var scope = test.Accounts.Factory.Services.CreateAsyncScope();
        var checker = scope.ServiceProvider.GetRequiredService<ITicketAccessChecker>();
        Assert.True(await checker.CanAsync(actor, ticket.Id, TicketOperation.UpdateStatus, CancellationToken.None));
        await using var database = test.Accounts.Database();
        if (change == "membership") await database.SupportTeamMembers.Where(x => x.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else if (change == "team") await database.SupportTeams.Where(x => x.Id == test.TeamA).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else if (change == "department") await database.Departments.Where(x => x.Id == test.DepartmentA).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else if (change is "read-grant" or "write-grant")
        {
            var role = await database.UserRoles.Where(x => x.UserId == user.Id).Select(x => x.RoleId).SingleAsync();
            var code = change == "read-grant" ? PermissionCodes.TicketReadTeam : PermissionCodes.TicketUpdateStatus;
            var permission = await database.Permissions.Where(x => x.Code == code).Select(x => x.Id).SingleAsync();
            await database.RolePermissions.Where(x => x.RoleId == role && x.PermissionId == permission).ExecuteDeleteAsync();
        }
        else if (change == "assignee") await database.Tickets.Where(x => x.Id == ticket.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AssignedAgentId, test.Accounts.AdminUser.Id));
        else if (change == "reassigned-team") await database.Tickets.Where(x => x.Id == ticket.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AssignedTeamId, test.TeamB));
        else if (change == "user") await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else await database.RefreshTokens.Where(x => x.Id == actor.SessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        Assert.False(await checker.CanAsync(actor, ticket.Id, TicketOperation.UpdateStatus, CancellationToken.None));
        using var process = await ProcessAsync(client, ticket);
        Assert.Equal((HttpStatusCode)denied, process.StatusCode);
        var stored = await test.StoredAsync(ticket.Id);
        Assert.Equal(TicketStatus.New, stored.Status);
        Assert.Equal(0, await database.TicketActivities.CountAsync(x => x.TicketId == ticket.Id));

        // Direct call bypasses HTTP middleware to verify authorization is repeated in the transaction.
        var guard = scope.ServiceProvider.GetRequiredService<TicketWriteGuard>();
        var scopedDb = scope.ServiceProvider.GetRequiredService<be.Infrastructure.Persistence.HelpdeskDbContext>();
        await using var guardedTransaction = await scopedDb.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<TicketOperationException>(() => guard.LockAsync(actor, ticket.Id, TicketOperation.UpdateStatus, ticket.Version, CancellationToken.None));
        Assert.Equal(denied == 401 ? TicketError.Unauthenticated : denied == 403 ? TicketError.Forbidden : TicketError.NotFound, error.Error);
    }

    [PostgresFact]
    public async Task RequesterAndGlobalReaderKeepHistoricalAccessWhenOrganizationOrCategoryIsInactive()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var requester = await test.UserAsync(RoleNames.Requester);
        var ticket = await test.TicketAsync(requester.Id, test.TeamA);
        await using var database = test.Accounts.Database();
        await database.Departments.Where(x => x.Id == test.DepartmentA).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await database.TicketCategories.Where(x => x.Id == test.Category).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        using var client = await test.Accounts.ClientAsync(requester);
        using var own = await client.GetAsync($"/api/v1/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var admin = await test.Accounts.Admin.GetAsync($"/api/v1/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [PostgresFact]
    public async Task SystemAdminNameDoesNotBypassMissingGlobalReadGrant()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var other = await test.UserAsync(RoleNames.Requester);
        var ticket = await test.TicketAsync(other.Id, test.TeamA);
        var adminRole = await test.Accounts.RoleIdAsync(RoleNames.SystemAdmin);
        await test.Accounts.SnapshotAsync(adminRole);
        await using var database = test.Accounts.Database();
        var grant = await database.Permissions.Where(x => x.Code == PermissionCodes.TicketReadAll).Select(x => x.Id).SingleAsync();
        await database.RolePermissions.Where(x => x.RoleId == adminRole && x.PermissionId == grant).ExecuteDeleteAsync();
        using var response = await test.Accounts.Admin.GetAsync($"/api/v1/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var process = await ProcessAsync(test.Accounts.Admin, ticket);
        Assert.Equal(HttpStatusCode.NotFound, process.StatusCode);
        Assert.Equal(ticket.Version, (await test.StoredAsync(ticket.Id)).Version);
    }

    [PostgresTheory]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public async Task TerminalTicketsRemainReadableButHaveNoProcessingOrAssignmentScope(TicketStatus status)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA, status: status);
        var flags = await test.Accounts.Admin.GetFromJsonAsync<TicketAccess>($"/api/v1/tickets/{ticket.Id}/access");
        Assert.Equal(new TicketAccess(true, false, false), flags);
        using var process = await ProcessAsync(test.Accounts.Admin, ticket);
        Assert.Equal(HttpStatusCode.Forbidden, process.StatusCode);
        using var assign = await AssignAsync(test.Accounts.Admin, ticket, test.TeamA);
        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
    }

    [PostgresFact]
    public async Task ConcurrentWritesRequireCurrentVersionAndCreateOnlyOneActivity()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id);
        var responses = await Task.WhenAll(ProcessAsync(test.Accounts.Admin, ticket), ProcessAsync(test.Accounts.Admin, ticket));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
            await ErrorAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "VersionConflict");
            await using var database = test.Accounts.Database();
            Assert.Equal(1, await database.TicketActivities.CountAsync(x => x.TicketId == ticket.Id));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresFact]
    public async Task GuardRequiresTransactionAndRejectsDirtyTicketBeforeAnyWrite()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id);
        var actor = Actor(test.Accounts.Admin);
        await using var scope = test.Accounts.Factory.Services.CreateAsyncScope();
        var guard = scope.ServiceProvider.GetRequiredService<TicketWriteGuard>();
        var database = scope.ServiceProvider.GetRequiredService<be.Infrastructure.Persistence.HelpdeskDbContext>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.LockAsync(actor, ticket.Id, TicketOperation.UpdateStatus, ticket.Version, CancellationToken.None));
        var tracked = await database.Tickets.SingleAsync(x => x.Id == ticket.Id);
        tracked.Title = "Must not be saved";
        await using var transaction = await database.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.LockAsync(actor, ticket.Id, TicketOperation.UpdateStatus, ticket.Version, CancellationToken.None));
        Assert.Equal(ticket.Title, (await test.StoredAsync(ticket.Id)).Title);
    }

    [PostgresTheory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=999")]
    [InlineData("priority=0")]
    [InlineData("teamId=00000000-0000-0000-0000-000000000000")]
    public async Task ListValidationDoesNotChangeScope(string query)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        using var response = await test.Accounts.Admin.GetAsync("/api/v1/tickets?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [PostgresFact]
    public async Task InvalidIdentityUnknownOperationAndWrongSessionOwnerFailClosed()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id);
        await using var scope = test.Accounts.Factory.Services.CreateAsyncScope();
        var checker = scope.ServiceProvider.GetRequiredService<ITicketAccessChecker>();
        var actor = Actor(test.Accounts.Admin);
        foreach (var invalid in new[] { actor with { UserId = Guid.NewGuid() }, actor with { UserId = Guid.Empty }, actor with { SessionId = Guid.Empty } })
            Assert.Equal(new TicketAccess(false, false, false), await checker.GetAsync(invalid, ticket.Id, CancellationToken.None));
        Assert.False(await checker.CanAsync(actor, ticket.Id, (TicketOperation)999, CancellationToken.None));
        using var anonymous = test.Accounts.Factory.CreateClient();
        using var response = await anonymous.GetAsync("/api/v1/tickets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var swagger = await anonymous.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = swagger.GetProperty("paths");
        var operation = paths.GetProperty("/api/v1/tickets").GetProperty("get");
        Assert.True(operation.TryGetProperty("security", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("403", out _));
        Assert.True(paths.GetProperty("/api/v1/tickets").GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));
        Assert.False(paths.TryGetProperty("/api/v1/tickets/{id}/status", out _));
        foreach (var route in new[] { "/api/v1/tickets/{id}/assign", "/api/v1/tickets/{id}/transition" })
        {
            var command = paths.GetProperty(route).GetProperty("post");
            Assert.True(command.TryGetProperty("security", out _));
            Assert.True(command.GetProperty("responses").TryGetProperty("409", out _));
        }
    }
}
