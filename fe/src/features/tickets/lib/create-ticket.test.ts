import { describe, expect, it } from 'vitest'
import { validateTicketDraft } from './create-ticket'

const draft = { title: ' VPN ', description: ' Không thể kết nối. ', categoryId: '10000000-0000-0000-0000-000000000001', priority: '2' }
describe('create Ticket validation', () => {
  it('builds the four-field command with trimmed text and numeric priority', () => {
    expect(validateTicketDraft(draft).value).toEqual({ title: 'VPN', description: 'Không thể kết nối.', categoryId: draft.categoryId, priority: 2 })
  })
  it.each([
    { title: '' }, { title: ' \n\t ' }, { title: 'x'.repeat(301) },
    { description: '' }, { description: '\n \t' }, { description: 'x'.repeat(20_001) },
    { categoryId: '' }, { categoryId: 'not-a-guid' }, { categoryId: '00000000-0000-0000-0000-000000000000' },
    { priority: '0' }, { priority: '5' }, { priority: 'Normal' }, { priority: '1e0' },
  ])('rejects invalid input before POST: %o', change => {
    expect(validateTicketDraft({ ...draft, ...change }).errors).toBeDefined()
  })
  it('accepts the maximum lengths and validates length before trimming', () => {
    expect(validateTicketDraft({ ...draft, title: 'x'.repeat(300), description: 'x'.repeat(20_000) }).value).toBeDefined()
    expect(validateTicketDraft({ ...draft, title: ` ${'x'.repeat(300)}` }).errors?.title).toBeDefined()
  })
})
