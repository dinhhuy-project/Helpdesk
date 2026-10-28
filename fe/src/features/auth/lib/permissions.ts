// Wire codes from be/Domain/Constants/PermissionCodes.cs; roles are display metadata, not a bypass.
export const Permission = {
  TicketCreate: 'ticket.create', TicketReadOwn: 'ticket.read.own', TicketReadTeam: 'ticket.read.team',
  TicketReadAll: 'ticket.read.all', TicketAssign: 'ticket.assign', TicketUpdateStatus: 'ticket.update.status',
  TicketConfirm: 'ticket.confirm', TicketReopen: 'ticket.reopen', TicketCancel: 'ticket.cancel',
  TicketCommentPublic: 'ticket.comment.public', TicketCommentInternal: 'ticket.comment.internal',
  TicketManageSla: 'ticket.manage.sla', UserManage: 'user.manage', RoleManage: 'role.manage',
  TeamManage: 'team.manage', DepartmentManage: 'department.manage', CategoryManage: 'category.manage',
} as const
export type PermissionCode = typeof Permission[keyof typeof Permission]
export interface PermissionRule { anyOf?: readonly PermissionCode[]; allOf?: readonly PermissionCode[] }

export function hasPermissions(granted: readonly string[], rule: PermissionRule = {}): boolean {
  if (rule.anyOf && !rule.anyOf.some(code => granted.includes(code))) return false
  if (rule.allOf && (!rule.allOf.length || !rule.allOf.every(code => granted.includes(code)))) return false
  return true
}

export const readTicketRule: PermissionRule = {
  anyOf: [Permission.TicketReadOwn, Permission.TicketReadTeam, Permission.TicketReadAll],
}
