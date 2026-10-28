using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using be.Application.Features.Reporting;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Domain.Rules;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Helpdesk.Persistence.Tests.NotificationTestContext;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
public class ReportingTests(PostgresFixture fixture)
{
    private static async Task RecentAsync(NotificationTestContext test)
    {
        await using var database = test.Data.Accounts.Database();
        var createdAt = DateTime.UtcNow.AddMinutes(-5);
        // The shared ACL fixture deliberately dates Tickets in January; dashboard defaults to the last 30 UTC dates.
        await database.Tickets.Where(x => x.CategoryId == test.Data.Category).ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedAt, createdAt));
    }

    [PostgresFact]
    public async Task ReadPermissionsAreRequiredForEveryReportAndAdminPermissionAloneDoesNotGrantTicketAccess()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var manager = await test.Data.CustomUserAsync(PermissionCodes.UserManage, PermissionCodes.TicketManageSla);
        using var client = await test.ClientAsync(manager);
        foreach (var path in new[] { "/api/v1/dashboard", "/api/v1/reports/tickets/teams", "/api/v1/reports/tickets/agents", "/api/v1/reports/tickets/daily" })
        {
            using var denied = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
    }

    [PostgresFact]
    public async Task CombinedFiltersNarrowTheScopeAndSuccessfulResponsesCannotBeCached()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var outsider = await test.Data.UserAsync(RoleNames.Requester);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        var selected = await test.Data.TicketAsync(owner.Id, test.Data.TeamA, agent.Id, status: TicketStatus.WaitingForRequester);
        var hidden = await test.Data.TicketAsync(outsider.Id, test.Data.TeamA, agent.Id, status: TicketStatus.WaitingForRequester);
        await test.Data.TicketAsync(owner.Id, test.Data.TeamA, agent.Id);
        await test.Data.TicketAsync(owner.Id, test.Data.TeamB);
        await RecentAsync(test);
        await using var database = test.Data.Accounts.Database();
        await database.Tickets.Where(x => x.Id == selected.Id || x.Id == hidden.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.Priority, TicketPriority.High));
        using var client = await test.ClientAsync(owner);
        using var response = await client.GetAsync($"/api/v1/dashboard?teamId={test.Data.TeamA}&agentId={agent.Id}&status=3&priority=3");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var summary = (await response.Content.ReadFromJsonAsync<DashboardReport>())!;
        Assert.Equal(1, summary.Tickets.Total);
        Assert.Equal(1, summary.ByStatus.Single(x => x.Key == "WaitingForRequester").Count);
        Assert.Equal(1, summary.ByPriority.Single(x => x.Key == "High").Count);
    }

    [PostgresFact]
    public async Task DailySeriesIncludesAsOfMidnightWhenTheRequestedEndIsLater()
    {
        var now = DateTime.UtcNow.Date;
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new ReportClock(now))));
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var ticket = await test.Data.TicketAsync(owner.Id);
        await using var database = test.Data.Accounts.Database();
        await database.Tickets.Where(x => x.Id == ticket.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedAt, now));
        using var client = await test.ClientAsync(owner);
        var range = $"?createdFrom={now.AddDays(-1):yyyy-MM-dd}T00:00:00Z&createdTo={now.AddDays(1):yyyy-MM-dd}T00:00:00Z";
        var result = (await client.GetFromJsonAsync<DailyTicketReport>("/api/v1/reports/tickets/daily" + range))!;
        Assert.Equal(new long[] { 0, 1 }, result.Days.Select(x => x.CreatedTickets));
    }
    [PostgresTheory]
    [InlineData("membership")]
    [InlineData("team")]
    [InlineData("department")]
    [InlineData("grant")]
    [InlineData("session")]
    [InlineData("user")]
    public async Task ReportsUseCurrentMembershipGrantsUserAndSession(string change)
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var agent = await test.Data.CustomUserAsync(PermissionCodes.TicketReadOwn, PermissionCodes.TicketReadTeam);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, test.Data.TeamA);
        await test.Data.TicketAsync(agent.Id);
        await RecentAsync(test);
        using var client = await test.ClientAsync(agent);
        Assert.Equal(2, (await client.GetFromJsonAsync<DashboardReport>("/api/v1/dashboard"))!.Tickets.Total);
        await using var database = test.Data.Accounts.Database();
        if (change == "membership") await database.SupportTeamMembers.Where(x => x.UserId == agent.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        if (change == "team") await database.SupportTeams.Where(x => x.Id == test.Data.TeamA).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        if (change == "department") await database.Departments.Where(x => x.Id == test.Data.DepartmentA).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        if (change == "grant")
        {
            var roles = database.UserRoles.Where(x => x.UserId == agent.Id).Select(x => x.RoleId);
            var permission = await database.Permissions.SingleAsync(x => x.Code == PermissionCodes.TicketReadTeam);
            await database.RolePermissions.Where(x => roles.Contains(x.RoleId) && x.PermissionId == permission.Id).ExecuteDeleteAsync();
        }
        if (change == "user") await database.Users.Where(x => x.Id == agent.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        if (change == "session")
        {
            var actor = TicketAccessTestContext.Actor(client);
            var revokedAt = DateTime.UtcNow;
            await database.RefreshTokens.Where(x => x.Id == actor.SessionId).ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAt, revokedAt));
        }
        using var response = await client.GetAsync("/api/v1/dashboard");
        if (change is "session" or "user") Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        else Assert.Equal(1, (await response.Content.ReadFromJsonAsync<DashboardReport>())!.Tickets.Total);
    }

    [PostgresTheory]
    [InlineData("/api/v1/dashboard", "Reports:dashboard")]
    [InlineData("/api/v1/reports/tickets/teams", "Reports:groups-page")]
    public async Task PermissionScopeChangeDuringAggregationDiscardsTheResponse(string path, string tag)
    {
        var probe = new ReportProbe(tag);
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, configure: services => services.Replace(
            ServiceDescriptor.Scoped<HelpdeskDbContext>(_ => new(new DbContextOptionsBuilder<HelpdeskDbContext>()
                .UseNpgsql(fixture.ConnectionString, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", "helpdesk"))
                .AddInterceptors(probe).Options))));
        var agent = await test.Data.CustomUserAsync(PermissionCodes.TicketReadOwn, PermissionCodes.TicketReadTeam);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        await test.Data.TicketAsync(test.Data.Accounts.AdminUser.Id, test.Data.TeamA);
        await RecentAsync(test);
        using var client = await test.ClientAsync(agent);
        probe.AfterQuery = async () =>
        {
            await using var database = test.Data.Accounts.Database();
            await database.SupportTeamMembers.Where(x => x.UserId == agent.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        };
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(probe.Fired);
        Assert.DoesNotContain("groups", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task SqlSlaBreachClassificationMatchesDomainRulesForAllStatusesAndCompletionBoundaries()
    {
        var now = DateTime.UtcNow.Date;
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString, configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new ReportClock(now))));
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        await using var database = test.Data.Accounts.Database();
        var policy = new SlaPolicy { Name = "Parity", FirstResponseMinutes = 60, ResolutionMinutes = 90, IsActive = false };
        database.SlaPolicies.Add(policy); test.Data.OwnSlaPolicy(policy.Id);
        var applied = now.AddHours(-3);
        var cases = new List<(TicketStatus Status, TicketSla Sla)>();
        foreach (var status in Enum.GetValues<TicketStatus>())
        foreach (var response in new DateTime?[] { null, applied.AddMinutes(60), applied.AddMinutes(61) })
        foreach (var resolution in new DateTime?[] { null, applied.AddMinutes(90), applied.AddMinutes(91) })
        {
            var ticket = new Ticket { RequesterId = owner.Id, CreatedByUserId = owner.Id, CategoryId = test.Data.Category, Title = "Parity", Description = "Private",
                Status = status, CreatedAt = applied, ResolutionSummary = status is TicketStatus.Resolved or TicketStatus.Closed ? "Done" : null,
                ResolvedAt = status is TicketStatus.Resolved or TicketStatus.Closed ? now.AddMinutes(-10) : null, ClosedAt = status == TicketStatus.Closed ? now : null };
            var snapshot = new TicketSla { TicketId = ticket.Id, SlaPolicyId = policy.Id, AppliedAt = applied,
                FirstResponseDueAt = applied.AddHours(1), ResolutionDueAt = applied.AddMinutes(90), FirstRespondedAt = response, ResolvedAt = resolution };
            if (response == null && resolution == null) snapshot.FirstResponseBreachedAt = now.AddMinutes(-1);
            test.Data.OwnTicket(ticket.Id); database.AddRange(ticket, snapshot); cases.Add((status, snapshot));
        }
        await database.SaveChangesAsync();
        using var client = await test.ClientAsync(owner);
        var report = (await client.GetFromJsonAsync<DashboardReport>("/api/v1/dashboard"))!;
        var eligible = cases.Where(x => x.Status != TicketStatus.Cancelled).ToArray();
        Assert.Equal(eligible.LongLength, report.Sla.Eligible);
        Assert.Equal(eligible.LongCount(x => SlaRules.FirstResponseBreached(x.Sla, x.Status, now)), report.Sla.FirstResponse.Breached);
        Assert.Equal(eligible.LongCount(x => SlaRules.ResolutionBreached(x.Sla, x.Status, now)), report.Sla.Resolution.Breached);
        Assert.Equal(eligible.LongLength, report.Sla.FirstResponse.Met + report.Sla.FirstResponse.Breached + report.Sla.FirstResponse.Pending + report.Sla.FirstResponse.Stopped + report.Sla.FirstResponse.Unknown);
        Assert.Equal(eligible.LongLength, report.Sla.Resolution.Met + report.Sla.Resolution.Breached + report.Sla.Resolution.Pending + report.Sla.Resolution.Unknown);
        Assert.True(report.Sla.FirstResponse.Unknown > 0);
        Assert.True(report.Sla.Resolution.Unknown > 0);
    }

    private sealed class ReportClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
    private sealed class ReportProbe(string tag) : DbCommandInterceptor
    {
        public Func<Task>? AfterQuery { get; set; }
        public bool Fired { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Fired && AfterQuery is not null && command.CommandText.Contains(tag, StringComparison.Ordinal))
            { Fired = true; await AfterQuery(); }
            return result;
        }
    }

    [PostgresFact]
    public async Task DashboardAggregatesCurrentTicketScopeWithoutDuplicatingMembershipsOrLeakingInternalContent()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        var outsider = await test.Data.UserAsync(RoleNames.Requester);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        var own = await test.Data.TicketAsync(owner.Id);
        var team = await test.Data.TicketAsync(outsider.Id, test.Data.TeamA, agent.Id, status: TicketStatus.WaitingForRequester);
        await test.Data.TicketAsync(outsider.Id, test.Data.TeamB, status: TicketStatus.Closed);
        await test.Data.Accounts.AssignAsync(agent.Id, await test.Data.Accounts.RoleIdAsync(RoleNames.TeamLeader));
        await RecentAsync(test);
        using var requester = await test.ClientAsync(owner);
        using var processor = await test.ClientAsync(agent);
        var mine = (await requester.GetFromJsonAsync<DashboardReport>("/api/v1/dashboard"))!;
        Assert.Equal(1, mine.Tickets.Total);
        Assert.Equal(1, mine.Tickets.UnassignedTeam);
        Assert.Equal(1, mine.ByStatus.Single(x => x.Key == "New").Count);
        Assert.Equal(mine.Tickets.Total, mine.ByStatus.Sum(x => x.Count));
        Assert.Equal(mine.Tickets.Total, mine.ByPriority.Sum(x => x.Count));
        var currentTeam = (await processor.GetFromJsonAsync<DashboardReport>("/api/v1/dashboard"))!;
        Assert.Equal(1, currentTeam.Tickets.Total);
        Assert.Equal(1, currentTeam.ByStatus.Single(x => x.Key == "WaitingForRequester").Count);
        using var guessed = await requester.GetAsync("/api/v1/dashboard?teamId=" + test.Data.TeamB);
        Assert.Equal(0, (await guessed.Content.ReadFromJsonAsync<DashboardReport>())!.Tickets.Total);
        using var fakeUser = await requester.GetAsync("/api/v1/dashboard?userId=" + outsider.Id);
        Assert.Equal(1, (await fakeUser.Content.ReadFromJsonAsync<DashboardReport>())!.Tickets.Total);
        await using var database = test.Data.Accounts.Database();
        database.TicketComments.Add(new TicketComment { TicketId = own.Id, AuthorId = agent.Id, IsInternal = true, Content = "internal-dashboard-marker" });
        await database.SaveChangesAsync();
        using var body = await requester.GetAsync("/api/v1/dashboard");
        var json = await body.Content.ReadAsStringAsync();
        Assert.DoesNotContain("internal-dashboard-marker", json);
        Assert.DoesNotContain("description", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task TeamsAndAgentsPaginateOnlyVisibleGroupsAndKeepAnUnassignedBucket()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var other = await test.Data.UserAsync(RoleNames.Requester);
        var agent = await test.Data.UserAsync(RoleNames.SupportAgent);
        await test.Data.MemberAsync(agent.Id, test.Data.TeamA);
        await test.Data.TicketAsync(owner.Id, test.Data.TeamA, agent.Id);
        await test.Data.TicketAsync(owner.Id, test.Data.TeamA, agent.Id);
        await test.Data.TicketAsync(owner.Id);
        await test.Data.TicketAsync(other.Id, test.Data.TeamB);
        await RecentAsync(test);
        using var client = await test.ClientAsync(owner);
        foreach (var dimension in new[] { "teams", "agents" })
        {
            var page = (await client.GetFromJsonAsync<GroupedTicketReport>($"/api/v1/reports/tickets/{dimension}?pageSize=1"))!;
            Assert.Equal(2, page.Groups.TotalCount);
            Assert.Equal(2, Assert.Single(page.Groups.Items).Tickets.Total);
            Assert.Equal(dimension == "teams" ? test.Data.TeamA : agent.Id, page.Groups.Items[0].Id);
            var last = (await client.GetFromJsonAsync<GroupedTicketReport>($"/api/v1/reports/tickets/{dimension}?pageSize=1&page=2"))!;
            Assert.Null(Assert.Single(last.Groups.Items).Id);
            Assert.Equal(1, last.Groups.Items[0].Tickets.Total);
            using var invalidScope = await client.GetAsync($"/api/v1/reports/tickets/{dimension}?teamId={test.Data.TeamB}");
            Assert.Empty((await invalidScope.Content.ReadFromJsonAsync<GroupedTicketReport>())!.Groups.Items);
        }
        await using var database = test.Data.Accounts.Database();
        await database.Users.Where(x => x.Id == agent.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        var retained = (await client.GetFromJsonAsync<GroupedTicketReport>("/api/v1/reports/tickets/agents"))!;
        Assert.False(retained.Groups.Items.Single(x => x.Id == agent.Id).IsActive);
    }

    [PostgresFact]
    public async Task SlaOutcomesExcludeCancelledAndMissingSnapshotsAndDoNotClaimMissingResponsesAsMet()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        using var client = await test.ClientAsync(owner);
        var met = await test.Data.TicketAsync(owner.Id, status: TicketStatus.Resolved);
        var late = await test.Data.TicketAsync(owner.Id, status: TicketStatus.WaitingForRequester);
        var pending = await test.Data.TicketAsync(owner.Id);
        var stopped = await test.Data.TicketAsync(owner.Id, status: TicketStatus.Resolved);
        var cancelled = await test.Data.TicketAsync(owner.Id, status: TicketStatus.Cancelled);
        await test.Data.TicketAsync(owner.Id);
        await RecentAsync(test);
        var now = DateTime.UtcNow;
        await using var database = test.Data.Accounts.Database();
        var policy = new SlaPolicy { Name = "Reporting", FirstResponseMinutes = 60, ResolutionMinutes = 120, IsActive = false };
        database.SlaPolicies.Add(policy); test.Data.OwnSlaPolicy(policy.Id);
        foreach (var ticket in new[] { met, late, pending, stopped, cancelled })
        {
            var applied = ticket.Id == late.Id ? now.AddHours(-3) : now.AddMinutes(-30);
            database.TicketSlas.Add(new TicketSla { TicketId = ticket.Id, SlaPolicyId = policy.Id, AppliedAt = applied,
                FirstResponseDueAt = applied.AddHours(1), ResolutionDueAt = applied.AddHours(2),
                FirstRespondedAt = ticket.Id == met.Id ? applied.AddMinutes(10) : null,
                ResolvedAt = ticket.Id == met.Id || ticket.Id == stopped.Id ? applied.AddMinutes(20) : null,
                FirstResponseBreachedAt = ticket.Id == cancelled.Id ? now : null });
        }
        await database.SaveChangesAsync();
        var report = (await client.GetFromJsonAsync<DashboardReport>("/api/v1/dashboard"))!;
        Assert.Equal(6, report.Tickets.Total);
        Assert.Equal(5, report.Sla.WithSla);
        Assert.Equal(1, report.Sla.WithoutSla);
        Assert.Equal(1, report.Sla.ExcludedCancelled);
        Assert.Equal(4, report.Sla.Eligible);
        Assert.Equal(1, report.Sla.FirstResponse.Met);
        Assert.Equal(1, report.Sla.FirstResponse.Breached);
        Assert.Equal(1, report.Sla.FirstResponse.Pending);
        Assert.Equal(1, report.Sla.FirstResponse.Stopped);
        Assert.Equal(0, report.Sla.FirstResponse.Unknown);
        Assert.Equal(50m, report.Sla.FirstResponse.MetPercent);
        Assert.Equal(2, report.Sla.Resolution.Met);
        Assert.Equal(1, report.Sla.Resolution.Breached);
        Assert.Equal(1, report.Sla.Resolution.Pending);
        Assert.Equal(66.67m, report.Sla.Resolution.MetPercent);
        Assert.Equal(10d, report.Sla.FirstResponse.AverageMinutes);
        Assert.Equal(20d, report.Sla.Resolution.AverageMinutes);
    }

    [PostgresFact]
    public async Task DateRangeIsUtcHalfOpenAndDailySeriesFillsMissingDaysAfterAggregation()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        var first = await test.Data.TicketAsync(owner.Id);
        var second = await test.Data.TicketAsync(owner.Id);
        var boundary = await test.Data.TicketAsync(owner.Id);
        var from = DateTime.UtcNow.Date.AddDays(-3);
        await using var database = test.Data.Accounts.Database();
        await database.Tickets.Where(x => x.Id == first.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedAt, from.AddHours(23)));
        await database.Tickets.Where(x => x.Id == second.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedAt, from.AddDays(2)));
        await database.Tickets.Where(x => x.Id == boundary.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.CreatedAt, from.AddDays(3)));
        using var client = await test.ClientAsync(owner);
        var range = "?createdFrom=" + Uri.EscapeDataString(new DateTimeOffset(from).ToOffset(TimeSpan.FromHours(7)).ToString("O")) +
            "&createdTo=" + Uri.EscapeDataString(new DateTimeOffset(from.AddDays(3)).ToString("O"));
        var dashboard = (await client.GetFromJsonAsync<DashboardReport>("/api/v1/dashboard" + range))!;
        var daily = (await client.GetFromJsonAsync<DailyTicketReport>("/api/v1/reports/tickets/daily" + range))!;
        Assert.Equal(2, dashboard.Tickets.Total);
        Assert.Equal(new long[] { 1, 0, 1 }, daily.Days.Select(x => x.CreatedTickets));
        Assert.All(daily.Days, x => Assert.Equal(DateTimeKind.Utc, x.Date.Kind));
        Assert.Equal(dashboard.Tickets.Total, daily.Days.Sum(x => x.CreatedTickets));
    }

    [PostgresFact]
    public async Task EmptyAndInvalidReportsHaveStableContractsAndAuthenticationIsRequired()
    {
        await using var test = await NotificationTestContext.CreateAsync(fixture.ConnectionString);
        var owner = await test.Data.UserAsync(RoleNames.Requester);
        using var client = await test.ClientAsync(owner);
        using var anonymous = test.Factory.CreateClient();
        using var denied = await anonymous.GetAsync("/api/v1/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var empty = (await client.GetFromJsonAsync<DashboardReport>("/api/v1/dashboard"))!;
        Assert.Equal(0, empty.Tickets.Total);
        Assert.Equal(7, empty.ByStatus.Count);
        Assert.Equal(4, empty.ByPriority.Count);
        Assert.Null(empty.Sla.FirstResponse.MetPercent);
        Assert.Null(empty.Sla.Resolution.AverageMinutes);
        foreach (var query in new[] { "status=999", "priority=0", "teamId=" + Guid.Empty, "agentId=" + Guid.Empty,
            "createdFrom=2026-01-01Z&createdTo=2025-12-01Z", "createdFrom=2024-01-01T00:00:00Z&createdTo=2026-01-01T00:00:00Z" })
        {
            using var invalid = await client.GetAsync("/api/v1/dashboard?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        foreach (var query in new[] { "page=0", "pageSize=101", "sortBy=name", "descending=invalid" })
        {
            using var invalid = await client.GetAsync("/api/v1/reports/tickets/teams?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }
}
