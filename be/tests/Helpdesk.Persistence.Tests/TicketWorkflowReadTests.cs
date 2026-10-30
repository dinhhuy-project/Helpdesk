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
public class TicketWorkflowReadTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task AssignmentLookupsUseTicketSourceScopeBeforeDestinationAndNeedNoAdministrationGrant()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var leader = await test.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketAssign);
        await test.MemberAsync(leader.Id, test.TeamA, leader: true);
        using var client = await test.Accounts.ClientAsync(leader);
        var own = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA);
        var outside = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamB);
        var root = $"/api/v1/tickets/{own.Id}";
        var teams = (await client.GetFromJsonAsync<PagedResult<TicketAssignmentTeam>>(root + "/assignment-teams?pageSize=1"))!;
        Assert.Equal(1, teams.TotalCount);
        Assert.Equal(test.TeamA, Assert.Single(teams.Items).Id);
        var empty = (await client.GetFromJsonAsync<PagedResult<TicketAssignmentTeam>>(root + "/assignment-teams?page=2&pageSize=1"))!;
        Assert.Equal(1, empty.TotalCount); Assert.Empty(empty.Items);
        using var deniedTeam = await client.GetAsync(root + $"/assignment-teams/{test.TeamB}/agents");
        await ErrorAsync(deniedTeam, HttpStatusCode.NotFound, "NotFound");
        foreach (var suffix in new[] { "workflow", "assignment-teams", $"assignment-teams/{test.TeamA}/agents" })
        {
            using var deniedSource = await client.GetAsync($"/api/v1/tickets/{outside.Id}/{suffix}");
            await ErrorAsync(deniedSource, HttpStatusCode.NotFound, "NotFound");
        }
        using var adminApi = await client.GetAsync("/api/v1/support-teams");
        Assert.Equal(HttpStatusCode.Forbidden, adminApi.StatusCode);
        var filtered = (await client.GetFromJsonAsync<PagedResult<TicketAssignmentTeam>>(root + "/assignment-teams?search=not-present"))!;
        Assert.Empty(filtered.Items); Assert.Equal(0, filtered.TotalCount);
        await using var database = test.Accounts.Database();
        await database.SupportTeamMembers.Where(x => x.UserId == leader.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsLeader, false));
        using var demoted = await client.GetAsync(root + "/assignment-teams");
        await ErrorAsync(demoted, HttpStatusCode.Forbidden, "Forbidden");
    }

    [PostgresTheory]
    [InlineData("eligible")]
    [InlineData("no-processing")]
    [InlineData("no-read")]
    [InlineData("inactive-user")]
    [InlineData("inactive-member")]
    [InlineData("non-member")]
    [InlineData("inactive-team")]
    [InlineData("inactive-department")]
    public async Task AgentLookupSharesMutationEligibilityAndExposesOnlyMinimalIdentity(string scenario)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var actor = await test.CustomUserAsync(PermissionCodes.TicketReadAll, PermissionCodes.TicketAssign);
        var grants = scenario == "no-processing" ? new[] { PermissionCodes.TicketReadTeam } : scenario == "no-read" ?
            [PermissionCodes.TicketUpdateStatus] : new[] { PermissionCodes.TicketReadTeam, PermissionCodes.TicketUpdateStatus };
        var agent = await test.CustomUserAsync(grants);
        if (scenario != "non-member") await test.MemberAsync(agent.Id, test.TeamB, active: scenario != "inactive-member");
        await using var database = test.Accounts.Database();
        if (scenario == "inactive-user") await database.Users.Where(x => x.Id == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (scenario == "inactive-team") await database.SupportTeams.Where(x => x.Id == test.TeamB).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (scenario == "inactive-department") await database.Departments.Where(x => x.Id == test.DepartmentB).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id);
        using var client = await test.Accounts.ClientAsync(actor);
        using var response = await client.GetAsync($"/api/v1/tickets/{ticket.Id}/assignment-teams/{test.TeamB}/agents");
        if (scenario is "inactive-team" or "inactive-department")
        {
            await ErrorAsync(response, HttpStatusCode.NotFound, "NotFound"); return;
        }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PagedResult<TicketAssignmentAgent>>())!;
        Assert.Equal(scenario == "eligible" ? 1 : 0, result.TotalCount);
        if (scenario == "eligible") Assert.Equal(agent.Id, Assert.Single(result.Items).Id); else Assert.Empty(result.Items);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("email", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        using var write = await client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/assign", new { TeamId = test.TeamB, AgentId = agent.Id, ticket.Version });
        Assert.Equal(scenario == "eligible" ? HttpStatusCode.OK : HttpStatusCode.BadRequest, write.StatusCode);
    }

    [PostgresTheory]
    [InlineData(TicketStatus.New, TicketStatus.Cancelled)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Cancelled)]
    [InlineData(TicketStatus.WaitingForRequester, TicketStatus.Cancelled)]
    [InlineData(TicketStatus.Reopened, TicketStatus.Cancelled)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed, TicketStatus.Reopened)]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public async Task RequesterCapabilitiesRespectWorkflowWithoutGrantingProcessingOrAssignment(TicketStatus status, params TicketStatus[] expected)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.CustomUserAsync(PermissionCodes.TicketReadOwn, PermissionCodes.TicketConfirm, PermissionCodes.TicketReopen, PermissionCodes.TicketCancel);
        var ticket = await test.TicketAsync(owner.Id, status: status);
        using var client = await test.Accounts.ClientAsync(owner);
        var context = (await client.GetFromJsonAsync<TicketWorkflowContext>($"/api/v1/tickets/{ticket.Id}/workflow"))!;
        Assert.Equal(ticket.Version, context.Version);
        Assert.Equal(status, context.Status);
        Assert.False(context.CanAssign); Assert.False(context.AssignmentIsValid);
        Assert.Equal(expected.Order(), context.Transitions.Order());
        using var denied = await client.GetAsync($"/api/v1/tickets/{ticket.Id}/assignment-teams");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [PostgresFact]
    public async Task ProcessingCapabilitiesRecheckGrantsMembershipAssignmentAndHistoricalRequesterActions()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.CustomUserAsync(PermissionCodes.TicketReadOwn, PermissionCodes.TicketConfirm, PermissionCodes.TicketReopen);
        var agent = await test.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketUpdateStatus,
            PermissionCodes.TicketConfirm, PermissionCodes.TicketReopen, PermissionCodes.TicketCancel);
        await test.MemberAsync(agent.Id, test.TeamA);
        using var client = await test.Accounts.ClientAsync(agent);
        var ticket = await test.TicketAsync(owner.Id, test.TeamA, agent.Id, status: TicketStatus.InProgress);
        var url = $"/api/v1/tickets/{ticket.Id}/workflow";
        var context = (await client.GetFromJsonAsync<TicketWorkflowContext>(url))!;
        Assert.True(context.AssignmentIsValid);
        Assert.Equal(new[] { TicketStatus.WaitingForRequester, TicketStatus.Resolved }, context.Transitions);
        await using var database = test.Accounts.Database();
        // Same ticket version, but a grant changed: processing eligibility disappears immediately.
        var updateId = await database.Permissions.Where(x => x.Code == PermissionCodes.TicketUpdateStatus).Select(x => x.Id).SingleAsync();
        var roles = database.UserRoles.Where(x => x.UserId == agent.Id).Select(x => x.RoleId);
        await database.RolePermissions.Where(x => roles.Contains(x.RoleId) && x.PermissionId == updateId).ExecuteDeleteAsync();
        context = (await client.GetFromJsonAsync<TicketWorkflowContext>(url))!;
        Assert.Equal(ticket.Version, context.Version); Assert.Empty(context.Transitions); Assert.False(context.AssignmentIsValid);
        await database.Tickets.Where(x => x.Id == ticket.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, TicketStatus.Resolved)
            .SetProperty(x => x.ResolutionSummary, "Resolved").SetProperty(x => x.ResolvedAt, DateTime.UtcNow));
        context = (await client.GetFromJsonAsync<TicketWorkflowContext>(url))!;
        Assert.Empty(context.Transitions); // Dedicated permissions do not make an agent the requester.
        await database.Departments.Where(x => x.Id == test.DepartmentA).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        using var requester = await test.Accounts.ClientAsync(owner);
        var historical = (await requester.GetFromJsonAsync<TicketWorkflowContext>(url))!;
        Assert.Equal(new[] { TicketStatus.Closed, TicketStatus.Reopened }, historical.Transitions);
        Assert.Equal("Team A", historical.AssignedTeamName);
        Assert.NotNull(historical.AssignedAgentName);
        using var outOfScope = await client.GetAsync(url);
        await ErrorAsync(outOfScope, HttpStatusCode.NotFound, "NotFound");
    }
}
