using be.Domain.Constants;

namespace be.Infrastructure.Identity;

internal static class IdentitySeedCatalog
{
    internal static readonly string[] Permissions =
    [
        PermissionCodes.TicketCreate, PermissionCodes.TicketReadOwn, PermissionCodes.TicketReadTeam,
        PermissionCodes.TicketReadAll, PermissionCodes.TicketAssign, PermissionCodes.TicketUpdateStatus,
        PermissionCodes.TicketConfirm, PermissionCodes.TicketReopen, PermissionCodes.TicketCancel,
        PermissionCodes.TicketCommentPublic, PermissionCodes.TicketCommentInternal,
        PermissionCodes.TicketManageSla, PermissionCodes.UserManage, PermissionCodes.RoleManage, PermissionCodes.TeamManage,
        PermissionCodes.DepartmentManage, PermissionCodes.CategoryManage,
        PermissionCodes.KnowledgeManage, PermissionCodes.KnowledgePublish, PermissionCodes.IncidentManage, PermissionCodes.IncidentPublish
    ];

    private static readonly string[] RequesterPermissions =
        [PermissionCodes.TicketCreate, PermissionCodes.TicketReadOwn, PermissionCodes.TicketCommentPublic,
         PermissionCodes.TicketConfirm, PermissionCodes.TicketReopen, PermissionCodes.TicketCancel];
    private static readonly string[] AgentPermissions =
        [.. RequesterPermissions, PermissionCodes.TicketReadTeam, PermissionCodes.TicketUpdateStatus, PermissionCodes.TicketCommentInternal];

    internal static readonly IReadOnlyDictionary<string, string[]> Roles = new Dictionary<string, string[]>
    {
        [RoleNames.Requester] = RequesterPermissions,
        [RoleNames.SupportAgent] = AgentPermissions,
        [RoleNames.TeamLeader] = [.. AgentPermissions, PermissionCodes.TicketAssign, PermissionCodes.TeamManage],
        [RoleNames.SystemAdmin] = Permissions
    };
}
