import { isGuid } from '../../../shared/lib/identifier'
import type { CatalogKind, CatalogSearch, MemberSearch } from '../types/catalog'

type Parsed<T> = { value: T; error?: never } | { error: string; value?: never }
export const defaultCatalogSearch: CatalogSearch = { page: 1, pageSize: 20, sortBy: 'name', descending: false }
function reader(params: URLSearchParams) {
  const read = (key: string) => {
    if (params.getAll(key).length > 1) throw new Error(`Tham số ${key} bị lặp.`)
    return params.get(key)?.trim() || undefined
  }
  return { read,
    integer: (key: string, fallback: number, max: number) => {
      const raw = read(key)
      if (raw && (!/^\d+$/.test(raw) || Number(raw) < 1 || Number(raw) > max)) throw new Error('Phân trang không hợp lệ.')
      return raw ? Number(raw) : fallback
    },
    boolean: (key: string) => {
      const raw = read(key)
      if (raw !== undefined && raw !== 'true' && raw !== 'false') throw new Error('Bộ lọc trạng thái không hợp lệ.')
      return raw === undefined ? undefined : raw === 'true'
    },
  }
}
export function parseCatalogSearch(params: URLSearchParams, kind: CatalogKind): Parsed<CatalogSearch> {
  try {
    const { read, integer, boolean } = reader(params)
    const value: CatalogSearch = { ...defaultCatalogSearch, page: integer('page', 1, 1_000_000), pageSize: integer('pageSize', 20, 100),
      search: read('search'), isActive: boolean('isActive'), descending: boolean('descending') ?? false }
    if ((value.search?.length ?? 0) > 200) throw new Error('Tìm kiếm tối đa 200 ký tự.')
    const sort = read('sortBy')
    if (sort !== undefined && sort !== 'name' && sort !== 'createdAt') throw new Error('Sắp xếp không hợp lệ.')
    if (sort) value.sortBy = sort
    if (kind === 'teams') {
      const id = read('departmentId')
      if (id && !isGuid(id)) throw new Error('Phòng ban không hợp lệ.')
      value.departmentId = id?.toLowerCase()
    }
    if (kind === 'categories') {
      const id = read('parentId')
      if (id && !isGuid(id)) throw new Error('Danh mục cha không hợp lệ.')
      value.parentId = id?.toLowerCase()
      value.rootsOnly = boolean('rootsOnly') ?? (!id && !value.search)
      if (value.rootsOnly && id) throw new Error('Chọn danh mục gốc hoặc một nhánh, không chọn đồng thời.')
    }
    return { value }
  } catch (failure) { return { error: failure instanceof Error ? failure.message : 'Bộ lọc không hợp lệ.' } }
}
export function catalogSearchParams(search: CatalogSearch) {
  const result = new URLSearchParams()
  for (const [key, value] of Object.entries(search)) {
    if (value !== undefined && value !== defaultCatalogSearch[key as keyof CatalogSearch]) result.set(key, String(value))
  }
  return result
}
export function parseMemberSearch(params: URLSearchParams): Parsed<MemberSearch> {
  try {
    const { integer, boolean } = reader(params)
    return { value: { page: integer('memberPage', 1, 1_000_000), pageSize: integer('memberPageSize', 20, 100), isActive: boolean('memberIsActive') } }
  } catch (failure) { return { error: failure instanceof Error ? failure.message : 'Phân trang thành viên không hợp lệ.' } }
}
export const catalogLabels: Record<CatalogKind, { title: string; singular: string; path: string }> = {
  departments: { title: 'Phòng ban', singular: 'phòng ban', path: '/admin/departments' },
  teams: { title: 'Nhóm hỗ trợ', singular: 'nhóm hỗ trợ', path: '/admin/teams' },
  categories: { title: 'Danh mục yêu cầu', singular: 'danh mục', path: '/admin/categories' },
}
