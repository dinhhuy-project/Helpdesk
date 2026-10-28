using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using be.Application.Features.Accounts;
using be.Application.Features.Tickets;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Infrastructure.Persistence;
using be.Infrastructure.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TestAccounts = Helpdesk.Persistence.Tests.AccountAdministrationTests.TestAccounts;
using TestUser = Helpdesk.Persistence.Tests.AccountAdministrationTests.TestUser;

namespace Helpdesk.Persistence.Tests;

internal sealed class TicketAccessTestContext(TestAccounts accounts) : IAsyncDisposable
{
    private readonly List<Guid> _tickets = [];
    private readonly List<Guid> _categories = [];
    private readonly List<Guid> _slaPolicies = [];
    public TestAccounts Accounts { get; } = accounts;
    public string Prefix { get; } = Guid.NewGuid().ToString("N") + "-";
    public Guid DepartmentA { get; private set; }
    public Guid DepartmentB { get; private set; }
    public Guid TeamA { get; private set; }
    public Guid TeamB { get; private set; }
    public Guid Category { get; private set; }
    public void OwnTicket(Guid id) => _tickets.Add(id);
    public void OwnCategory(Guid id) => _categories.Add(id);
    public void OwnSlaPolicy(Guid id) => _slaPolicies.Add(id);
    public static async Task<TicketAccessTestContext> CreateAsync(string connection)
    {
        var test = new TicketAccessTestContext(await TestAccounts.CreateAsync(connection));
        await using var database = test.Accounts.Database();
        var first = new Department { Name = test.Prefix + "Department A" };
        var second = new Department { Name = test.Prefix + "Department B" };
        var teamA = new SupportTeam { DepartmentId = first.Id, Name = "Team A" };
        var teamB = new SupportTeam { DepartmentId = second.Id, Name = "Team B" };
        var category = new TicketCategory { Name = test.Prefix + "Category" };
        database.AddRange(first, second, teamA, teamB, category);
        await database.SaveChangesAsync();
        (test.DepartmentA, test.DepartmentB, test.TeamA, test.TeamB, test.Category) = (first.Id, second.Id, teamA.Id, teamB.Id, category.Id);
        test.OwnCategory(category.Id);
        return test;
    }
    public async Task<TestUser> UserAsync(string role)
    {
        var user = await Accounts.NewUserAsync();
        await Accounts.AssignAsync(user.Id, await Accounts.RoleIdAsync(role));
        return user;
    }
    public async Task<TestUser> CustomUserAsync(params string[] grants)
    {
        var user = await Accounts.NewUserAsync();
        await Accounts.AssignAsync(user.Id, await Accounts.CustomRoleAsync(grants));
        return user;
    }
    public async Task MemberAsync(Guid userId, Guid teamId, bool leader = false, bool active = true)
    {
        await using var database = Accounts.Database();
        database.SupportTeamMembers.Add(new SupportTeamMember { UserId = userId, SupportTeamId = teamId, IsLeader = leader, IsActive = active });
        await database.SaveChangesAsync();
    }
    public async Task<Ticket> TicketAsync(Guid requester, Guid? team = null, Guid? agent = null, Guid? creator = null, TicketStatus status = TicketStatus.New)
    {
        await using var database = Accounts.Database();
        var ticket = new Ticket { Title = Prefix + "Ticket " + _tickets.Count, Description = "private-description-" + Guid.NewGuid().ToString("N"),
            RequesterId = requester, CreatedByUserId = creator ?? Accounts.AdminUser.Id, CategoryId = Category, AssignedTeamId = team, AssignedAgentId = agent,
            Status = status, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(_tickets.Count) };
        if (status is TicketStatus.Resolved or TicketStatus.Closed) { ticket.ResolutionSummary = "Resolved"; ticket.ResolvedAt = DateTime.UtcNow; }
        if (status == TicketStatus.Closed) ticket.ClosedAt = DateTime.UtcNow;
        database.Tickets.Add(ticket);
        await database.SaveChangesAsync();
        _tickets.Add(ticket.Id);
        return ticket;
    }
    public async Task<Ticket> StoredAsync(Guid id)
    {
        await using var database = Accounts.Database();
        return await database.Tickets.AsNoTracking().SingleAsync(x => x.Id == id);
    }
    public static AccountActor Actor(HttpClient client)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        return new(Guid.Parse(token.Subject), Guid.Parse(token.Claims.Single(x => x.Type == "sid").Value));
    }
    public static Task<HttpResponseMessage> ProcessAsync(HttpClient client, Ticket ticket) =>
        client.PostAsync($"/__tests/ticket-scope/{ticket.Id}/process?version={ticket.Version}", null);
    public static Task<HttpResponseMessage> AssignAsync(HttpClient client, Ticket ticket, Guid teamId, Guid? agent = null) =>
        client.PostAsJsonAsync($"/__tests/ticket-scope/{ticket.Id}/assign", new { TeamId = teamId, AgentId = agent, ticket.Version });
    public static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }
    public async ValueTask DisposeAsync()
    {
        await using var database = Accounts.Database();
        await database.Notifications.Where(x => x.TicketId != null && _tickets.Contains(x.TicketId.Value)).ExecuteDeleteAsync();
        await database.TicketActivities.Where(x => _tickets.Contains(x.TicketId)).ExecuteDeleteAsync();
        await database.AttachmentUploadIntents.Where(x => _tickets.Contains(x.TicketId)).ExecuteDeleteAsync();
        await database.TicketAttachments.Where(x => _tickets.Contains(x.TicketId)).ExecuteDeleteAsync();
        await database.TicketComments.Where(x => _tickets.Contains(x.TicketId)).ExecuteDeleteAsync();
        await database.TicketSlas.Where(x => _tickets.Contains(x.TicketId)).ExecuteDeleteAsync();
        await database.Tickets.Where(x => _tickets.Contains(x.Id)).ExecuteDeleteAsync();
        await database.SlaPolicies.Where(x => _slaPolicies.Contains(x.Id)).ExecuteDeleteAsync();
        await database.SupportTeamMembers.Where(x => x.SupportTeamId == TeamA || x.SupportTeamId == TeamB).ExecuteDeleteAsync();
        await database.SupportTeams.Where(x => x.Id == TeamA || x.Id == TeamB).ExecuteDeleteAsync();
        // Users may still have department links deliberately set by a scope test.
        await database.Users.Where(x => x.DepartmentId == DepartmentA || x.DepartmentId == DepartmentB)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DepartmentId, (Guid?)null));
        await database.Departments.Where(x => x.Id == DepartmentA || x.Id == DepartmentB).ExecuteDeleteAsync();
        while (await database.TicketCategories.AnyAsync(x => _categories.Contains(x.Id)))
        {
            var removed = await database.TicketCategories.Where(x => _categories.Contains(x.Id) &&
                !database.TicketCategories.Any(child => child.ParentId == x.Id)).ExecuteDeleteAsync();
            if (removed == 0) throw new InvalidOperationException("Test-owned category tree could not be cleaned safely.");
        }
        await Accounts.DisposeAsync();
    }
}

