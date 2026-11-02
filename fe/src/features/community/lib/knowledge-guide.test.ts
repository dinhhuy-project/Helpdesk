import { describe, expect, it } from 'vitest'
import { emptyKnowledgeGuide, guideComplete, normalizedSteps } from './knowledge-guide'

describe('knowledge guide authoring rules', () => {
  const guide = { ...emptyKnowledgeGuide, applicability: 'Windows client', prerequisites: 'Active account', expectedOutcome: 'Connected', escalation: 'Create a Ticket', sourceName: 'IT procedure' }
  it('formats each nonempty source line without dropping safety notes', () => {
    expect(normalizedSteps('1. Check connection\r\n\r\n4) Record error\nNever share a password')).toBe('1. Check connection\n2. Record error\n3. Never share a password')
  })
  it.each(['', 'Freeform', '2. Starts at two', '1. First\n3. Gap', '1. \n2. Empty', '1. First\n1. Repeated',
    '1. First\n0. Invalid', '1. First\n100. Invalid', '1. First\n2.No separator', '1. First\n2.', '01. Leading zero'])('blocks incomplete step sequence %s', content => {
    expect(guideComplete(guide, content)).toBe(false)
  })
  it('accepts complete metadata and consecutive steps with details', () => {
    expect(guideComplete(guide, '1. Check\nMore detail\n2. Record error')).toBe(true)
    expect(guideComplete(emptyKnowledgeGuide, '1. Check')).toBe(false)
    expect(guideComplete(null, '1. Check')).toBe(false)
  })
  it('applies the same per-step and count limits as the backend', () => {
    expect(guideComplete(guide, `1. ${'x'.repeat(4001)}`)).toBe(false)
    expect(guideComplete(guide, Array.from({ length: 21 }, (_, index) => `${index + 1}. Step`).join('\n'))).toBe(false)
  })
})
