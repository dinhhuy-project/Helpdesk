namespace be.Domain.Constants;

public static class PermissionCodes
{
    public const string TicketCreate = "ticket.create";
    public const string TicketReadOwn = "ticket.read.own";
    public const string TicketReadTeam = "ticket.read.team";
    public const string TicketReadAll = "ticket.read.all";
    public const string TicketAssign = "ticket.assign";
    public const string TicketUpdateStatus = "ticket.update.status";
    public const string TicketConfirm = "ticket.confirm";
    public const string TicketReopen = "ticket.reopen";
    public const string TicketCancel = "ticket.cancel";
    public const string TicketCommentPublic = "ticket.comment.public";
    public const string TicketCommentInternal = "ticket.comment.internal";
    public const string TicketManageSla = "ticket.manage.sla";
    public const string UserManage = "user.manage";
    public const string RoleManage = "role.manage";
    public const string TeamManage = "team.manage";
    public const string DepartmentManage = "department.manage";
    public const string CategoryManage = "category.manage";
    public const string KnowledgeManage = "knowledge.manage";
    public const string KnowledgePublish = "knowledge.publish";
    public const string IncidentManage = "incident.manage";
    public const string IncidentPublish = "incident.publish";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        TicketCreate, TicketReadOwn, TicketReadTeam, TicketReadAll, TicketAssign, TicketUpdateStatus, TicketConfirm, TicketReopen, TicketCancel,
        TicketCommentPublic, TicketCommentInternal, TicketManageSla, UserManage, RoleManage, TeamManage, DepartmentManage, CategoryManage,
        KnowledgeManage, KnowledgePublish, IncidentManage, IncidentPublish
    });
}