// TestServer-only mutation harness: exercises the production guard with real saves/activity/rollback.
// Guard tests deliberately bypass workflow rules; production commands use TicketMutationService.
[ApiController]
[Authorize]
[Route("__tests/ticket-scope")]
public sealed class TicketScopeMutationProbeController(HelpdeskDbContext database, TicketWriteGuard guard) : ControllerBase
{
    [HttpPost("{id:guid}/process")]
    public async Task<IActionResult> Process(Guid id, [FromQuery] uint version, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var actor = CurrentActor();
        var ticket = await guard.LockAsync(actor, id, TicketOperation.UpdateStatus, version, cancellationToken);
        var previous = ticket.Status;
        ticket.Status = TicketStatus.InProgress;
        ticket.UpdatedAt = DateTime.UtcNow;
        database.TicketActivities.Add(new TicketActivity { TicketId = id, ActorId = actor.UserId, Action = "ScopeTestStatusChanged",
            OldValue = JsonSerializer.Serialize(new { Status = previous }), NewValue = JsonSerializer.Serialize(new { ticket.Status }) });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }
    [HttpPost("{id:guid}/assign")]
    public async Task<IActionResult> Assign(Guid id, TicketAssignmentProbeRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var actor = CurrentActor();
        var ticket = await guard.LockAssignmentAsync(actor, id, request.Version, new(request.TeamId, request.AgentId), cancellationToken);
        ticket.AssignedTeamId = request.TeamId;
        ticket.AssignedAgentId = request.AgentId;
        ticket.UpdatedAt = DateTime.UtcNow;
        database.TicketActivities.Add(new TicketActivity { TicketId = id, ActorId = actor.UserId, Action = "ScopeTestAssigned",
            NewValue = JsonSerializer.Serialize(new { request.TeamId, request.AgentId }) });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }
    private AccountActor CurrentActor() => new(Guid.Parse(User.FindFirst("sub")!.Value), Guid.Parse(User.FindFirst("sid")!.Value));
}
public sealed class TicketAssignmentProbeRequest : be.Contracts.Requests.VersionedAccountRequest
{
    public Guid TeamId { get; init; }
    public Guid? AgentId { get; init; }
}
