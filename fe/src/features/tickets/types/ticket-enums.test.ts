import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { getTicketPriorityLabel, getTicketStatusLabel, TicketPriority, TicketStatus } from './ticket-enums'

function readBackendEnum(name: string) {
  const source = readFileSync(resolve(process.cwd(), '../be/Domain/Enums', `${name}.cs`), 'utf8')
  return Object.fromEntries([...source.matchAll(/([A-Za-z]+)\s*=\s*(\d+)/g)].map(match => [match[1], Number(match[2])]))
}

describe('Ticket enum wire contract', () => {
  it('matches the actual backend JSON numeric values', () => {
    expect(TicketStatus).toEqual(readBackendEnum('TicketStatus'))
    expect(TicketPriority).toEqual(readBackendEnum('TicketPriority'))
  })
  it('handles an unknown future enum value without mislabeling it', () => {
    expect(getTicketStatusLabel(999)).toBe('Không xác định')
    expect(getTicketPriorityLabel(999)).toBe('Không xác định')
  })
})
