import type { TicketSla } from '../features/sla/types/sla'

export function noSlaForPath(path: string): TicketSla {
  return { ticketId: path.split('/')[4] ?? '', sla: null, status: 1, asOf: '2026-10-29T00:00:00Z' }
}
