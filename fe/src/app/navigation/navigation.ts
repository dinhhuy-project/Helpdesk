import { Permission, hasPermissions, readTicketRule } from '../../features/auth/lib/permissions'
import type { PermissionRule } from '../../features/auth/lib/permissions'

export interface NavigationItem {
  id: string
  label: string
  path: string
  group: 'Cá nhân' | 'Yêu cầu hỗ trợ' | 'Quản trị' | 'Theo dõi'
  rule?: PermissionRule
  available: boolean
}

export const navigation: readonly NavigationItem[] = [
  { id: 'home', label: 'Trang chủ', path: '/', group: 'Cá nhân', available: true },
  { id: 'account', label: 'Tài khoản', path: '/account', group: 'Cá nhân', available: true },
  { id: 'security', label: 'Bảo mật', path: '/account/password', group: 'Cá nhân', available: true },
  { id: 'knowledge', label: 'Tra cứu tri thức', path: '/knowledge', group: 'Yêu cầu hỗ trợ', available: true },
  { id: 'incidents', label: 'Sự cố chung', path: '/incidents', group: 'Yêu cầu hỗ trợ', available: true },
  { id: 'knowledge-admin', label: 'Biên soạn tri thức', path: '/admin/knowledge', group: 'Quản trị', available: true,
    rule: { anyOf: [Permission.KnowledgeManage, Permission.KnowledgePublish] } },
  { id: 'incident-admin', label: 'Quản trị sự cố chung', path: '/admin/incidents', group: 'Quản trị', available: true,
    rule: { anyOf: [Permission.IncidentManage, Permission.IncidentPublish] } },
  { id: 'tickets', label: 'Danh sách yêu cầu', path: '/tickets', group: 'Yêu cầu hỗ trợ', available: true, rule: readTicketRule },
  { id: 'create-ticket', label: 'Tạo yêu cầu', path: '/tickets/new', group: 'Yêu cầu hỗ trợ', available: true,
    rule: { allOf: [Permission.TicketCreate] } },
  { id: 'users', label: 'Tài khoản người dùng', path: '/admin/users', group: 'Quản trị', available: true,
    rule: { allOf: [Permission.UserManage] } },
  { id: 'roles', label: 'Vai trò và quyền', path: '/admin/roles', group: 'Quản trị', available: true,
    rule: { allOf: [Permission.RoleManage] } },
  { id: 'departments', label: 'Phòng ban', path: '/admin/departments', group: 'Quản trị', available: true,
    rule: { allOf: [Permission.DepartmentManage] } },
  { id: 'teams', label: 'Nhóm hỗ trợ', path: '/admin/teams', group: 'Quản trị', available: true,
    rule: { allOf: [Permission.TeamManage] } },
  { id: 'categories', label: 'Danh mục yêu cầu', path: '/admin/categories', group: 'Quản trị', available: true,
    rule: { allOf: [Permission.CategoryManage] } },
  { id: 'sla', label: 'Chính sách SLA', path: '/admin/sla-policies', group: 'Quản trị', available: true,
    rule: { allOf: [Permission.TicketManageSla] } },
  { id: 'notifications', label: 'Thông báo', path: '/notifications', group: 'Theo dõi', available: true },
  { id: 'dashboard', label: 'Tổng quan', path: '/dashboard', group: 'Theo dõi', available: true, rule: readTicketRule },
  { id: 'reports', label: 'Báo cáo', path: '/reports/tickets', group: 'Theo dõi', available: true, rule: readTicketRule },
]

export function visibleNavigation(permissions: readonly string[], verified: boolean) {
  return navigation.filter(item => verified && hasPermissions(permissions, item.rule))
}
