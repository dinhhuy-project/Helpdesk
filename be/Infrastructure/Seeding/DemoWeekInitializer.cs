using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Domain.Enums;
using be.Domain.Rules;
using be.Infrastructure.Identity;
using be.Infrastructure.Notifications;
using be.Infrastructure.Persistence;
using be.Infrastructure.Sla;
using be.Infrastructure.Tickets;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Seeding;

// An explicitly invoked, additive historical simulation, independent of the empty-database startup seed.
public sealed class DemoWeekInitializer(HelpdeskDbContext database, IPasswordHasher<User> passwords, TimeProvider clock)
{
    public static Guid MarkerId => Id("completed");

    public async Task<bool> SeedAsync(string initialPassword, CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
        if ((await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            throw new IdentityInitializationException("Apply reviewed migrations before seeding the demo week.");
        var marker = await database.IdentityAuditLogs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == MarkerId, cancellationToken);
        if (marker is not null)
        {
            if (marker.Action != "DemoWeekSeeded") throw new IdentityInitializationException("The demo marker ID is already used by a different audit record.");
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        if (initialPassword.Length is < 24 or > 128)
            throw new IdentityInitializationException("Supply a randomly generated demo password between 24 and 128 characters.");

        var roles = await ValidateCatalogAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        // Seven local calendar dates, including today's partial date. All persisted timestamps remain UTC.
        var localToday = now.AddHours(7).Date;
        var firstDay = DateTime.SpecifyKind(localToday.AddDays(-6).AddHours(-7), DateTimeKind.Utc);
        var organizationAt = firstDay.AddDays(-30);
        var departments = new[] { "Công nghệ thông tin", "Nhân sự", "Hành chính", "Tài chính kế toán", "Kinh doanh", "Vận hành" }
            .Select((name, i) => new Department { Id = Id($"department:{i}"), Name = DemoWeekCatalog.Prefix + name,
                Description = "Đơn vị mô phỏng của bộ dữ liệu demo tuần; không phải dữ liệu nhân sự thật.", CreatedAt = organizationAt }).ToArray();
        var teams = new[] { "IT Service Desk", "HR Operations", "Workplace Services" }
            .Select((name, i) => new SupportTeam { Id = Id($"team:{i}"), DepartmentId = departments[i].Id,
                Name = DemoWeekCatalog.Prefix + name, CreatedAt = organizationAt }).ToArray();
        var users = DemoWeekCatalog.Accounts.ToDictionary(account => account.Login, account =>
        {
            var user = new User { Id = Id($"user:{account.Login}"), Email = account.Email,
                DisplayName = account.DisplayName + " (Demo)", DepartmentId = departments[account.Department].Id, CreatedAt = organizationAt };
            user.PasswordHash = passwords.HashPassword(user, initialPassword);
            return user;
        });
        database.Departments.AddRange(departments);
        database.SupportTeams.AddRange(teams);
        database.Users.AddRange(users.Values);
        foreach (var account in DemoWeekCatalog.Accounts)
        {
            database.UserRoles.Add(new UserRole { UserId = users[account.Login].Id, RoleId = roles[account.Role].Id });
            if (account.Team is { } team)
                database.SupportTeamMembers.Add(new SupportTeamMember { SupportTeamId = teams[team].Id, UserId = users[account.Login].Id,
                    IsLeader = account.Role == RoleNames.TeamLeader, JoinedAt = organizationAt });
        }
        // Exercise multiple roles without widening this agent's effective grants.
        database.UserRoles.Add(new UserRole { UserId = users["agent.it.1"].Id, RoleId = roles[RoleNames.Requester].Id });
        var roots = new[] { "Hỗ trợ CNTT", "Dịch vụ nhân sự", "Dịch vụ văn phòng" }
            .Select((name, i) => new TicketCategory { Id = Id($"category-root:{i}"), Name = DemoWeekCatalog.Prefix + name, CreatedAt = organizationAt }).ToArray();
        var categoryNames = new[] { "Mạng và VPN", "Tài khoản và email", "Thiết bị máy tính", "Ứng dụng nghiệp vụ",
            "Chấm công", "Nghỉ phép", "Hồ sơ nhân sự", "Chế độ phúc lợi",
            "Máy in", "Phòng họp", "Thẻ ra vào", "Thiết bị văn phòng" };
        var categories = categoryNames.Select((name, i) => new TicketCategory { Id = Id($"category:{i}"), Name = name,
            ParentId = roots[i / 4].Id, CreatedAt = organizationAt }).ToArray();
        database.TicketCategories.AddRange(roots);
        database.TicketCategories.AddRange(categories);
        var priorities = Enum.GetValues<TicketPriority>();
        int[] responseMinutes = [240, 120, 30, 15];
        int[] resolutionMinutes = [1440, 480, 180, 60];
        var policies = priorities.Select((priority, i) => new SlaPolicy { Id = Id($"policy:{priority}"),
            Name = $"{DemoWeekCatalog.Prefix}SLA 24/7 - {priority}", Priority = priority,
            FirstResponseMinutes = responseMinutes[i], ResolutionMinutes = resolutionMinutes[i], CreatedAt = organizationAt }).ToArray();
        database.SlaPolicies.AddRange(policies);

        int[] counts = [12, 16, 24, 28, 26, 22, 16];
        TicketPriority[] priorityMix = [TicketPriority.Normal, TicketPriority.Normal, TicketPriority.High, TicketPriority.Normal,
            TicketPriority.Low, TicketPriority.Urgent, TicketPriority.Normal, TicketPriority.High, TicketPriority.Low, TicketPriority.Normal];
        var tickets = new List<(Ticket Ticket, int Scenario, int Category, TicketSla? Sla)>();
        var sequence = 0;
        for (var day = 0; day < counts.Length; day++)
        {
            var start = firstDay.AddDays(day);
            var earliest = start.AddHours(8).AddMinutes(5);
            var latest = start.AddHours(17).AddMinutes(40);
            if (latest > now.AddMinutes(-1)) latest = now.AddMinutes(-1);
            if (earliest > latest) earliest = start;
            // Even a run just after midnight must not invent future historical events.
            if (latest < earliest) latest = earliest;
            for (var j = 0; j < counts[day]; j++, sequence++)
            {
                var category = (j * 7 + day * 3) % categories.Length;
                var requester = users[$"requester.{(sequence * 5 + day) % 18 + 1:00}"];
                var createdAt = earliest.AddTicks((latest - earliest).Ticks * j / Math.Max(1, counts[day] - 1));
                createdAt = new DateTime(createdAt.Ticks - createdAt.Ticks % 10, DateTimeKind.Utc);
                var ticket = new Ticket { Id = Id($"ticket:{sequence}"), Title = DemoWeekCatalog.Prefix + Titles[category] + $" - {Locations[(j + day) % Locations.Length]}",
                    Description = $"{Descriptions[category]}\nKhu vực: {Locations[(j + day) % Locations.Length]}. " +
                        $"Ảnh hưởng đến {(j % 5) + 1} nhân viên; mong được kiểm tra và phản hồi hướng xử lý.\nDữ liệu mô phỏng vận hành, mã tình huống {sequence + 1:000}.",
                    RequesterId = requester.Id, CreatedByUserId = requester.Id, CategoryId = categories[category].Id,
                    Priority = priorityMix[(j + day * 3) % priorityMix.Length], CreatedAt = createdAt };
                var policy = policies.Single(x => x.Priority == ticket.Priority);
                // Imported/legacy Tickets intentionally illustrate the no-SLA state.
                var sla = sequence % 9 == 0 ? null : SlaRules.Snapshot(ticket, policy);
                if (sla is not null) database.TicketSlas.Add(sla);
                tickets.Add((ticket, (j + day * 5) % 12, category, sla));
            }
        }
        // Historical policies are retired; live policy selection and existing deadlines are untouched.
        foreach (var policy in policies) { policy.IsActive = false; policy.UpdatedAt = now; }
        database.Tickets.AddRange(tickets.Select(x => x.Ticket));
        await database.SaveChangesAsync(cancellationToken); // PostgreSQL generates TicketNumber.

        foreach (var (ticket, scenario, category, sla) in tickets)
            BuildHistory(ticket, scenario, category / 4, sla, users, teams, now);
        await database.SaveChangesAsync(cancellationToken);
        await AddNotificationsAsync(tickets.Select(x => x.Ticket.Id).ToArray(), users.Values.Select(x => x.Id).ToArray(), now, cancellationToken);
        database.IdentityAuditLogs.Add(new IdentityAuditLog { Id = MarkerId, ActorId = null, Action = "DemoWeekSeeded", CreatedAt = now,
            Data = JsonSerializer.Serialize(new { Batch = DemoWeekCatalog.Batch, AsOf = now, CreatedFrom = firstDay, TimeZone = "UTC+07:00",
                Accounts = users.Count, Tickets = tickets.Count, Departments = departments.Length, Teams = teams.Length,
                Categories = roots.Length + categories.Length, Synthetic = true, HistoricalNotificationsAlreadyDispatched = true }) });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<Dictionary<string, Role>> ValidateCatalogAsync(CancellationToken cancellationToken)
    {
        var emails = DemoWeekCatalog.Accounts.Select(x => x.Email).ToArray();
        if (await database.Users.AnyAsync(x => emails.Contains(x.NormalizedEmail), cancellationToken) ||
            await database.Departments.AnyAsync(x => x.Name.StartsWith(DemoWeekCatalog.Prefix), cancellationToken) ||
            await database.TicketCategories.AnyAsync(x => x.Name.StartsWith(DemoWeekCatalog.Prefix), cancellationToken) ||
            await database.SupportTeams.AnyAsync(x => x.Name.StartsWith(DemoWeekCatalog.Prefix), cancellationToken) ||
            await database.SlaPolicies.AnyAsync(x => x.Name.StartsWith(DemoWeekCatalog.Prefix), cancellationToken))
            throw new IdentityInitializationException("Reserved demo names already exist without a completion marker. No records were changed; inspect the collision before retrying.");
        var roles = await database.Roles.ToDictionaryAsync(x => x.Name, cancellationToken);
        foreach (var (name, expected) in IdentitySeedCatalog.Roles)
        {
            if (!roles.TryGetValue(name, out var role)) throw new IdentityInitializationException("Initialize the default Identity catalog before seeding the demo week.");
            var actual = await (from grant in database.RolePermissions join permission in database.Permissions on grant.PermissionId equals permission.Id
                where grant.RoleId == role.Id select permission.Code).ToArrayAsync(cancellationToken);
            if (!actual.Order().SequenceEqual(expected.Order()))
                throw new IdentityInitializationException("Default role grants differ from the documented demo matrix. Existing grants were preserved; review them before using this dataset.");
        }
        var custom = new Dictionary<string, string[]> { [DemoWeekCatalog.AccountManagerRole] = [PermissionCodes.UserManage],
            [DemoWeekCatalog.ReportViewerRole] = [PermissionCodes.TicketReadAll] };
        foreach (var (name, codes) in custom)
        {
            if (roles.ContainsKey(name)) throw new IdentityInitializationException("A reserved demo role already exists without a completion marker.");
            var role = new Role { Id = Id($"role:{name}"), Name = name, Description = "Role dành riêng cho bộ dữ liệu mô phỏng tuần." };
            roles.Add(name, role);
            database.Roles.Add(role);
            foreach (var code in codes)
            {
                var permissionId = await database.Permissions.Where(x => x.Code == code).Select(x => x.Id).SingleAsync(cancellationToken);
                database.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionId });
            }
        }
        return roles;
    }

    private void BuildHistory(Ticket ticket, int scenario, int team, TicketSla? sla, Dictionary<string, User> users, SupportTeam[] teams, DateTime now)
    {
        var tracker = new TicketSlaTracker(database);
        Activity(ticket, TicketActivityActions.Created, ticket.RequesterId, ticket.CreatedAt, null, JsonSerializer.Serialize(new
        { ticket.TicketNumber, ticket.RequesterId, ticket.CreatedByUserId, ticket.CategoryId, Status = TicketStatus.New.ToString(), Priority = ticket.Priority.ToString() }));
        var agents = new[] { "agent.it", "agent.hr", "agent.office" };
        var leaders = new[] { "lead.it", "lead.hr", "lead.office" };
        var agent = users[$"{agents[team]}.{ticket.TicketNumber % 2 + 1}"].Id;
        var events = new List<(DateTime At, Action<DateTime> Execute)>();
        void At(double minutes, Action<DateTime> action) => events.Add((ticket.CreatedAt.AddMinutes(minutes), action));
        void Detect(DateTime at) { if (sla is not null) tracker.Detect(ticket, sla, at); }
        void Transition(TicketStatus target, Guid actor, DateTime at, string? summary = null)
        {
            Detect(at); // Persist breach evidence before terminal states suppress new detection.
            var previous = State(ticket);
            TicketWorkflow.Apply(ticket, target, summary, at);
            if (sla is not null) { sla.ResolvedAt = ticket.ResolvedAt; tracker.Detect(ticket, sla, at, actor); }
            Activity(ticket, TicketActivityActions.StatusChanged, actor, at, previous, State(ticket));
        }
        void Comment(Guid author, bool internalNote, string content, DateTime at)
        {
            var comment = new TicketComment { Id = Id($"comment:{ticket.Id}:{at.Ticks}:{author}"), TicketId = ticket.Id, AuthorId = author,
                Content = content, IsInternal = internalNote, CreatedAt = at };
            database.TicketComments.Add(comment);
            if (!internalNote && author == agent && sla is not null) { sla.FirstRespondedAt ??= at; tracker.Detect(ticket, sla, at, author); }
            Activity(ticket, internalNote ? TicketActivityActions.InternalNoteAdded : TicketActivityActions.PublicCommentAdded, author, at, null,
                JsonSerializer.Serialize(new { CommentId = comment.Id, comment.IsInternal }));
        }
        if (sla is not null)
        {
            At(sla.FirstResponseMinutes!.Value + 1, Detect);
            At(sla.ResolutionMinutes!.Value + 1, Detect);
        }
        if (scenario != 0)
            At(3, at =>
            {
                var previous = JsonSerializer.Serialize(new { ticket.AssignedTeamId, ticket.AssignedAgentId });
                ticket.AssignedTeamId = teams[team].Id;
                ticket.AssignedAgentId = scenario == 1 ? null : agent;
                ticket.UpdatedAt = at;
                // Only read-all administrators can initially route an unassigned Ticket.
                Activity(ticket, TicketActivityActions.Assigned, users["admin"].Id, at, previous,
                    JsonSerializer.Serialize(new { ticket.AssignedTeamId, ticket.AssignedAgentId }));
            });
        if (scenario >= 2)
        {
            At(5, at => Transition(TicketStatus.InProgress, agent, at));
            At(8, at => Comment(users[leaders[team]].Id, true, "Đã kiểm tra nhật ký và đối chiếu cấu hình. Cần phối hợp bộ phận liên quan trước khi xác nhận kết quả. Ghi chú nội bộ mô phỏng.", at));
            var response = scenario is 3 or 7 or 8 ? (sla?.FirstResponseMinutes ?? 120) * 1.5 : 12;
            if (scenario is not (3 or 10))
            {
                At(response, at => Comment(agent, false, "Chào bạn, nhóm đã tiếp nhận và kiểm tra yêu cầu. Vui lòng giữ nguyên hiện trạng để chúng tôi xác minh; sẽ cập nhật kết quả xử lý tại đây.", at));
                At(response + 15, at => Comment(ticket.RequesterId, false, "Mình đã bổ sung thông tin và kiểm tra lại, tình trạng vẫn còn. Nhờ nhóm tiếp tục hỗ trợ, cảm ơn.", at));
            }
            if (scenario is 4 or 11)
                At(response + 20, at => Transition(TicketStatus.WaitingForRequester, agent, at));
            if (scenario is 5 or 6 or 7 or 8 or 9)
            {
                var resolve = Math.Max(response + 45, (sla?.ResolutionMinutes ?? 240) * (scenario is 7 or 8 ? 1.3 : 0.45));
                At(resolve - 2, at => Comment(agent, false, "Đã hoàn tất điều chỉnh và kiểm tra lại với người dùng. Bạn vui lòng xác nhận kết quả; nếu lỗi tái diễn, hãy phản hồi trên Ticket này.", at));
                At(resolve, at => Transition(TicketStatus.Resolved, agent, at,
                    "Đã xác minh nguyên nhân, cập nhật cấu hình/dữ liệu liên quan và kiểm thử lại. Người dùng đã nhận hướng dẫn để kiểm tra kết quả."));
                if (scenario is 6 or 8)
                {
                    At(resolve + 40, at => Comment(ticket.RequesterId, false, "Mình đã kiểm tra và sử dụng bình thường. Đồng ý xác nhận hoàn tất yêu cầu.", at));
                    At(resolve + 45, at => Transition(TicketStatus.Closed, ticket.RequesterId, at));
                }
                if (scenario == 9)
                {
                    At(resolve + 85, at => Comment(ticket.RequesterId, false, "Lỗi tái diễn khi làm lại thao tác trong ca chiều, mình xin mở lại để nhóm kiểm tra thêm.", at));
                    At(resolve + 90, at => Transition(TicketStatus.Reopened, ticket.RequesterId, at));
                }
            }
            if (scenario == 10)
            {
                At(23, at => Comment(ticket.RequesterId, false, "Yêu cầu bị gửi trùng; bộ phận đã xử lý theo phiếu khác. Mình xin hủy phiếu này.", at));
                At(25, at => Transition(TicketStatus.Cancelled, ticket.RequesterId, at));
            }
        }
        foreach (var item in events.OrderBy(x => x.At)) if (item.At <= now) item.Execute(item.At);
        Detect(now);
    }

    private async Task AddNotificationsAsync(Guid[] ticketIds, Guid[] userIds, DateTime now, CancellationToken cancellationToken)
    {
        var readers = await new TicketScope(database, clock).Readers().Where(x => ticketIds.Contains(x.TicketId) && userIds.Contains(x.UserId))
            .ToListAsync(cancellationToken);
        var activities = await database.TicketActivities.AsNoTracking().Where(x => ticketIds.Contains(x.TicketId) && x.Action != TicketActivityActions.Created)
            .ToListAsync(cancellationToken);
        var index = 0;
        // Representative persisted history only. No backlog is sent to existing accounts or live SignalR clients.
        foreach (var group in activities.GroupBy(x => x.TicketId))
        foreach (var activity in group.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(3))
        foreach (var reader in readers.Where(x => x.TicketId == activity.TicketId && x.UserId != activity.ActorId &&
            (!NotificationKinds.Internal.Contains(activity.Action) || x.CanReadInternal)))
            database.Notifications.Add(new Notification { Id = Id($"notification:{activity.Id}:{reader.UserId}"), UserId = reader.UserId,
                TicketId = activity.TicketId, TicketActivityId = activity.Id, Title = NotificationKinds.Title(activity.Action),
                Content = "Cập nhật lịch sử mô phỏng. Mở Ticket để xem nội dung trong phạm vi được phép.", CreatedAt = activity.CreatedAt,
                ReadAt = index++ % 3 == 0 ? null : new DateTime(Math.Min(activity.CreatedAt.AddMinutes(30).Ticks, now.Ticks), DateTimeKind.Utc),
                DispatchedAt = activity.CreatedAt });
    }

    private void Activity(Ticket ticket, string action, Guid actor, DateTime at, string? oldValue, string newValue) =>
        database.TicketActivities.Add(new TicketActivity { TicketId = ticket.Id, ActorId = actor, Action = action,
            CreatedAt = at, OldValue = oldValue, NewValue = newValue });
    private static string State(Ticket ticket) => JsonSerializer.Serialize(new
    { Status = ticket.Status.ToString(), ticket.ResolutionSummary, ticket.ResolvedAt, ticket.ClosedAt });
    private static Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{DemoWeekCatalog.Batch}:{key}"))[..16]);

    private static readonly string[] Locations = ["Trụ sở Hà Nội - tầng 2", "Chi nhánh Đà Nẵng", "Văn phòng TP.HCM", "Kho vận Bình Dương", "Trụ sở Hà Nội - tầng 4"];
    private static readonly string[] Titles = ["VPN ngắt kết nối khi truy cập hệ thống nội bộ", "Không đồng bộ được email trên máy tính mới",
        "Laptop khởi động chậm và treo khi họp trực tuyến", "Không xuất được báo cáo từ hệ thống ERP", "Thiếu lượt chấm công ca sáng",
        "Đơn nghỉ phép chưa cập nhật người phê duyệt", "Cần xác nhận thông tin hồ sơ lao động", "Chưa hiển thị quyền lợi bảo hiểm bổ sung",
        "Máy in báo kẹt giấy khi in chứng từ", "Thiết bị trình chiếu phòng họp không nhận tín hiệu", "Thẻ nhân viên không mở được cửa", "Điều hòa khu làm việc không ổn định"];
    private static readonly string[] Descriptions = ["Kết nối VPN thường ngắt sau vài phút, không truy cập được thư mục dùng chung.",
        "Hộp thư hoạt động trên web nhưng ứng dụng máy tính chưa tải được thư mới.", "Máy tính có dấu hiệu chậm sau lần cập nhật gần nhất, ảnh hưởng cuộc họp với đối tác.",
        "Thao tác xuất báo cáo kết thúc nhưng không tải được tệp, đã thử lại trên hai trình duyệt.", "Bảng công chưa ghi nhận giờ vào mặc dù đã quẹt thẻ tại cổng.",
        "Đã gửi đơn nghỉ phép nhưng yêu cầu vẫn ở trạng thái chờ dù quản lý đã xác nhận.", "Cần rà soát thông tin hợp đồng và xác nhận hồ sơ để hoàn thành thủ tục hành chính.",
        "Cổng nhân sự chưa hiển thị quyền lợi của kỳ đăng ký mới.", "Máy in khu vực chung báo kẹt giấy liên tục, ảnh hưởng việc in chứng từ cuối kỳ.",
        "Cáp đã cắm đúng nhưng màn hình phòng họp chưa nhận tín hiệu từ laptop.", "Thẻ vẫn chấm công được nhưng đầu đọc cửa khu làm việc từ chối truy cập.",
        "Nhiệt độ khu làm việc không ổn định, cần kiểm tra lịch bảo trì và bộ điều khiển."];
}
