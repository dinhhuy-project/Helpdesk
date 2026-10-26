using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
public class TicketMutationTests(PostgresFixture fixture)
{
    private static Task<HttpResponseMessage> Transition(HttpClient client, Guid id, uint version, TicketStatus status, string? summary = null) =>
        client.PostAsJsonAsync($"/api/v1/tickets/{id}/transition", new { Status = status, ResolutionSummary = summary, Version = version });
    private static Task<HttpResponseMessage> Assign(HttpClient client, Guid id, uint version, Guid teamId, Guid? agentId = null) =>
        client.PostAsJsonAsync($"/api/v1/tickets/{id}/assign", new { TeamId = teamId, AgentId = agentId, Version = version });
    private static async Task<TicketMutationResult> Success(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<TicketMutationResult>())!;
        }
    }

    [PostgresFact]
    public async Task AgentProcessesAssignedTicketAndRequesterConfirmsWithAtomicPublicResolutionHistory()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.UserAsync(RoleNames.Requester);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(agent.Id, test.TeamA);
        using var requester = await test.Accounts.ClientAsync(owner);
        using var processor = await test.Accounts.ClientAsync(agent);
        var ticket = await test.TicketAsync(owner.Id);
        var assigned = await Success(await Assign(test.Accounts.Admin, ticket.Id, ticket.Version, test.TeamA, agent.Id));
        Assert.Equal(TicketStatus.New, assigned.Status);
        Assert.NotEqual(ticket.Version, assigned.Version);
        using var stale = await Transition(processor, ticket.Id, ticket.Version, TicketStatus.InProgress);
        await ErrorAsync(stale, HttpStatusCode.Conflict, "VersionConflict");
        using var wrongActor = await Transition(requester, ticket.Id, assigned.Version, TicketStatus.InProgress);
        await ErrorAsync(wrongActor, HttpStatusCode.Forbidden, "Forbidden");
        var started = await Success(await Transition(processor, ticket.Id, assigned.Version, TicketStatus.InProgress));
        var waiting = await Success(await Transition(processor, ticket.Id, started.Version, TicketStatus.WaitingForRequester));
        using var skipResume = await Transition(processor, ticket.Id, waiting.Version, TicketStatus.Resolved, "Result");
        await ErrorAsync(skipResume, HttpStatusCode.Conflict, "InvalidTransition");
        var resumed = await Success(await Transition(processor, ticket.Id, waiting.Version, TicketStatus.InProgress));
        using var blank = await Transition(processor, ticket.Id, resumed.Version, TicketStatus.Resolved, "   ");
        await ErrorAsync(blank, HttpStatusCode.BadRequest, "ResolutionRequired");
        var resolved = await Success(await Transition(processor, ticket.Id, resumed.Version, TicketStatus.Resolved, "  Public result  "));
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Null(resolved.ClosedAt);
        using var agentConfirm = await Transition(processor, ticket.Id, resolved.Version, TicketStatus.Closed);
        await ErrorAsync(agentConfirm, HttpStatusCode.Forbidden, "Forbidden");
        var closed = await Success(await Transition(requester, ticket.Id, resolved.Version, TicketStatus.Closed));
        Assert.NotNull(closed.ClosedAt);
        Assert.Equal(resolved.ResolvedAt, closed.ResolvedAt);
        var stored = await test.StoredAsync(ticket.Id);
        Assert.Equal("Public result", stored.ResolutionSummary);
        Assert.Equal(ticket.Title, stored.Title);
        Assert.Equal(ticket.RequesterId, stored.RequesterId);
        Assert.Equal(closed.Version, stored.Version);
        await using var database = test.Accounts.Database();
        var history = await database.TicketActivities.Where(x => x.TicketId == ticket.Id).ToListAsync();
        Assert.Equal(6, history.Count);
        Assert.Single(history, x => x.Action == "TicketAssigned");
        var confirmation = Assert.Single(history, x => x.ActorId == owner.Id);
        Assert.Equal("Resolved", JsonDocument.Parse(confirmation.OldValue!).RootElement.GetProperty("Status").GetString());
        Assert.Equal("Closed", JsonDocument.Parse(confirmation.NewValue!).RootElement.GetProperty("Status").GetString());
        Assert.All(history, x => Assert.DoesNotContain(ticket.Description, x.NewValue));
        Assert.False(await database.TicketSlas.AnyAsync(x => x.TicketId == ticket.Id));
        using var terminal = await Assign(test.Accounts.Admin, ticket.Id, closed.Version, test.TeamA);
        await ErrorAsync(terminal, HttpStatusCode.Forbidden, "Forbidden");
        using var reopenClosed = await Transition(requester, ticket.Id, closed.Version, TicketStatus.Reopened);
        Assert.Equal(HttpStatusCode.Forbidden, reopenClosed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await requester.GetAsync($"/api/v1/tickets/{ticket.Id}")).StatusCode);
    }

    [PostgresFact]
    public async Task ReopenKeepsPastResolutionInActivityAndNeverRecalculatesExistingSlaDeadlines()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var owner = await test.UserAsync(RoleNames.Requester);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(agent.Id, test.TeamA);
        using var requester = await test.Accounts.ClientAsync(owner);
        using var processor = await test.Accounts.ClientAsync(agent);
        var ticket = await test.TicketAsync(owner.Id, test.TeamA, agent.Id, status: TicketStatus.Resolved);
        var snapshot = await AddSla(test, ticket);
        var reopened = await Success(await Transition(requester, ticket.Id, ticket.Version, TicketStatus.Reopened));
        Assert.Null(reopened.ResolvedAt);
        Assert.Null((await test.StoredAsync(ticket.Id)).ResolutionSummary);
        await using var database = test.Accounts.Database();
        var sla = await database.TicketSlas.AsNoTracking().SingleAsync(x => x.TicketId == ticket.Id);
        Assert.Null(sla.ResolvedAt);
        Assert.Equal(snapshot.FirstResponseDueAt, sla.FirstResponseDueAt);
        Assert.Equal(snapshot.ResolutionDueAt, sla.ResolutionDueAt);
        Assert.Equal(snapshot.FirstRespondedAt, sla.FirstRespondedAt);
        var activity = await database.TicketActivities.SingleAsync(x => x.TicketId == ticket.Id);
        Assert.Equal("Resolved", JsonDocument.Parse(activity.OldValue!).RootElement.GetProperty("Status").GetString());
        Assert.Equal("Resolved", JsonDocument.Parse(activity.OldValue!).RootElement.GetProperty("ResolutionSummary").GetString());
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(activity.NewValue!).RootElement.GetProperty("ResolutionSummary").ValueKind);
        var resumed = await Success(await Transition(processor, ticket.Id, reopened.Version, TicketStatus.InProgress));
        var resolved = await Success(await Transition(processor, ticket.Id, resumed.Version, TicketStatus.Resolved, "Second result"));
        sla = await database.TicketSlas.AsNoTracking().SingleAsync(x => x.TicketId == ticket.Id);
        Assert.Equal(resolved.ResolvedAt, sla.ResolvedAt);
        Assert.Equal(snapshot.ResolutionDueAt, sla.ResolutionDueAt);
    }

    [PostgresTheory]
    [InlineData(TicketStatus.Closed, TicketStatus.Resolved, PermissionCodes.TicketConfirm)]
    [InlineData(TicketStatus.Reopened, TicketStatus.Resolved, PermissionCodes.TicketReopen)]
    [InlineData(TicketStatus.Cancelled, TicketStatus.New, PermissionCodes.TicketCancel)]
    public async Task RequesterActionsNeedTheirOwnPermissionAndResourceScope(TicketStatus target, TicketStatus source, string permission)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var ownerWithoutPermission = await test.CustomUserAsync(PermissionCodes.TicketReadOwn);
        var ownerWithPermission = await test.CustomUserAsync(PermissionCodes.TicketReadOwn, permission);
        var globalWithoutPermission = await test.CustomUserAsync(PermissionCodes.TicketReadAll, PermissionCodes.TicketUpdateStatus);
        var globalWithPermission = await test.CustomUserAsync(PermissionCodes.TicketReadAll, permission);
        var teamMember = await test.CustomUserAsync(PermissionCodes.TicketReadTeam, permission);
        await test.MemberAsync(teamMember.Id, test.TeamA, leader: true);
        var ticket = await test.TicketAsync(ownerWithoutPermission.Id, test.TeamA, status: source);
        using var ownerDenied = await test.Accounts.ClientAsync(ownerWithoutPermission);
        using var noGrant = await Transition(ownerDenied, ticket.Id, ticket.Version, target);
        await ErrorAsync(noGrant, HttpStatusCode.Forbidden, "Forbidden");
        using var globalDenied = await test.Accounts.ClientAsync(globalWithoutPermission);
        using var globalNoGrant = await Transition(globalDenied, ticket.Id, ticket.Version, target);
        await ErrorAsync(globalNoGrant, HttpStatusCode.Forbidden, "Forbidden");
        using var leader = await test.Accounts.ClientAsync(teamMember);
        using var wrongOwner = await Transition(leader, ticket.Id, ticket.Version, target);
        await ErrorAsync(wrongOwner, HttpStatusCode.Forbidden, "Forbidden");
        using var ownerClient = await test.Accounts.ClientAsync(ownerWithPermission);
        using var outside = await Transition(ownerClient, ticket.Id, ticket.Version, target);
        await ErrorAsync(outside, HttpStatusCode.NotFound, "NotFound");
        var own = await test.TicketAsync(ownerWithPermission.Id, status: source);
        await Success(await Transition(ownerClient, own.Id, own.Version, target));
        using var global = await test.Accounts.ClientAsync(globalWithPermission);
        await Success(await Transition(global, ticket.Id, ticket.Version, target));
    }

    [PostgresFact]
    public async Task AssignmentValidatesSourceAndDestinationAndIdenticalCommandDoesNotWriteAgain()
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var leader = await test.UserAsync(RoleNames.TeamLeader);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(leader.Id, test.TeamA, leader: true);
        await test.MemberAsync(agent.Id, test.TeamA);
        using var client = await test.Accounts.ClientAsync(leader);
        var outside = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamB);
        using var claim = await Assign(client, outside.Id, outside.Version, test.TeamA, agent.Id);
        await ErrorAsync(claim, HttpStatusCode.NotFound, "NotFound");
        var source = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA);
        using var outsideTarget = await Assign(client, source.Id, source.Version, test.TeamB);
        await ErrorAsync(outsideTarget, HttpStatusCode.BadRequest, "InvalidAssignment");
        var assigned = await Success(await Assign(client, source.Id, source.Version, test.TeamA, agent.Id));
        var repeated = await Success(await Assign(client, source.Id, assigned.Version, test.TeamA, agent.Id));
        Assert.Equal(assigned, repeated);
        await test.MemberAsync(leader.Id, test.TeamB, leader: true);
        var cleared = await Success(await Assign(client, source.Id, repeated.Version, test.TeamB));
        Assert.Equal(test.TeamB, cleared.AssignedTeamId);
        Assert.Null(cleared.AssignedAgentId);
        Assert.Equal(TicketStatus.New, cleared.Status);
        using var stale = await Assign(client, source.Id, repeated.Version, test.TeamB);
        await ErrorAsync(stale, HttpStatusCode.Conflict, "VersionConflict");
        await using var database = test.Accounts.Database();
        Assert.Equal(2, await database.TicketActivities.CountAsync(x => x.TicketId == source.Id));
    }

    [PostgresTheory]
    [InlineData("no-team")]
    [InlineData("no-agent")]
    [InlineData("inactive-agent")]
    [InlineData("inactive-team")]
    [InlineData("inactive-department")]
    [InlineData("inactive-membership")]
    [InlineData("missing-processor-grant")]
    public async Task ProcessingRechecksCurrentAssignmentEvenForGlobalOperator(string failure)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var agent = await test.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketUpdateStatus);
        await test.MemberAsync(agent.Id, test.TeamA);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id,
            failure == "no-team" ? null : test.TeamA, failure is "no-team" or "no-agent" ? null : agent.Id);
        await using var database = test.Accounts.Database();
        if (failure == "inactive-agent") await database.Users.Where(x => x.Id == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (failure == "inactive-team") await database.SupportTeams.Where(x => x.Id == test.TeamA).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (failure == "inactive-department") await database.Departments.Where(x => x.Id == test.DepartmentA).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (failure == "inactive-membership") await database.SupportTeamMembers.Where(x => x.UserId == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        if (failure == "missing-processor-grant")
        {
            var roleIds = database.UserRoles.Where(x => x.UserId == agent.Id).Select(x => x.RoleId);
            var permissionId = await database.Permissions.Where(x => x.Code == PermissionCodes.TicketUpdateStatus).Select(x => x.Id).SingleAsync();
            await database.RolePermissions.Where(x => roleIds.Contains(x.RoleId) && x.PermissionId == permissionId).ExecuteDeleteAsync();
        }
        using var response = await Transition(test.Accounts.Admin, ticket.Id, ticket.Version, TicketStatus.InProgress);
        await ErrorAsync(response, HttpStatusCode.BadRequest, "InvalidAssignment");
        Assert.Equal(ticket.Version, (await test.StoredAsync(ticket.Id)).Version);
        Assert.False(await database.TicketActivities.AnyAsync(x => x.TicketId == ticket.Id));
    }

    [PostgresTheory]
    [InlineData("assign-assign")]
    [InlineData("transition-transition")]
    [InlineData("assign-transition")]
    public async Task ConcurrentCommandsWithSameVersionCommitExactlyOneChangeAndOneActivity(string commands)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(agent.Id, test.TeamA);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA, agent.Id);
        Task<HttpResponseMessage> AssignCommand() => Assign(test.Accounts.Admin, ticket.Id, ticket.Version, test.TeamB);
        Task<HttpResponseMessage> TransitionCommand() => Transition(test.Accounts.Admin, ticket.Id, ticket.Version, TicketStatus.InProgress);
        // Authorization and version are checked before validating the new processing state.
        var responses = await Task.WhenAll(commands.StartsWith("assign", StringComparison.Ordinal) ? AssignCommand() : TransitionCommand(),
            commands.EndsWith("assign", StringComparison.Ordinal) ? AssignCommand() : TransitionCommand());
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            await ErrorAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "VersionConflict");
            await using var database = test.Accounts.Database();
            Assert.Single(await database.TicketActivities.Where(x => x.TicketId == ticket.Id).ToListAsync());
            Assert.NotEqual(ticket.Version, (await test.StoredAsync(ticket.Id)).Version);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [PostgresTheory]
    [InlineData("permission")]
    [InlineData("session")]
    [InlineData("inactive-user")]
    [InlineData("membership")]
    public async Task ProductionTransitionWaitsForRevocationAndRechecksTheSameJwt(string change)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var agent = await test.CustomUserAsync(PermissionCodes.TicketReadTeam, PermissionCodes.TicketUpdateStatus);
        await test.MemberAsync(agent.Id, test.TeamA);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA, agent.Id);
        using var client = await test.Accounts.ClientAsync(agent);
        var actor = Actor(client);
        await using var database = test.Accounts.Database();
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)");
        if (change == "session") await database.RefreshTokens.Where(x => x.Id == actor.SessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        else if (change == "inactive-user") await database.Users.Where(x => x.Id == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else if (change == "membership") await database.SupportTeamMembers.Where(x => x.UserId == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        else
        {
            var roleIds = database.UserRoles.Where(x => x.UserId == agent.Id).Select(x => x.RoleId);
            var permissionId = await database.Permissions.Where(x => x.Code == PermissionCodes.TicketUpdateStatus).Select(x => x.Id).SingleAsync();
            await database.RolePermissions.Where(x => roleIds.Contains(x.RoleId) && x.PermissionId == permissionId).ExecuteDeleteAsync();
        }
        var pending = Transition(client, ticket.Id, ticket.Version, TicketStatus.InProgress);
        await transaction.CommitAsync();
        using var response = await pending;
        Assert.Equal(change is "session" or "inactive-user" ? HttpStatusCode.Unauthorized :
            change == "membership" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ticket.Version, (await test.StoredAsync(ticket.Id)).Version);
        Assert.False(await database.TicketActivities.AnyAsync(x => x.TicketId == ticket.Id));
    }

    [PostgresTheory]
    [InlineData("assign", "missing-version")]
    [InlineData("transition", "missing-version")]
    [InlineData("assign", "empty-team")]
    [InlineData("assign", "requesterId")]
    [InlineData("assign", "status")]
    [InlineData("transition", "invalid-status")]
    [InlineData("transition", "missing-status")]
    [InlineData("transition", "long-summary")]
    [InlineData("transition", "irrelevant-summary")]
    [InlineData("transition", "assignedAgentId")]
    [InlineData("transition", "resolvedAt")]
    [InlineData("transition", "actorId")]
    public async Task InvalidAndServerOwnedFieldsCannotMutateTicket(string route, string failure)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id);
        var body = new Dictionary<string, object?> { ["version"] = ticket.Version };
        if (route == "assign") body["teamId"] = test.TeamA;
        else body["status"] = TicketStatus.InProgress;
        switch (failure)
        {
            case "missing-version": body.Remove("version"); break;
            case "empty-team": body["teamId"] = Guid.Empty; break;
            case "invalid-status": body["status"] = 999; break;
            case "missing-status": body.Remove("status"); break;
            case "long-summary": body["resolutionSummary"] = new string('x', 4001); break;
            case "irrelevant-summary": body["resolutionSummary"] = "private-summary-marker"; break;
            default: body[failure] = "forged-value-marker"; break;
        }
        using var response = await test.Accounts.Admin.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/{route}", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-summary-marker", error);
        Assert.DoesNotContain("forged-value-marker", error);
        Assert.Equal(ticket.Version, (await test.StoredAsync(ticket.Id)).Version);
    }

    [PostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailureAfterSaveBeforeCommitRollsBackTicketActivityAndSla(bool assign)
    {
        await using var test = await CreateAsync(fixture.ConnectionString);
        var agent = await test.UserAsync(RoleNames.SupportAgent);
        await test.MemberAsync(agent.Id, test.TeamA);
        var ticket = await test.TicketAsync(test.Accounts.AdminUser.Id, test.TeamA, agent.Id, status: TicketStatus.InProgress);
        var snapshot = await AddSla(test, ticket);
        var failure = new FailAfterMutationSave();
        await using var factory = test.Accounts.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Scoped<HelpdeskDbContext>(_ => new(new DbContextOptionsBuilder<HelpdeskDbContext>()
                .UseNpgsql(fixture.ConnectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk"))
                .AddInterceptors(failure).Options)))));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = test.Accounts.Admin.DefaultRequestHeaders.Authorization;
        using var response = assign ? await Assign(client, ticket.Id, ticket.Version, test.TeamB) :
            await Transition(client, ticket.Id, ticket.Version, TicketStatus.Resolved, "private-summary-marker");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(failure.SawSavedMutation);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-marker", body);
        Assert.DoesNotContain("private-summary-marker", body);
        var stored = await test.StoredAsync(ticket.Id);
        Assert.Equal(ticket.Version, stored.Version);
        Assert.Equal(ticket.Status, stored.Status);
        Assert.Equal(ticket.AssignedTeamId, stored.AssignedTeamId);
        Assert.Equal(ticket.AssignedAgentId, stored.AssignedAgentId);
        Assert.Null(stored.ResolutionSummary);
        await using var database = test.Accounts.Database();
        Assert.False(await database.TicketActivities.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.Equal(snapshot.ResolvedAt, (await database.TicketSlas.SingleAsync(x => x.TicketId == ticket.Id)).ResolvedAt);
    }

    private sealed class FailAfterMutationSave : SaveChangesInterceptor
    {
        public bool SawSavedMutation { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            SawSavedMutation = eventData.Context!.ChangeTracker.Entries<TicketActivity>().Any(x =>
                x.State == EntityState.Unchanged && x.Entity.Action is "TicketAssigned" or "TicketStatusChanged");
            if (SawSavedMutation) throw new InvalidOperationException("secret-marker: failure after SQL save before commit");
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<TicketSla> AddSla(TicketAccessTestContext test, Ticket ticket)
    {
        await using var database = test.Accounts.Database();
        var policy = new SlaPolicy { Name = test.Prefix + "SLA", FirstResponseMinutes = 60, ResolutionMinutes = 240 };
        database.SlaPolicies.Add(policy);
        test.OwnSlaPolicy(policy.Id);
        var applied = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var snapshot = new TicketSla { TicketId = ticket.Id, SlaPolicyId = policy.Id, AppliedAt = applied,
            FirstResponseDueAt = applied.AddHours(1), ResolutionDueAt = applied.AddHours(4), FirstRespondedAt = applied.AddMinutes(30), ResolvedAt = ticket.ResolvedAt };
        database.TicketSlas.Add(snapshot);
        await database.SaveChangesAsync();
        return snapshot;
    }
}
