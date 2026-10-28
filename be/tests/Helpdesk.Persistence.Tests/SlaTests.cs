using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Sla;
using be.Application.Features.Tickets;
using be.Configuration;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Persistence;
using be.Infrastructure.Sla;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using static Helpdesk.Persistence.Tests.NotificationTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class SlaTests(PostgresFixture fixture)
{
    private const string Policies = "/api/v1/admin/sla-policies";
    private static async Task<SlaPolicyDetails> CreatePolicy(NotificationTestContext test, HttpClient client, TicketPriority priority = TicketPriority.Normal)
    {
        using var response = await client.PostAsJsonAsync(Policies, new { name = "Policy", priority, firstResponseMinutes = 60, resolutionMinutes = 240 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var policy = (await response.Content.ReadFromJsonAsync<SlaPolicyDetails>())!;
        test.Data.OwnSlaPolicy(policy.Id);
        return policy;
    }
    private static async Task<TicketCreated> CreateTicket(NotificationTestContext test, HttpClient client, TicketPriority priority = TicketPriority.Normal)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/tickets", new { title = "SLA Ticket", description = "Description", categoryId = test.Data.Category, priority });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = (await response.Content.ReadFromJsonAsync<TicketCreated>())!;
        test.Data.OwnTicket(ticket.Id);
        return ticket;
    }
    private static async Task<int> Process(NotificationTestContext test)
    {
        await using var scope = test.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SlaOverdueProcessor>().RunOnceAsync(CancellationToken.None);
    }
    private static async Task<TicketSla> AddSnapshot(NotificationTestContext test, Guid ticketId, DateTime? appliedAt = null, int response = 1, int resolution = 2)
    {
        await using var database = test.Data.Accounts.Database();
        var policy = new SlaPolicy { Name = "Manual", FirstResponseMinutes = response, ResolutionMinutes = resolution, IsActive = false };
        var applied = appliedAt ?? DateTime.UtcNow.AddHours(-1);
        var sla = new TicketSla { TicketId = ticketId, SlaPolicyId = policy.Id, AppliedAt = applied,
            FirstResponseDueAt = applied.AddMinutes(response), ResolutionDueAt = applied.AddMinutes(resolution) };
        database.AddRange(policy, sla);
        await database.SaveChangesAsync();
        test.Data.OwnSlaPolicy(policy.Id);
        return sla;
    }

    [PostgresFact]
    public async Task PoliciesRequireCurrentPermissionValidateAndProtectConcurrentActivePriority()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        using var requester = await test.ClientAsync(owner);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var forbidden = await requester.GetAsync(Policies);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var missing = await admin.GetAsync(Policies + "/" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        foreach (var payload in new object[] {
            new { name = " ", priority = 1, firstResponseMinutes = 1, resolutionMinutes = 2 },
            new { name = "Name", priority = 1, firstResponseMinutes = 3, resolutionMinutes = 2 },
            new { name = "Name", priority = 1, firstResponseMinutes = 1, resolutionMinutes = 525601 },
            new { name = "Name", priority = 999, firstResponseMinutes = 1, resolutionMinutes = 2 },
            new { name = "Name", priority = 1, firstResponseMinutes = 1, resolutionMinutes = 2, isActive = false } })
        {
            using var invalid = await admin.PostAsJsonAsync(Policies, payload);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => admin.PostAsJsonAsync(Policies, new { name = "Concurrent", priority = TicketPriority.Normal, firstResponseMinutes = 1, resolutionMinutes = 2 })));
        try
        {
            Assert.Single(attempts, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Single(attempts, x => x.StatusCode == HttpStatusCode.Conflict);
            var policy = (await attempts.Single(x => x.StatusCode == HttpStatusCode.Created).Content.ReadFromJsonAsync<SlaPolicyDetails>())!;
            test.Data.OwnSlaPolicy(policy.Id);
            using var stale = await admin.PatchAsJsonAsync($"{Policies}/{policy.Id}", new { name = "New", priority = policy.Priority, firstResponseMinutes = 2, resolutionMinutes = 3, version = policy.Version + 1 });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var page = (await admin.GetFromJsonAsync<PagedResult<SlaPolicyDetails>>(Policies + "?pageSize=1"))!;
            Assert.Equal(policy.Id, Assert.Single(page.Items).Id);
        }
        finally { foreach (var attempt in attempts) attempt.Dispose(); }
    }

    [PostgresFact]
    public async Task CreationSnapshotsMatchingActivePolicyAndUpdatesOnlyAffectFutureTickets()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        var policy = await CreatePolicy(test, admin);
        var old = await CreateTicket(test, admin);
        var snapshot = (await admin.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{old.Id}/sla"))!.Sla!;
        Assert.Equal(policy.Id, snapshot.PolicyId);
        Assert.Equal(old.CreatedAt.AddMinutes(60).Ticks / 10, snapshot.FirstResponseDueAt.Ticks / 10);
        using var update = await admin.PatchAsJsonAsync($"{Policies}/{policy.Id}", new { name = "Changed", priority = policy.Priority, firstResponseMinutes = 10, resolutionMinutes = 20, version = policy.Version });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var changed = (await update.Content.ReadFromJsonAsync<SlaPolicyDetails>())!;
        Assert.NotEqual(policy.Version, changed.Version);
        Assert.Equal(snapshot, (await admin.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{old.Id}/sla"))!.Sla);
        var next = await CreateTicket(test, admin);
        Assert.Equal(10, (await admin.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{next.Id}/sla"))!.Sla!.FirstResponseMinutes);
        using var deactivate = await admin.PutAsJsonAsync($"{Policies}/{policy.Id}/status", new { isActive = false, version = changed.Version });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        var without = await CreateTicket(test, admin);
        Assert.Null((await admin.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{without.Id}/sla"))!.Sla);
        var high = await CreateTicket(test, admin, TicketPriority.High);
        Assert.Null((await admin.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{high.Id}/sla"))!.Sla);
        Assert.Equal(snapshot, (await admin.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{old.Id}/sla"))!.Sla);
    }

    [PostgresFact]
    public async Task FirstResponseOnlyAcceptsPublicNonRequesterWithCurrentProcessingScopeOnce()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        var teammate = await test.Data.UserAsync(RoleNames.SupportAgent);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        await test.Data.MemberAsync(teammate.Id, test.Data.TeamA);
        var ticket = await test.Data.TicketAsync(owner.Id, test.Data.TeamA, agent.Id);
        await AddSnapshot(test, ticket.Id, DateTime.UtcNow, 60, 240);
        using var requester = await test.ClientAsync(owner);
        using var processor = await test.ClientAsync(agent);
        using var otherAgent = await test.ClientAsync(teammate);
        using var ownerComment = await CommentAsync(requester, ticket.Id);
        await test.Data.Accounts.AssignAsync(owner.Id, await test.Data.Accounts.RoleIdAsync(RoleNames.SystemAdmin));
        using var ownerWithProcessingGrant = await CommentAsync(requester, ticket.Id);
        using var internalNote = await CommentAsync(processor, ticket.Id, true);
        using var bystanderComment = await CommentAsync(otherAgent, ticket.Id);
        Assert.Equal(HttpStatusCode.Created, bystanderComment.StatusCode);
        await using var database = test.Data.Accounts.Database();
        Assert.Null((await database.TicketSlas.AsNoTracking().SingleAsync(x => x.TicketId == ticket.Id)).FirstRespondedAt);
        using var valid = await CommentAsync(processor, ticket.Id);
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        var first = (await database.TicketSlas.AsNoTracking().SingleAsync(x => x.TicketId == ticket.Id)).FirstRespondedAt;
        Assert.NotNull(first);
        using var again = await CommentAsync(processor, ticket.Id);
        Assert.Equal(first, (await database.TicketSlas.AsNoTracking().SingleAsync(x => x.TicketId == ticket.Id)).FirstRespondedAt);
        Assert.Equal(ticket.Version, (await test.Data.StoredAsync(ticket.Id)).Version);
    }

    [PostgresFact]
    public async Task OverdueWorkersAreIdempotentAtomicAndUseCurrentNotificationAcl()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        await AddSnapshot(test, ticket.Id);
        using var requester = await test.ClientAsync(owner);
        var before = (await requester.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{ticket.Id}/sla"))!.Sla!;
        Assert.True(before.FirstResponseBreached);
        Assert.Null(before.FirstResponseBreachedAt);
        var runs = await Task.WhenAll(Process(test), Process(test));
        Assert.Equal(1, runs.Sum());
        Assert.Equal(0, await Process(test));
        await using var database = test.Data.Accounts.Database();
        var sla = await database.TicketSlas.SingleAsync(x => x.TicketId == ticket.Id);
        Assert.NotNull(sla.FirstResponseBreachedAt);
        Assert.NotNull(sla.ResolutionBreachedAt);
        var activities = await database.TicketActivities.Where(x => x.TicketId == ticket.Id).ToListAsync();
        Assert.Equal(2, activities.Count);
        Assert.All(activities, x => Assert.Null(x.ActorId));
        var notifications = await database.Notifications.Where(x => x.TicketId == ticket.Id && x.UserId == owner.Id).ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, x => Assert.Null(x.DispatchedAt));
        Assert.Empty(test.Sender.Sent);
        var timeline = (await requester.GetFromJsonAsync<PagedResult<TicketActivityDetails>>($"/api/v1/tickets/{ticket.Id}/activities"))!;
        Assert.Equal(2, timeline.TotalCount);
        Assert.All(timeline.Items, x => Assert.NotNull(x.NewValue!.DueAt));
    }

    [PostgresFact]
    public async Task SnapshotReadUsesTicketAclAndOldTicketsNeverGetRetroactiveSla()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var outsider = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        using var requester = await test.ClientAsync(owner);
        using var other = await test.ClientAsync(outsider);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        await CreatePolicy(test, admin);
        Assert.Null((await requester.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{ticket.Id}/sla"))!.Sla);
        using var denied = await other.GetAsync($"/api/v1/tickets/{ticket.Id}/sla");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        await Process(test);
        Assert.Null((await requester.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{ticket.Id}/sla"))!.Sla);
    }

    [PostgresFact]
    public async Task LatePublicResponseCreatesBreachAndRollbackAfterSaveRestoresAllRelatedRows()
    {
        var interceptor = new FailSlaSave();
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, configure: services => services.Replace(
            ServiceDescriptor.Scoped<HelpdeskDbContext>(_ => new(new DbContextOptionsBuilder<HelpdeskDbContext>()
                .UseNpgsql(fixture.ConnectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk"))
                .AddInterceptors(interceptor).Options))));
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        await AddSnapshot(test, ticket.Id);
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        using var failed = await CommentAsync(admin, ticket.Id);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.True(interceptor.Fired);
        await using var database = test.Data.Accounts.Database();
        var row = await database.TicketSlas.AsNoTracking().SingleAsync(x => x.TicketId == ticket.Id);
        Assert.Null(row.FirstRespondedAt);
        Assert.Null(row.FirstResponseBreachedAt);
        Assert.False(await database.TicketComments.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.False(await database.TicketActivities.AnyAsync(x => x.TicketId == ticket.Id));
        Assert.False(await database.Notifications.AnyAsync(x => x.TicketId == ticket.Id));
        interceptor.Enabled = false;
        using var success = await CommentAsync(admin, ticket.Id);
        Assert.Equal(HttpStatusCode.Created, success.StatusCode);
        row = await database.TicketSlas.AsNoTracking().SingleAsync(x => x.TicketId == ticket.Id);
        Assert.NotNull(row.FirstRespondedAt);
        Assert.NotNull(row.FirstResponseBreachedAt);
        Assert.NotNull(row.ResolutionBreachedAt);
    }

    [PostgresFact]
    public async Task ResolutionAndReopeningKeepTargetsStopClocksAndNeverRepeatBreachActivities()
    {
        var clock = new SlaClock(DateTime.UtcNow);
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, configure: services =>
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        var ticket = await test.Data.TicketAsync(owner.Id, test.Data.TeamA, agent.Id, status: TicketStatus.InProgress);
        var initial = await AddSnapshot(test, ticket.Id, clock.GetUtcNow().UtcDateTime, 1, 2);
        using var processor = await test.ClientAsync(agent);
        using var requester = await test.ClientAsync(owner);
        using var resolved = await processor.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/transition", new { status = TicketStatus.Resolved, resolutionSummary = "Done", version = ticket.Version });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var receipt = (await resolved.Content.ReadFromJsonAsync<TicketMutationResult>())!;
        clock.Now = clock.Now.AddMinutes(5);
        Assert.Equal(0, await Process(test));
        var stopped = (await requester.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{ticket.Id}/sla"))!.Sla!;
        Assert.False(stopped.FirstResponseBreached);
        Assert.False(stopped.ResolutionBreached);
        Assert.Null(stopped.FirstRespondedAt);
        using var reopened = await requester.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/transition", new { status = TicketStatus.Reopened, version = receipt.Version });
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        var active = (await requester.GetFromJsonAsync<TicketSlaDetails>($"/api/v1/tickets/{ticket.Id}/sla"))!.Sla!;
        Assert.Null(active.ResolvedAt);
        Assert.True(active.FirstResponseBreached);
        Assert.True(active.ResolutionBreached);
        Assert.Equal(initial.FirstResponseDueAt.Ticks / 10, active.FirstResponseDueAt.Ticks / 10);
        Assert.Equal(initial.ResolutionDueAt.Ticks / 10, active.ResolutionDueAt.Ticks / 10);
        Assert.Equal(0, await Process(test));
        await using var database = test.Data.Accounts.Database();
        Assert.Equal(2, await database.TicketActivities.CountAsync(x => x.TicketId == ticket.Id &&
            (x.Action == TicketActivityActions.SlaFirstResponseBreached || x.Action == TicketActivityActions.SlaResolutionBreached)));
    }

    [PostgresFact]
    public async Task WorkerHonorsBatchWaitingClockAndSkipsClosedCancelledAndOnTimeCompletedSnapshots()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, configure: services => services.Configure<SlaOptions>(options => options.BatchSize = 1));
        var waiting = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, status: TicketStatus.WaitingForRequester);
        var next = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id);
        var closed = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, status: TicketStatus.Closed);
        var cancelled = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, status: TicketStatus.Cancelled);
        var resolved = await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, status: TicketStatus.Resolved);
        foreach (var id in new[] { waiting.Id, next.Id, closed.Id, cancelled.Id, resolved.Id }) await AddSnapshot(test, id);
        await using var database = test.Data.Accounts.Database();
        var response = DateTime.UtcNow.AddHours(-1).AddSeconds(20);
        var resolution = response.AddSeconds(20);
        await database.TicketSlas.Where(x => x.TicketId == resolved.Id).ExecuteUpdateAsync(set =>
            set.SetProperty(x => x.FirstRespondedAt, response).SetProperty(x => x.ResolvedAt, resolution));
        Assert.Equal(1, await Process(test));
        Assert.Equal(1, await Process(test));
        Assert.Equal(0, await Process(test));
        Assert.False(await database.TicketActivities.AnyAsync(x => x.TicketId == closed.Id || x.TicketId == cancelled.Id || x.TicketId == resolved.Id));
        Assert.Equal(2, await database.TicketActivities.CountAsync(x => x.TicketId == waiting.Id));
    }

    [PostgresFact]
    public async Task FailedSnapshotSaveRollsBackNewTicketAndMigrationRollbackPreservesCommittedSnapshot()
    {
        var probe = new FailSnapshotSave();
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, configure: services => services.Replace(
            ServiceDescriptor.Scoped<HelpdeskDbContext>(_ => new(new DbContextOptionsBuilder<HelpdeskDbContext>()
                .UseNpgsql(fixture.ConnectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk"))
                .AddInterceptors(probe).Options))));
        using var admin = await test.ClientAsync(test.Data.Accounts.AdminUser);
        await CreatePolicy(test, admin);
        using var failed = await admin.PostAsJsonAsync("/api/v1/tickets", new { title = test.Data.Prefix + "Failed", description = "Description", categoryId = test.Data.Category });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.True(probe.Fired);
        await using var database = test.Data.Accounts.Database();
        Assert.False(await database.Tickets.AnyAsync(x => x.Title == test.Data.Prefix + "Failed"));
        probe.Enabled = false;
        var ticket = await CreateTicket(test, admin);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var rollback = new NpgsqlCommand(ModelTests.ReadSql("SlaMvp.Rollback.sql"), connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => rollback.ExecuteNonQueryAsync());
        Assert.Equal("P0001", error.SqlState);
        await connection.CloseAsync();
        Assert.Contains("20261028122927_SlaMvp", await database.Database.GetAppliedMigrationsAsync());
        Assert.NotNull((await database.TicketSlas.SingleAsync(x => x.TicketId == ticket.Id)).PolicyName);
    }

    [PostgresFact]
    public async Task PolicyServiceRechecksRevokedManageGrantInsideMutationTransaction()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var manager = await test.Data.CustomUserAsync(PermissionCodes.TicketManageSla);
        using var client = await test.ClientAsync(manager);
        var actor = TicketAccessTestContext.Actor(client);
        await using var database = test.Data.Accounts.Database();
        await database.UserRoles.Where(x => x.UserId == manager.Id).ExecuteDeleteAsync();
        await using var scope = test.Factory.Services.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<SlaOperationException>(() => scope.ServiceProvider.GetRequiredService<SlaService>()
            .CreateAsync(actor, new("Denied", TicketPriority.Low, 1, 2), CancellationToken.None));
        Assert.Equal(SlaError.Forbidden, error.Error);
        Assert.False(await database.SlaPolicies.AnyAsync(x => x.Name == "Denied"));
    }

    private sealed class SlaClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private sealed class FailSnapshotSave : SaveChangesInterceptor
    {
        public bool Enabled { get; set; } = true;
        public bool Fired { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<TicketSla>().Any(x => x.Entity.PolicyName != null))
            { Fired = true; throw new InvalidOperationException("Snapshot SQL rollback probe."); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FailSlaSave : SaveChangesInterceptor
    {
        public bool Enabled { get; set; } = true;
        public bool Fired { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<TicketSla>().Any(x => x.Entity.FirstResponseBreachedAt != null))
            { Fired = true; throw new InvalidOperationException("SLA SQL rollback probe."); }
            return ValueTask.FromResult(result);
        }
    }
}
