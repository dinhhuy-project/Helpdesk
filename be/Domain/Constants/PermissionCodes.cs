namespace be.Domain.Constants;

public static class PermissionCodes
{
    public const string TicketCreate = "ticket.create";
    public const string TicketReadOwn = "ticket.read.own";
    public const string TicketReadTeam = "ticket.read.team";
    public const string TicketReadAll = "ticket.read.all";
    public const string TicketAssign = "ticket.assign";
    public const string TicketUpdateStatus = "ticket.update.status";
    public const string TicketCommentPublic = "ticket.comment.public";
    public const string TicketCommentInternal = "ticket.comment.internal";
    public const string TicketManageSla = "ticket.manage.sla";
    public const string UserManage = "user.manage";
    public const string TeamManage = "team.manage";
}
