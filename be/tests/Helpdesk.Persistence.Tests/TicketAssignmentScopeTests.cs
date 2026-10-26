using System.Net;
using System.Net.Http.Json;
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
public class TicketAssignmentScopeTests(PostgresFixture fixture)
{
    [PostgresTheory]
    [InlineData("missing-team")]
    [InlineData("inactive-team")]
    [InlineData("inactive-department")]
    [InlineData("outside-led-team")]
    [InlineData("missing-agent")]
    [InlineData("inactive-agent")]
    [InlineData("not-member")]
    [InlineData("inactive-membership")]
    [InlineData("agent-without-permission")]
    [InlineData("agent-without-read-permission")]
    [InlineData("empty-team")]
    [InlineData("empty-agent")]
    public async Task AssignmentDestinationMustBeWithinScopeAndHaveAnEligibleActiveAgent(string failure)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var leader = await test.UserAsync(RoleNames.TeamLeader);
        await test.MemberAsync(leader.Id, test.TeamA, leader: true);
        if (failure != "outside-led-team") await test.MemberAsync(leader.Id, test.TeamB, leader: true);
        var targetUser = failure == "agent-without-read-permission" ? await test.CustomUserAsync(PermissionCodes.TicketUpdateStatus) :
            await test.UserAsync(failure == "agent-without-permission" ? RoleNames.Requester : RoleNames.SupportAgent);
        if (failure != "not-member") await test.MemberAsync(targetUser.Id, test.TeamB, active: failure != "inactive-membership");
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA);
        await using var database = test.Accounts.Database();
        if (failure == "inactive-team") await database.SupportTeams.Where(x => x.Id == test.TeamB).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (failure == "inactive-department") await database.Departments.Where(x => x.Id == test.DepartmentB).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (failure == "inactive-agent") await database.Users.Where(x => x.Id == targetUser.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        var teamId = failure == "missing-team" ? Guid.NewGuid() : failure == "empty-team" ? Guid.Empty : test.TeamB;
        var agentId = failure == "missing-agent" ? Guid.NewGuid() : failure == "empty-agent" ? Guid.Empty : targetUser.Id;
        using var client = await test.Accounts.ClientAsync(leader);
        await using var scope = test.Accounts.Factory.Services.CreateAsyncScope();
        var checker = scope.ServiceProvider.GetRequiredService<ITicketAccessChecker>();
        Assert.True(await checker.CanAsync(Actor(client), ticket.Id, TicketOperation.Assign, CancellationToken.None));
        Assert.False(await checker.CanAssignToAsync(Actor(client), ticket.Id, new(teamId, agentId), CancellationToken.None));
        using var response = await AssignAsync(client, ticket, teamId, agentId);
        await ErrorAsync(response, HttpStatusCode.BadRequest, "InvalidAssignment");
        using var production = await client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/assign", new { TeamId = teamId, AgentId = agentId, ticket.Version });
        await ErrorAsync(production, HttpStatusCode.BadRequest, failure is "empty-team" or "empty-agent" ? "InvalidInput" : "InvalidAssignment");
        var stored = await test.StoredAsync(ticket.Id);
        Assert.Equal(ticket.AssignedTeamId, stored.AssignedTeamId);
        Assert.Equal(ticket.Version, stored.Version);
        Assert.Equal(0, await database.TicketActivities.CountAsync(x => x.TicketId == ticket.Id));
    }

    [PostgresFact]
    public async Task LeaderMayTransferBetweenLedTeamsButCannotClaimAnOutsideTicketByChangingDestination()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var leader = await test.UserAsync(RoleNames.TeamLeader);
        await test.MemberAsync(leader.Id, test.TeamA, leader: true);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(agent.Id, test.TeamA);
        using var client = await test.Accounts.ClientAsync(leader);
        var outside = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamB);
        using var claim = await AssignAsync(client, outside, test.TeamA, agent.Id);
        await ErrorAsync(claim, HttpStatusCode.NotFound, "NotFound");
        Assert.Equal(outside.Version, (await test.StoredAsync(outside.Id)).Version);
        await test.MemberAsync(leader.Id, test.TeamB, leader: true);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamB);
        using var transfer = await AssignAsync(client, ticket, test.TeamA, agent.Id);
        Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);
        var stored = await test.StoredAsync(ticket.Id);
        Assert.Equal(test.TeamA, stored.AssignedTeamId);
        Assert.Equal(agent.Id, stored.AssignedAgentId);
    }

    [PostgresFact]
    public async Task GlobalOperatorMayAssignUnassignedOrHistoricallyInactiveSourceToActiveDestination()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var actor = await test.CustomUserAsync(PermissionCodes.TicketReadAll, PermissionCodes.TicketAssign);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(agent.Id, test.TeamB);
        await using var database = test.Accounts.Database();
        await database.Departments.Where(x => x.Id == test.DepartmentA).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        using var client = await test.Accounts.ClientAsync(actor);
        foreach (var source in new Guid?[] { null, test.TeamA })
        {
            var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, source);
            using var response = await AssignAsync(client, ticket, test.TeamB, agent.Id);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    [PostgresFact]
    public async Task DemotionImmediatelyRemovesLeaderAssignmentAndProcessingScopeButKeepsTeamRead()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var leader = await test.UserAsync(RoleNames.TeamLeader);
        await test.MemberAsync(leader.Id, test.TeamA, leader: true);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA);
        using var client = await test.Accounts.ClientAsync(leader);
        Assert.Equal(new TicketAccess(true, true, true), await client.GetFromJsonAsync<TicketAccess>($"/api/v1/tickets/{ticket.Id}/access"));
        await using var database = test.Accounts.Database();
        await database.SupportTeamMembers.Where(x => x.UserId == leader.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsLeader, false));
        Assert.Equal(new TicketAccess(true, false, false), await client.GetFromJsonAsync<TicketAccess>($"/api/v1/tickets/{ticket.Id}/access"));
        using var response = await AssignAsync(client, ticket, test.TeamA);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [PostgresFact]
    public async Task GuardRefreshesPreviouslyReadTicketAfterLockingAndDoesNotTrustOldAssignment()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var user = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(user.Id, test.TeamA);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA, user.Id);
        using var client = await test.Accounts.ClientAsync(user);
        await using var scope = test.Accounts.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<be.Infrastructure.Persistence.HelpdeskDbContext>();
        var tracked = await database.Tickets.SingleAsync(x => x.Id == ticket.Id);
        await using (var other = test.Accounts.Database())
            await other.Tickets.Where(x => x.Id == ticket.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AssignedAgentId, test.Accounts.AdminUser.Id));
        await using var transaction = await database.Database.BeginTransactionAsync();
        var guard = scope.ServiceProvider.GetRequiredService<TicketWriteGuard>();
        var error = await Assert.ThrowsAsync<TicketOperationException>(() => guard.LockAsync(Actor(client), ticket.Id,
            TicketOperation.UpdateStatus, ticket.Version, CancellationToken.None));
        Assert.Equal(TicketError.Forbidden, error.Error);
        Assert.Equal(test.Accounts.AdminUser.Id, tracked.AssignedAgentId);
    }

    [PostgresFact]
    public async Task GuardWaitsForConcurrentMembershipRevocationAndReadsCommittedState()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var user = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(user.Id, test.TeamA);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA, user.Id);
        using var client = await test.Accounts.ClientAsync(user);
        await using var database = test.Accounts.Database();
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)");
        await database.SupportTeamMembers.Where(x => x.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        var write = ProcessAsync(client, ticket);
        await transaction.CommitAsync();
        using var response = await write;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(TicketStatus.New, (await test.StoredAsync(ticket.Id)).Status);
    }
}
