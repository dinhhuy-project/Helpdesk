export function parseCommentPage(params: URLSearchParams): number | null {
  const raw = params.get('commentPage') ?? '1'
  return params.getAll('commentPage').length <= 1 && /^\d+$/.test(raw) && Number(raw) >= 1 && Number(raw) <= 1_000_000 ? Number(raw) : null
}
export function parseCommentVisibility(params: URLSearchParams): 'public' | 'internal' | null {
  const raw = params.get('comments') ?? 'public'
  return params.getAll('comments').length <= 1 && (raw === 'public' || raw === 'internal') ? raw : null
}
