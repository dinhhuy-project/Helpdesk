// Wire values: be/Domain/Enums/TicketStatus.cs and TicketPriority.cs (JSON numbers).
export const TicketStatus = {
  New: 1, InProgress: 2, WaitingForRequester: 3, Resolved: 4, Closed: 5, Cancelled: 6, Reopened: 7,
} as const
export type TicketStatus = typeof TicketStatus[keyof typeof TicketStatus]

export const TicketPriority = { Low: 1, Normal: 2, High: 3, Urgent: 4 } as const
export type TicketPriority = typeof TicketPriority[keyof typeof TicketPriority]

export const ticketStatusLabels: Record<TicketStatus, string> = {
  1: 'Mới', 2: 'Đang xử lý', 3: 'Chờ người yêu cầu', 4: 'Đã giải quyết',
  5: 'Đã đóng', 6: 'Đã hủy', 7: 'Mở lại',
}
export const ticketPriorityLabels: Record<TicketPriority, string> = {
  1: 'Thấp', 2: 'Bình thường', 3: 'Cao', 4: 'Khẩn cấp',
}

export function getTicketStatusLabel(value: number): string {
  return Object.hasOwn(ticketStatusLabels, value) ? ticketStatusLabels[value as TicketStatus] : 'Không xác định'
}
export function getTicketPriorityLabel(value: number): string {
  return Object.hasOwn(ticketPriorityLabels, value) ? ticketPriorityLabels[value as TicketPriority] : 'Không xác định'
}
