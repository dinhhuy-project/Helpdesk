const roles: Record<string, string> = {
  Requester: 'Người yêu cầu', SupportAgent: 'Nhân viên hỗ trợ',
  TeamLeader: 'Trưởng nhóm hỗ trợ', SystemAdmin: 'Quản trị hệ thống',
}
const permissions: Record<string, string> = {
  'ticket.create': 'Tạo yêu cầu hỗ trợ',
  'ticket.read.own': 'Xem yêu cầu của mình',
  'ticket.read.team': 'Xem yêu cầu trong nhóm',
  'ticket.read.all': 'Xem tất cả yêu cầu',
  'ticket.assign': 'Phân công xử lý yêu cầu',
  'ticket.update.status': 'Cập nhật trạng thái xử lý',
  'ticket.confirm': 'Xác nhận hoàn tất yêu cầu',
  'ticket.reopen': 'Mở lại yêu cầu',
  'ticket.cancel': 'Hủy yêu cầu',
  'ticket.comment.public': 'Bình luận công khai',
  'ticket.comment.internal': 'Trao đổi nội bộ',
  'ticket.manage.sla': 'Quản lý chính sách SLA',
  'user.manage': 'Quản lý tài khoản',
  'role.manage': 'Quản lý vai trò và cấp quyền',
  'team.manage': 'Quản lý nhóm hỗ trợ',
  'department.manage': 'Quản lý phòng ban',
  'category.manage': 'Quản lý danh mục yêu cầu',
}

export const roleLabel = (value: string) => Object.hasOwn(roles, value) ? roles[value] : value
export const permissionLabel = (value: string) => Object.hasOwn(permissions, value) ? permissions[value] : value
