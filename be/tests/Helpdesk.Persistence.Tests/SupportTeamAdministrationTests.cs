using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.SupportTeams;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using TestAccounts = Helpdesk.Persistence.Tests.AccountAdministrationTests.TestAccounts;
using TestUser = Helpdesk.Persistence.Tests.AccountAdministrationTests.TestUser;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class SupportTeamAdministrationTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task FrontendLookupsAndCapabilitiesUseTeamScopeWithoutUserOrDepartmentManagement()
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var own = await test.CreateTeamAsync("Own");
        var outsideDepartment = await test.DepartmentAsync();
        var outside = await test.CreateTeamAsync("Outside", outsideDepartment);
        var leader = await test.LeaderAsync(own.Id);
        var candidate = await test.Accounts.NewUserAsync();
        using var client = await test.Accounts.ClientAsync(leader);
        var global = await client.GetFromJsonAsync<TeamAdministrationAccess>("/api/v1/support-teams/administration-access");
        Assert.NotNull(global);
        Assert.False(global.CanCreate || global.CanManageLeadership || global.CanChangeStatus);
        var access = await client.GetFromJsonAsync<TeamAdministrationAccess>($"/api/v1/support-teams/{own.Id}/administration-access");
        Assert.NotNull(access);
        Assert.True(access.CanAddMembers);
        var departments = await client.GetFromJsonAsync<PagedResult<be.Application.Features.Departments.DepartmentLookup>>(
            $"/api/v1/support-teams/department-lookup?search={test.Prefix}&pageSize=1");
        Assert.NotNull(departments);
        Assert.Equal(1, departments.TotalCount);
        Assert.Equal(own.DepartmentId, Assert.Single(departments.Items).Id);
        var candidates = await client.GetFromJsonAsync<PagedResult<TeamMemberCandidate>>($"/api/v1/support-teams/{own.Id}/member-candidates?pageSize=100");
        Assert.NotNull(candidates);
        Assert.Contains(candidates.Items, x => x.Id == candidate.Id);
        Assert.DoesNotContain(candidates.Items, x => x.Id == leader.Id);
        using var userList = await client.GetAsync("/api/v1/users");
        using var departmentList = await client.GetAsync("/api/v1/departments");
        Assert.Equal(HttpStatusCode.Forbidden, userList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, departmentList.StatusCode);
        foreach (var suffix in new[] { "administration-access", "member-candidates" })
        {
            using var hidden = await client.GetAsync($"/api/v1/support-teams/{outside.Id}/{suffix}");
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        var added = await test.AddAsync(own.Id, candidate.Id, client);
        Assert.False(added.Member.CanBeLeader);
        using var removed = await client.DeleteAsync($"/api/v1/support-teams/{own.Id}/members/{candidate.Id}?version={added.Member.Version}&teamVersion={added.Team.Version}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        candidates = await client.GetFromJsonAsync<PagedResult<TeamMemberCandidate>>($"/api/v1/support-teams/{own.Id}/member-candidates?pageSize=100");
        Assert.DoesNotContain(candidates!.Items, x => x.Id == candidate.Id); // Inactive rows must be reactivated.
        var members = await client.GetFromJsonAsync<PagedResult<TeamMemberDetails>>($"/api/v1/support-teams/{own.Id}/members");
        Assert.True(Assert.Single(members!.Items, x => x.UserId == leader.Id).CanBeLeader);
        Assert.DoesNotContain("email", await (await client.GetAsync($"/api/v1/support-teams/{own.Id}/member-candidates")).Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        using var invalid = await client.GetAsync($"/api/v1/support-teams/{own.Id}/member-candidates?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [PostgresFact]
    public async Task TeamCapabilitiesAndLookupsRecheckInactiveParentsAndRevokedCurrentGrant()
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var team = await test.CreateTeamAsync();
        var leader = await test.LeaderAsync(team.Id);
        using var client = await test.Accounts.ClientAsync(leader);
        await using var database = test.Accounts.Database();
        var name = await database.Departments.Where(x => x.Id == team.DepartmentId).Select(x => x.Name).SingleAsync();
        Assert.Equal(name, team.DepartmentName);
        var admin = await test.Accounts.Admin.GetFromJsonAsync<TeamAdministrationAccess>($"/api/v1/support-teams/{team.Id}/administration-access");
        Assert.True(admin!.CanCreate && admin.CanManageLeadership && admin.CanChangeStatus && admin.CanAddMembers);
        await database.Departments.Where(x => x.Id == team.DepartmentId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsActive, false));
        using var hidden = await client.GetAsync($"/api/v1/support-teams/{team.Id}/member-candidates");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        admin = await test.Accounts.Admin.GetFromJsonAsync<TeamAdministrationAccess>($"/api/v1/support-teams/{team.Id}/administration-access");
        Assert.False(admin!.CanAddMembers);
        var members = await test.Accounts.Admin.GetFromJsonAsync<PagedResult<TeamMemberDetails>>($"/api/v1/support-teams/{team.Id}/members");
        Assert.All(members!.Items, x => Assert.False(x.CanBeLeader));
        using var inactive = await test.Accounts.Admin.GetAsync($"/api/v1/support-teams/{team.Id}/member-candidates");
        Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
        var current = await test.Accounts.GetAsync(leader.Id);
        using var revoked = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/users/{leader.Id}/roles", new { RoleIds = Array.Empty<Guid>(), current.Version });
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        using var denied = await client.GetAsync("/api/v1/support-teams/administration-access");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [PostgresFact]
    public async Task TeamsHaveImmutableDepartmentAndNamesAreUniqueWithinDepartmentIncludingInactive()
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var first = await test.CreateTeamAsync("Finance");
        var otherDepartment = await test.DepartmentAsync();
        var sameName = await test.CreateTeamAsync("Finance", otherDepartment);
        Assert.NotEqual(first.Id, sameName.Id);
        Assert.True(first.Version > 0);
        using var inactive = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/support-teams/{first.Id}/status", new { IsActive = false, first.Version });
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        using var duplicate = await test.Accounts.Admin.PostAsJsonAsync("/api/v1/support-teams",
            new { first.DepartmentId, Name = " " + first.Name.ToUpperInvariant() + " " });
        await ErrorAsync(duplicate, HttpStatusCode.Conflict, "NameInUse");
        var list = await test.Accounts.Admin.GetFromJsonAsync<PagedResult<SupportTeamDetails>>(
            $"/api/v1/support-teams?departmentId={first.DepartmentId}&search={test.Prefix}&isActive=false&pageSize=1");
        Assert.Equal(1, list!.TotalCount);
        Assert.Equal(first.Id, Assert.Single(list.Items).Id);
        var current = await test.GetAsync(first.Id);
        using var update = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/support-teams/{first.Id}",
            new { current.Name, Description = "Updated", current.Version });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var changed = (await update.Content.ReadFromJsonAsync<SupportTeamDetails>())!;
        Assert.NotEqual(current.Version, changed.Version);
        using var move = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/support-teams/{first.Id}",
            new { changed.Name, DepartmentId = otherDepartment, changed.Version });
        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);
        using var deletion = await test.Accounts.Admin.DeleteAsync($"/api/v1/support-teams/{first.Id}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, deletion.StatusCode);
    }

    [PostgresFact]
    public async Task MembershipRemovalIsSoftAndReactivationPreservesJoinedAtAndSession()
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var team = await test.CreateTeamAsync();
        var user = await test.Accounts.NewUserAsync(); // No role or department required by membership itself.
        var added = await test.AddAsync(team.Id, user.Id);
        Assert.True(added.Member.IsActive);
        Assert.False(added.Member.IsLeader);
        Assert.NotEqual(team.Version, added.Team.Version);
        using var client = await test.Accounts.ClientAsync(user);
        using var removed = await test.Accounts.Admin.DeleteAsync($"/api/v1/support-teams/{team.Id}/members/{user.Id}?version={added.Member.Version}&teamVersion={added.Team.Version}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var member = await test.MemberAsync(team.Id, user.Id);
        Assert.False(member.IsActive);
        Assert.Equal(added.Member.JoinedAt, member.JoinedAt);
        Assert.NotEqual(added.Member.Version, member.Version);
        var current = await test.GetAsync(team.Id);
        using var duplicate = await test.Accounts.Admin.PostAsJsonAsync($"/api/v1/support-teams/{team.Id}/members",
            new { UserId = user.Id, TeamVersion = current.Version });
        await ErrorAsync(duplicate, HttpStatusCode.Conflict, "MembershipExists");
        using var activate = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/support-teams/{team.Id}/members/{user.Id}/status",
            new { IsActive = true, member.Version, TeamVersion = current.Version });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var restored = (await activate.Content.ReadFromJsonAsync<TeamMembershipResult>())!;
        Assert.Equal(added.Member.JoinedAt, restored.Member.JoinedAt);
        await using var database = test.Accounts.Database();
        var audits = await database.IdentityAuditLogs.CountAsync(x => x.ActorId == test.Accounts.AdminUser.Id);
        using var noOp = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/support-teams/{team.Id}/members/{user.Id}/status",
            new { IsActive = true, restored.Member.Version, TeamVersion = restored.Team.Version });
        Assert.Equal(restored, await noOp.Content.ReadFromJsonAsync<TeamMembershipResult>());
        Assert.Equal(audits, await database.IdentityAuditLogs.CountAsync(x => x.ActorId == test.Accounts.AdminUser.Id));
        using var me = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var cannotManage = await client.GetAsync($"/api/v1/support-teams/{team.Id}/members");
        Assert.Equal(HttpStatusCode.Forbidden, cannotManage.StatusCode);
    }

    [PostgresFact]
    public async Task LeadersAreScopedPerTeamAndCannotCreateTeamsOrGrantLeadershipOrRemoveLeaders()
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var own = await test.CreateTeamAsync("Own");
        var other = await test.CreateTeamAsync("Other");
        var leader = await test.LeaderAsync(own.Id);
        var peer = await test.LeaderAsync(own.Id);
        using var client = await test.Accounts.ClientAsync(leader);
        var list = await client.GetFromJsonAsync<PagedResult<SupportTeamDetails>>("/api/v1/support-teams?pageSize=1");
        Assert.Equal(1, list!.TotalCount);
        Assert.Equal(own.Id, Assert.Single(list.Items).Id);
        var hidden = await client.GetFromJsonAsync<PagedResult<SupportTeamDetails>>("/api/v1/support-teams?search=" + other.Name);
        Assert.Equal(0, hidden!.TotalCount);
        using var detail = await client.GetAsync($"/api/v1/support-teams/{other.Id}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        using var members = await client.GetAsync($"/api/v1/support-teams/{other.Id}/members");
        Assert.Equal(HttpStatusCode.NotFound, members.StatusCode);
        var user = await test.Accounts.NewUserAsync();
        using var crossTeamWrite = await client.PostAsJsonAsync($"/api/v1/support-teams/{other.Id}/members", new { UserId = user.Id, TeamVersion = other.Version });
        Assert.Equal(HttpStatusCode.NotFound, crossTeamWrite.StatusCode);
        var added = await test.AddAsync(own.Id, user.Id, client);
        using var edit = await client.PatchAsJsonAsync($"/api/v1/support-teams/{own.Id}",
            new { own.Name, Description = "Leader edit", Version = added.Team.Version });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var current = await test.GetAsync(own.Id);
        using var status = await client.PutAsJsonAsync($"/api/v1/support-teams/{own.Id}/status", new { IsActive = false, current.Version });
        Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
        using var create = await client.PostAsJsonAsync("/api/v1/support-teams", new { own.DepartmentId, Name = "Forbidden" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        using var promote = await client.PutAsJsonAsync($"/api/v1/support-teams/{own.Id}/members/{user.Id}/leadership",
            new { IsLeader = true, added.Member.Version, TeamVersion = current.Version });
        Assert.Equal(HttpStatusCode.Forbidden, promote.StatusCode);
        foreach (var protectedUser in new[] { leader.Id, peer.Id })
        {
            var member = await test.MemberAsync(own.Id, protectedUser);
            using var remove = await client.DeleteAsync($"/api/v1/support-teams/{own.Id}/members/{protectedUser}?version={member.Version}&teamVersion={current.Version}");
            Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
        }
        using var ordinary = await client.PutAsJsonAsync($"/api/v1/support-teams/{own.Id}/members/{user.Id}/status",
            new { IsActive = false, added.Member.Version, TeamVersion = current.Version });
        Assert.Equal(HttpStatusCode.OK, ordinary.StatusCode);
        using var roleGrant = await client.GetAsync("/api/v1/identity/roles");
        Assert.Equal(HttpStatusCode.Forbidden, roleGrant.StatusCode);
        using var users = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
    }

    [PostgresFact]
    public async Task RoleOrFlagAloneCannotBypassPermissionAndTeamScope()
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var team = await test.CreateTeamAsync();
        var user = await test.Accounts.NewUserAsync();
        await test.Accounts.AssignAsync(user.Id, await test.Accounts.RoleIdAsync(RoleNames.TeamLeader));
        using var client = await test.Accounts.ClientAsync(user);
        Assert.Empty((await client.GetFromJsonAsync<PagedResult<SupportTeamDetails>>("/api/v1/support-teams"))!.Items);
        var added = await test.AddAsync(team.Id, user.Id);
        using var ordinary = await client.GetAsync($"/api/v1/support-teams/{team.Id}");
        Assert.Equal(HttpStatusCode.NotFound, ordinary.StatusCode);
        var target = await test.Accounts.NewUserAsync();
        var targetMembership = await test.AddAsync(team.Id, target.Id);
        using var invalid = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/support-teams/{team.Id}/members/{target.Id}/leadership",
            new { IsLeader = true, targetMembership.Member.Version, TeamVersion = targetMembership.Team.Version });
        await ErrorAsync(invalid, HttpStatusCode.BadRequest, "InvalidLeader");
        await using var database = test.Accounts.Database();
        // A legacy/manual IsLeader flag must never grant the missing permission.
        await database.SupportTeamMembers.Where(x => x.SupportTeamId == team.Id && x.UserId == target.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsLeader, true));
        using var noGrantClient = await test.Accounts.ClientAsync(target);
        using var noGrant = await noGrantClient.GetAsync($"/api/v1/support-teams/{team.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, noGrant.StatusCode);
        using var anonymous = test.Accounts.Factory.CreateClient();
        using var noToken = await anonymous.GetAsync("/api/v1/support-teams");
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
    }

    [PostgresTheory]
    [InlineData("membership")]
    [InlineData("leadership")]
    [InlineData("team")]
    [InlineData("department")]
    [InlineData("grant")]
    [InlineData("user")]
    [InlineData("session")]
    public async Task ScopeAndPermissionRevocationTakeEffectOnSameJwtAndInsideMutation(string change)
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var team = await test.CreateTeamAsync();
        var leader = await test.LeaderAsync(team.Id);
        var target = await test.Accounts.NewUserAsync();
        var login = await test.Accounts.LoginAsync(leader);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        var actor = new AccountActor(leader.Id, Guid.Parse(token.Claims.Single(x => x.Type == "sid").Value));
        using var client = test.Accounts.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.AccessToken);
        await using var database = test.Accounts.Database();
        if (change == "membership") await database.SupportTeamMembers.Where(x => x.SupportTeamId == team.Id && x.UserId == leader.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else if (change == "leadership") await database.SupportTeamMembers.Where(x => x.SupportTeamId == team.Id && x.UserId == leader.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsLeader, false));
        else if (change == "team") await database.SupportTeams.Where(x => x.Id == team.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else if (change == "department") await database.Departments.Where(x => x.Id == team.DepartmentId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else if (change == "grant") await database.UserRoles.Where(x => x.UserId == leader.Id).ExecuteDeleteAsync();
        else if (change == "user") await database.Users.Where(x => x.Id == leader.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else await database.RefreshTokens.Where(x => x.UserId == leader.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        using var detail = await client.GetAsync($"/api/v1/support-teams/{team.Id}");
        var status = change is "user" or "session" ? HttpStatusCode.Unauthorized : change == "grant" ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound;
        Assert.Equal(status, detail.StatusCode);
        await using var scope = test.Accounts.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SupportTeamService>();
        var error = await Assert.ThrowsAsync<TeamOperationException>(() => service.AddMemberAsync(actor, team.Id,
            new(target.Id, 1), CancellationToken.None));
        Assert.Equal(change is "user" or "session" ? TeamError.Unauthenticated : change == "grant" ? TeamError.InsufficientScope : TeamError.NotFound, error.Error);
        Assert.False(await database.SupportTeamMembers.AnyAsync(x => x.SupportTeamId == team.Id && x.UserId == target.Id));
    }

    [PostgresFact]
    public async Task InvalidParentsAndInactiveUsersTeamsAndDepartmentsRejectNewMemberships()
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var team = await test.CreateTeamAsync();
        var user = await test.Accounts.NewUserAsync();
        await using var database = test.Accounts.Database();
        foreach (var departmentId in new[] { Guid.Empty, Guid.NewGuid() })
        {
            using var invalid = await test.Accounts.Admin.PostAsJsonAsync("/api/v1/support-teams", new { DepartmentId = departmentId, Name = "Invalid" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        foreach (var userId in new[] { user.Id, Guid.NewGuid() })
        {
            using var invalid = await test.Accounts.Admin.PostAsJsonAsync($"/api/v1/support-teams/{team.Id}/members", new { UserId = userId, TeamVersion = team.Version });
            await ErrorAsync(invalid, HttpStatusCode.BadRequest, "InvalidUser");
        }
        await database.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));
        using var deactivate = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/support-teams/{team.Id}/status", new { IsActive = false, team.Version });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        var current = await test.GetAsync(team.Id);
        using var inactiveTeam = await test.Accounts.Admin.PostAsJsonAsync($"/api/v1/support-teams/{team.Id}/members", new { UserId = user.Id, TeamVersion = current.Version });
        await ErrorAsync(inactiveTeam, HttpStatusCode.Conflict, "InactiveTeam");
        await database.Departments.Where(x => x.Id == team.DepartmentId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        using var activate = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/support-teams/{team.Id}/status", new { IsActive = true, current.Version });
        await ErrorAsync(activate, HttpStatusCode.BadRequest, "InvalidDepartment");
        using var createInInactive = await test.Accounts.Admin.PostAsJsonAsync("/api/v1/support-teams", new { team.DepartmentId, Name = "Inactive" });
        await ErrorAsync(createInInactive, HttpStatusCode.BadRequest, "InvalidDepartment");
    }

    [PostgresTheory]
    [InlineData("department")]
    [InlineData("user")]
    public async Task AddMemberRechecksAfterConcurrentDeactivation(string change)
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var team = await test.CreateTeamAsync();
        var target = await test.Accounts.NewUserAsync();
        await using var database = test.Accounts.Database();
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)");
        if (change == "department") await database.Departments.Where(x => x.Id == team.DepartmentId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else await database.Users.Where(x => x.Id == target.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        var add = test.Accounts.Admin.PostAsJsonAsync($"/api/v1/support-teams/{team.Id}/members", new { UserId = target.Id, TeamVersion = team.Version });
        await transaction.CommitAsync();
        using var response = await add;
        await ErrorAsync(response, HttpStatusCode.BadRequest, change == "department" ? "InvalidDepartment" : "InvalidUser");
    }

    [PostgresFact]
    public async Task ConcurrentMembershipWritesAndMetadataEditsRequireCurrentTeamVersion()
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var team = await test.CreateTeamAsync();
        var first = await test.Accounts.NewUserAsync();
        var second = await test.Accounts.NewUserAsync();
        var responses = await Task.WhenAll(new[] { first, second }.Select(user => test.Accounts.Admin.PostAsJsonAsync(
            $"/api/v1/support-teams/{team.Id}/members", new { UserId = user.Id, TeamVersion = team.Version })));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        using var staleEdit = await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/support-teams/{team.Id}", new { team.Name, Description = "Stale", team.Version });
        await ErrorAsync(staleEdit, HttpStatusCode.Conflict, "VersionConflict");
        var current = await test.GetAsync(team.Id);
        var updates = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => test.Accounts.Admin.PatchAsJsonAsync(
            $"/api/v1/support-teams/{team.Id}", new { current.Name, Description = $"Edit {i}", current.Version })));
        try
        {
            Assert.Single(updates, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(updates, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in updates) response.Dispose(); }
    }

    [PostgresFact]
    public async Task DeactivateAndRemovePreserveTicketAssignmentsMembershipRowsAndSessions()
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var team = await test.CreateTeamAsync();
        var user = await test.Accounts.NewUserAsync();
        var added = await test.AddAsync(team.Id, user.Id);
        using var client = await test.Accounts.ClientAsync(user);
        await using var database = test.Accounts.Database();
        var category = new TicketCategory { Name = test.Prefix + "Category" };
        var ticket = new Ticket { Title = "Historical ticket", Description = "History", CategoryId = category.Id,
            RequesterId = user.Id, CreatedByUserId = test.Accounts.AdminUser.Id, AssignedTeamId = team.Id, AssignedAgentId = user.Id };
        database.TicketCategories.Add(category);
        database.Tickets.Add(ticket);
        await database.SaveChangesAsync();
        try
        {
            using var remove = await test.Accounts.Admin.DeleteAsync($"/api/v1/support-teams/{team.Id}/members/{user.Id}?version={added.Member.Version}&teamVersion={added.Team.Version}");
            Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
            var current = await test.GetAsync(team.Id);
            using var deactivate = await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/support-teams/{team.Id}/status", new { IsActive = false, current.Version });
            Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
            var historical = await database.Tickets.AsNoTracking().SingleAsync(x => x.Id == ticket.Id);
            Assert.Equal(team.Id, historical.AssignedTeamId);
            Assert.Equal(user.Id, historical.AssignedAgentId);
            Assert.Equal(ticket.Version, historical.Version);
            Assert.False((await test.MemberAsync(team.Id, user.Id)).IsActive);
            using var me = await client.GetAsync("/api/v1/users/me");
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally
        {
            await database.Tickets.Where(x => x.Id == ticket.Id).ExecuteDeleteAsync();
            await database.TicketCategories.Where(x => x.Id == category.Id).ExecuteDeleteAsync();
        }
    }

    [PostgresTheory]
    [InlineData("blank")]
    [InlineData("long-name")]
    [InlineData("long-description")]
    [InlineData("overposting")]
    [InlineData("version")]
    [InlineData("status")]
    [InlineData("member-overposting")]
    [InlineData("member-version")]
    [InlineData("remove-version")]
    [InlineData("page")]
    [InlineData("page-size")]
    [InlineData("sort")]
    public async Task ValidationRejectsInvalidAndOverpostedRequests(string scenario)
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var team = await test.CreateTeamAsync();
        using var response = scenario switch
        {
            "blank" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/support-teams", new { team.DepartmentId, Name = "   " }),
            "long-name" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/support-teams", new { team.DepartmentId, Name = new string('x', 201) }),
            "long-description" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/support-teams", new { team.DepartmentId, Name = "Name", Description = new string('x', 2001) }),
            "overposting" => await test.Accounts.Admin.PostAsJsonAsync("/api/v1/support-teams", new { team.DepartmentId, Name = "Name", IsActive = false }),
            "version" => await test.Accounts.Admin.PatchAsJsonAsync($"/api/v1/support-teams/{team.Id}", new { team.Name }),
            "status" => await test.Accounts.Admin.PutAsJsonAsync($"/api/v1/support-teams/{team.Id}/status", new { team.Version }),
            "member-overposting" => await test.Accounts.Admin.PostAsJsonAsync($"/api/v1/support-teams/{team.Id}/members", new { UserId = test.Accounts.AdminUser.Id, TeamVersion = team.Version, IsLeader = true }),
            "member-version" => await test.Accounts.Admin.PostAsJsonAsync($"/api/v1/support-teams/{team.Id}/members", new { UserId = test.Accounts.AdminUser.Id }),
            "remove-version" => await test.Accounts.Admin.DeleteAsync($"/api/v1/support-teams/{team.Id}/members/{test.Accounts.AdminUser.Id}"),
            "page" => await test.Accounts.Admin.GetAsync("/api/v1/support-teams?page=0"),
            "page-size" => await test.Accounts.Admin.GetAsync($"/api/v1/support-teams/{team.Id}/members?pageSize=101"),
            _ => await test.Accounts.Admin.GetAsync("/api/v1/support-teams?sortBy=secret")
        };
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [PostgresFact]
    public async Task ExplicitGrantForExistingTeamLeaderIsIdempotentAndSeedPreservesRevocation()
    {
        await using var test = await Teams.CreateAsync(fixture.ConnectionString);
        var role = await test.Accounts.RoleIdAsync(RoleNames.TeamLeader);
        await test.Accounts.SnapshotAsync(role);
        await using var database = test.Accounts.Database();
        var permission = await database.Permissions.Where(x => x.Code == PermissionCodes.TeamManage).Select(x => x.Id).SingleAsync();
        var previous = await database.IdentityAuditLogs.Where(x => x.Action == "TeamLeadershipEnabled").Select(x => x.Id).ToArrayAsync();
        try
        {
            await database.RolePermissions.Where(x => x.RoleId == role && x.PermissionId == permission).ExecuteDeleteAsync();
            var initializer = new IdentityInitializer(database, TestAccounts.Hasher());
            await initializer.SeedAsync();
            Assert.False(await database.RolePermissions.AnyAsync(x => x.RoleId == role && x.PermissionId == permission));
            await initializer.GrantTeamLeadershipAsync();
            await initializer.GrantTeamLeadershipAsync();
            Assert.True(await database.RolePermissions.AnyAsync(x => x.RoleId == role && x.PermissionId == permission));
            Assert.Equal(previous.Length + 1, await database.IdentityAuditLogs.CountAsync(x => x.Action == "TeamLeadershipEnabled"));
        }
        finally { await database.IdentityAuditLogs.Where(x => x.Action == "TeamLeadershipEnabled" && !previous.Contains(x.Id)).ExecuteDeleteAsync(); }
    }

    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    private sealed class Teams(TestAccounts accounts) : IAsyncDisposable
    {
        private readonly List<Guid> _departments = [];
        private readonly List<Guid> _teams = [];
        public TestAccounts Accounts { get; } = accounts;
        public string Prefix { get; } = Guid.NewGuid().ToString("N") + "-";
        public static async Task<Teams> CreateAsync(string connection) => new(await TestAccounts.CreateAsync(connection));
        public async Task<Guid> DepartmentAsync()
        {
            await using var database = Accounts.Database();
            var department = new Department { Name = Prefix + Guid.NewGuid().ToString("N") };
            database.Departments.Add(department);
            await database.SaveChangesAsync();
            _departments.Add(department.Id);
            return department.Id;
        }
        public async Task<SupportTeamDetails> CreateTeamAsync(string name = "Team", Guid? department = null)
        {
            var id = department ?? (_departments.Count > 0 ? _departments[0] : await DepartmentAsync());
            using var response = await Accounts.Admin.PostAsJsonAsync("/api/v1/support-teams", new { DepartmentId = id, Name = " " + Prefix + name + " " });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var team = (await response.Content.ReadFromJsonAsync<SupportTeamDetails>())!;
            _teams.Add(team.Id);
            Assert.EndsWith($"/api/v1/support-teams/{team.Id}", response.Headers.Location?.ToString());
            return team;
        }
        public async Task<SupportTeamDetails> GetAsync(Guid id) => (await Accounts.Admin.GetFromJsonAsync<SupportTeamDetails>($"/api/v1/support-teams/{id}"))!;
        public async Task<TeamMemberDetails> MemberAsync(Guid id, Guid userId) =>
            (await Accounts.Admin.GetFromJsonAsync<PagedResult<TeamMemberDetails>>($"/api/v1/support-teams/{id}/members"))!.Items.Single(x => x.UserId == userId);
        public async Task<TeamMembershipResult> AddAsync(Guid id, Guid userId, HttpClient? client = null)
        {
            var current = await GetAsync(id);
            using var response = await (client ?? Accounts.Admin).PostAsJsonAsync($"/api/v1/support-teams/{id}/members", new { UserId = userId, TeamVersion = current.Version });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<TeamMembershipResult>())!;
        }
        public async Task<TestUser> LeaderAsync(Guid id)
        {
            var user = await Accounts.NewUserAsync();
            await Accounts.AssignAsync(user.Id, await Accounts.RoleIdAsync(RoleNames.TeamLeader));
            var added = await AddAsync(id, user.Id);
            using var response = await Accounts.Admin.PutAsJsonAsync($"/api/v1/support-teams/{id}/members/{user.Id}/leadership",
                new { IsLeader = true, added.Member.Version, TeamVersion = added.Team.Version });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return user;
        }
        public async ValueTask DisposeAsync()
        {
            await using var database = Accounts.Database();
            await database.SupportTeamMembers.Where(x => _teams.Contains(x.SupportTeamId)).ExecuteDeleteAsync();
            await database.SupportTeams.Where(x => _teams.Contains(x.Id)).ExecuteDeleteAsync();
            await database.Departments.Where(x => _departments.Contains(x.Id)).ExecuteDeleteAsync();
            await Accounts.DisposeAsync();
        }
    }
}
