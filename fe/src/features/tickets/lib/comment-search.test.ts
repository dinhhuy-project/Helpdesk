import { describe, expect, it } from 'vitest'
import { parseCommentPage, parseCommentVisibility } from './comment-search'
describe('comment URL filters', () => {
  it('uses public page 1 and separates comment paging from activity paging', () => {
    const params = new URLSearchParams('activityPage=9')
    expect(parseCommentPage(params)).toBe(1)
    expect(parseCommentVisibility(params)).toBe('public')
    expect(parseCommentPage(new URLSearchParams('commentPage=2&activityPage=9'))).toBe(2)
  })
  it.each(['commentPage=0', 'commentPage=1e3', 'commentPage=1000001', 'commentPage=1&commentPage=2'])('rejects invalid page: %s', raw => {
    expect(parseCommentPage(new URLSearchParams(raw))).toBeNull()
  })
  it.each(['comments=all', 'comments=private', 'comments=public&comments=internal'])('rejects invalid visibility: %s', raw => {
    expect(parseCommentVisibility(new URLSearchParams(raw))).toBeNull()
  })
})
