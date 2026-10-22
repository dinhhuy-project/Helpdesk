using be.Domain.Constants;

namespace be.Infrastructure.Identity;

internal static class IdentitySeedCatalog
{
    internal static readonly string[] Permissions =
    [
        PermissionCodes.TicketCreate, PermissionCodes.TicketReadOwn, PermissionCodes.TicketReadTeam,
        PermissionCodes.TicketReadAll, PermissionCodes.TicketAssign, PermissionCodes.TicketUpdateStatus,
        PermissionCodes.TicketCommentPublic, PermissionCodes.TicketCommentInternal,
        PermissionCodes.TicketManageSla, PermissionCodes.UserManage, PermissionCodes.TeamManage
    ];

    private static readonly string[] RequesterPermissions =
        [PermissionCodes.TicketCreate, PermissionCodes.TicketReadOwn, PermissionCodes.TicketCommentPublic];
    private static readonly string[] AgentPermissions =
        [.. RequesterPermissions, PermissionCodes.TicketReadTeam, PermissionCodes.TicketUpdateStatus, PermissionCodes.TicketCommentInternal];

    internal static readonly IReadOnlyDictionary<string, string[]> Roles = new Dictionary<string, string[]>
    {
        [RoleNames.Requester] = RequesterPermissions,
        [RoleNames.SupportAgent] = AgentPermissions,
        [RoleNames.TeamLeader] = [.. AgentPermissions, PermissionCodes.TicketAssign],
        [RoleNames.SystemAdmin] = Permissions
    };
}
