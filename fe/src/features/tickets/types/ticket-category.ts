export interface TicketCategoryLookup {
  id: string
  parentId: string | null
  name: string
  hasChildren: boolean
}
export interface CategoryLookupSearch {
  page: number
  pageSize: number
  search?: string
  parentId?: string
  rootsOnly: boolean
}
export interface SelectedTicketCategory {
  category: TicketCategoryLookup
  path: string[]
}
