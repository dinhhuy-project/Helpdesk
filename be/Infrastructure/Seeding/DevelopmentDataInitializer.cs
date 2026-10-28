using System.Text.Json;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Domain.Rules;
using be.Infrastructure.Identity;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Seeding;

public sealed class DevelopmentDataInitializer(HelpdeskDbContext database, IHostEnvironment environment, TimeProvider clock)
{
    public const string ConfigurationKey = "DevelopmentData:SeedOnStartup";

    // A complete, one-time bundle. Never fill gaps by rewriting a partially edited workspace.
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
            throw new IdentityInitializationException("Development sample data is supported only in Development.");

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
        if ((await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            throw new IdentityInitializationException("Apply reviewed migrations before seeding development data.");
        if (await database.Departments.AnyAsync(cancellationToken) || await database.SupportTeams.AnyAsync(cancellationToken) ||
            await database.TicketCategories.AnyAsync(cancellationToken) || await database.Tickets.AnyAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var adminId = await (from user in database.Users
                             join membership in database.UserRoles on user.Id equals membership.UserId
                             join role in database.Roles on membership.RoleId equals role.Id
                             where user.IsActive && role.Name == RoleNames.SystemAdmin
                             orderby user.CreatedAt, user.Id
                             select (Guid?)user.Id).FirstOrDefaultAsync(cancellationToken)
            ?? throw new IdentityInitializationException("Bootstrap an active SystemAdmin before seeding development data.");

        // The assigned sample agent must satisfy the same permission/membership rules as real assignments.
        var grants = await (from membership in database.UserRoles
                            join grant in database.RolePermissions on membership.RoleId equals grant.RoleId
                            join permission in database.Permissions on grant.PermissionId equals permission.Id
                            where membership.UserId == adminId select permission.Code).ToListAsync(cancellationToken);
        if (!grants.Contains(PermissionCodes.TicketUpdateStatus) || !grants.Contains(PermissionCodes.TicketReadAll))
            throw new IdentityInitializationException("The sample administrator needs ticket.update.status and ticket.read.all; existing grants were preserved.");

        var now = clock.GetUtcNow().UtcDateTime;
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var createdAt = now.AddHours(-2);
        Department[] departments =
        [
            new() { Name = "[Mẫu] Công nghệ thông tin", Description = "Phòng ban mẫu cho môi trường Development.", CreatedAt = createdAt },
            new() { Name = "[Mẫu] Nhân sự", Description = "Phòng ban mẫu cho môi trường Development.", CreatedAt = createdAt },
            new() { Name = "[Mẫu] Hành chính", Description = "Phòng ban mẫu cho môi trường Development.", CreatedAt = createdAt }
        ];
        SupportTeam[] teams =
        [
            new() { DepartmentId = departments[0].Id, Name = "[Mẫu] IT Helpdesk", CreatedAt = createdAt },
            new() { DepartmentId = departments[1].Id, Name = "[Mẫu] Hỗ trợ nhân sự", CreatedAt = createdAt },
            new() { DepartmentId = departments[2].Id, Name = "[Mẫu] Hỗ trợ văn phòng", CreatedAt = createdAt }
        ];
        TicketCategory[] roots =
        [
            new() { Name = "[Mẫu] Công nghệ thông tin", CreatedAt = createdAt },
            new() { Name = "[Mẫu] Nhân sự", CreatedAt = createdAt },
            new() { Name = "[Mẫu] Hành chính", CreatedAt = createdAt }
        ];
        TicketCategory[] children =
        [
            new() { ParentId = roots[0].Id, Name = "Mạng và VPN", CreatedAt = createdAt },
            new() { ParentId = roots[0].Id, Name = "Tài khoản và email", CreatedAt = createdAt },
            new() { ParentId = roots[1].Id, Name = "Chấm công", CreatedAt = createdAt },
            new() { ParentId = roots[2].Id, Name = "Thiết bị văn phòng", CreatedAt = createdAt }
        ];
        database.Departments.AddRange(departments);
        database.SupportTeams.AddRange(teams);
        database.SupportTeamMembers.AddRange(teams.Select(team => new SupportTeamMember
        {
            SupportTeamId = team.Id, UserId = adminId, IsLeader = true, JoinedAt = createdAt
        }));
        database.TicketCategories.AddRange(roots);
        database.TicketCategories.AddRange(children);

        (string Title, string Description, int Category, int Team, TicketPriority Priority, TicketStatus Status)[] examples =
        [
            ("[Mẫu] Không kết nối được Wi-Fi", "Dữ liệu mẫu: kiểm tra kết nối Wi-Fi tại khu vực làm việc.", 0, 0, TicketPriority.High, TicketStatus.New),
            ("[Mẫu] Cần hỗ trợ cấu hình email", "Dữ liệu mẫu: hướng dẫn thiết lập hộp thư trên máy tính.", 1, 0, TicketPriority.Normal, TicketStatus.InProgress),
            ("[Mẫu] Máy in cần kiểm tra", "Dữ liệu mẫu: chờ bổ sung thông tin lỗi máy in.", 3, 2, TicketPriority.Low, TicketStatus.WaitingForRequester),
            ("[Mẫu] Đã xử lý kết nối VPN", "Dữ liệu mẫu: kiểm tra lại kết nối VPN sau khi cấu hình.", 0, 0, TicketPriority.High, TicketStatus.Resolved),
            ("[Mẫu] Đã hướng dẫn tra cứu chấm công", "Dữ liệu mẫu: hướng dẫn tra cứu dữ liệu chấm công.", 2, 1, TicketPriority.Normal, TicketStatus.Closed)
        ];
        var tickets = examples.Select((example, index) => new Ticket
        {
            Title = example.Title, Description = example.Description, CategoryId = children[example.Category].Id,
            RequesterId = adminId, CreatedByUserId = adminId, Priority = example.Priority,
            CreatedAt = createdAt.AddMinutes(index * 10)
        }).ToArray();
        database.Tickets.AddRange(tickets);
        // TicketNumber comes from PostgreSQL. Both saves and every activity share this transaction.
        await database.SaveChangesAsync(cancellationToken);
        for (var index = 0; index < tickets.Length; index++)
        {
            var ticket = tickets[index];
            AddActivity(ticket, TicketActivityActions.Created, ticket.CreatedAt, null, JsonSerializer.Serialize(new
            {
                ticket.TicketNumber, ticket.RequesterId, ticket.CreatedByUserId, ticket.CategoryId,
                Status = TicketStatus.New.ToString(), Priority = ticket.Priority.ToString()
            }));
            if (examples[index].Status == TicketStatus.New) continue;

            var oldAssignment = JsonSerializer.Serialize(new { ticket.AssignedTeamId, ticket.AssignedAgentId });
            ticket.AssignedTeamId = teams[examples[index].Team].Id;
            ticket.AssignedAgentId = adminId;
            ticket.UpdatedAt = ticket.CreatedAt.AddMinutes(1);
            AddActivity(ticket, TicketActivityActions.Assigned, ticket.UpdatedAt.Value, oldAssignment,
                JsonSerializer.Serialize(new { ticket.AssignedTeamId, ticket.AssignedAgentId }));
            Transition(ticket, TicketStatus.InProgress, ticket.CreatedAt.AddMinutes(2));
            if (examples[index].Status == TicketStatus.WaitingForRequester)
                Transition(ticket, TicketStatus.WaitingForRequester, ticket.CreatedAt.AddMinutes(3));
            if (examples[index].Status is TicketStatus.Resolved or TicketStatus.Closed)
                Transition(ticket, TicketStatus.Resolved, ticket.CreatedAt.AddMinutes(3), "Kết quả mẫu: đã hướng dẫn và kiểm tra xử lý thành công.");
            if (examples[index].Status == TicketStatus.Closed)
                Transition(ticket, TicketStatus.Closed, ticket.CreatedAt.AddMinutes(4));
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private void Transition(Ticket ticket, TicketStatus target, DateTime at, string? resolution = null)
    {
        var previous = State(ticket);
        TicketWorkflow.Apply(ticket, target, resolution, at);
        AddActivity(ticket, TicketActivityActions.StatusChanged, at, previous, State(ticket));
    }
    private static string State(Ticket ticket) => JsonSerializer.Serialize(new
    {
        Status = ticket.Status.ToString(), ticket.ResolutionSummary, ticket.ResolvedAt, ticket.ClosedAt
    });
    private void AddActivity(Ticket ticket, string action, DateTime at, string? previous, string current) =>
        database.TicketActivities.Add(new TicketActivity
        {
            TicketId = ticket.Id, ActorId = null, Action = action, CreatedAt = at, OldValue = previous, NewValue = current
        });
}
