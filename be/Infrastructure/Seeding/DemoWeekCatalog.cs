using be.Domain.Constants;

namespace be.Infrastructure.Seeding;

// Explicitly synthetic identities; never reuse an employee's real address.
public static class DemoWeekCatalog
{
    public const string Batch = "helpdesk-demo-week-v1";
    public const string Domain = "demo.helpdesk.example";
    public const string AccountManagerRole = "DemoAccountManager";
    public const string ReportViewerRole = "DemoReportViewer";
    public const string Prefix = "[Demo tuần] ";
    public static IReadOnlyList<DemoAccount> Accounts { get; } =
    [
        new("admin", "Nguyễn Minh Quân", RoleNames.SystemAdmin, 0),
        new("lead.it", "Trần Hoàng Nam", RoleNames.TeamLeader, 0, 0),
        new("lead.hr", "Lê Thu Hà", RoleNames.TeamLeader, 1, 1),
        new("lead.office", "Phạm Ngọc Anh", RoleNames.TeamLeader, 2, 2),
        new("agent.it.1", "Đỗ Đức Huy", RoleNames.SupportAgent, 0, 0),
        new("agent.it.2", "Vũ Minh Đức", RoleNames.SupportAgent, 0, 0),
        new("agent.hr.1", "Nguyễn Thảo Linh", RoleNames.SupportAgent, 1, 1),
        new("agent.hr.2", "Trần Mỹ Duyên", RoleNames.SupportAgent, 1, 1),
        new("agent.office.1", "Lê Quốc Bảo", RoleNames.SupportAgent, 2, 2),
        new("agent.office.2", "Phan Thanh Tùng", RoleNames.SupportAgent, 2, 2),
        new("accounts", "Bùi Khánh Vy", AccountManagerRole, 1),
        new("reports", "Hoàng Gia Hân", ReportViewerRole, 3),
        new("requester.01", "Nguyễn Mai Chi", RoleNames.Requester, 3),
        new("requester.02", "Trần Tuấn Kiệt", RoleNames.Requester, 4),
        new("requester.03", "Lê Minh Khang", RoleNames.Requester, 5),
        new("requester.04", "Phạm Phương Thảo", RoleNames.Requester, 3),
        new("requester.05", "Vũ Hải Yến", RoleNames.Requester, 4),
        new("requester.06", "Đỗ Quang Huy", RoleNames.Requester, 5),
        new("requester.07", "Bùi Ngọc Trâm", RoleNames.Requester, 3),
        new("requester.08", "Phan Đức Anh", RoleNames.Requester, 4),
        new("requester.09", "Hoàng Minh Tâm", RoleNames.Requester, 5),
        new("requester.10", "Nguyễn Thanh Hà", RoleNames.Requester, 3),
        new("requester.11", "Trần Bảo Ngọc", RoleNames.Requester, 4),
        new("requester.12", "Lê Anh Dũng", RoleNames.Requester, 5),
        new("requester.13", "Phạm Tú Anh", RoleNames.Requester, 3),
        new("requester.14", "Vũ Nhật Minh", RoleNames.Requester, 4),
        new("requester.15", "Đỗ Hồng Nhung", RoleNames.Requester, 5),
        new("requester.16", "Bùi Thành Đạt", RoleNames.Requester, 3),
        new("requester.17", "Phan Hải Đăng", RoleNames.Requester, 4),
        new("requester.18", "Hoàng Thu Trang", RoleNames.Requester, 5)
    ];
}

public sealed record DemoAccount(string Login, string DisplayName, string Role, int Department, int? Team = null)
{
    public string Email => $"{Login}@{DemoWeekCatalog.Domain}";
}
