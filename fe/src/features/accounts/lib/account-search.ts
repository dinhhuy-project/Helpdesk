import type { AccountSearch } from '../types/account'

export const defaultAccountSearch: AccountSearch = { page: 1, pageSize: 20, sortBy: 'createdAt', descending: true }
export function parseAccountSearch(params: URLSearchParams): { value: AccountSearch; error?: never } | { error: string; value?: never } {
  const value = { ...defaultAccountSearch }
  try {
    const read = (key: string) => {
      if (params.getAll(key).length > 1) throw new Error(`Tham số ${key} bị lặp.`)
      return params.get(key)?.trim() || undefined
    }
    for (const [key, max] of [['page', 1_000_000], ['pageSize', 100]] as const) {
      const raw = read(key)
      if (raw && (!/^\d+$/.test(raw) || Number(raw) < 1 || Number(raw) > max)) throw new Error('Phân trang không hợp lệ.')
      if (raw) value[key] = Number(raw)
    }
    value.search = read('search')
    if ((value.search?.length ?? 0) > 200) throw new Error('Tìm kiếm tối đa 200 ký tự.')
    for (const key of ['isActive', 'descending'] as const) {
      const raw = read(key)
      if (raw && raw !== 'true' && raw !== 'false') throw new Error('Bộ lọc không hợp lệ.')
      if (raw) value[key] = raw === 'true'
    }
    const sort = read('sortBy')
    if (sort) {
      if (sort !== 'createdAt' && sort !== 'email' && sort !== 'displayName') throw new Error('Sắp xếp không hợp lệ.')
      value.sortBy = sort
    }
    return { value }
  } catch (error) { return { error: error instanceof Error ? error.message : 'Bộ lọc không hợp lệ.' } }
}
export function accountSearchParams(value: AccountSearch) {
  const result = new URLSearchParams()
  for (const [key, item] of Object.entries(value)) {
    if (item !== undefined && item !== defaultAccountSearch[key as keyof AccountSearch]) result.set(key, String(item))
  }
  return result
}
