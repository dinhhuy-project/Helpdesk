export type CatalogKind = 'departments' | 'teams' | 'categories'
export interface Department {
  id: string; name: string; description: string | null; isActive: boolean
  createdAt: string; updatedAt: string | null; version: number
}
export interface SupportTeam extends Department { departmentId: string; departmentName: string; departmentIsActive: boolean }
export interface TicketCategory extends Department {
  parentId: string | null; parentName: string | null; isAvailable: boolean; hasChildren: boolean; canActivate: boolean
}
export interface CatalogRecords { departments: Department; teams: SupportTeam; categories: TicketCategory }
export type CatalogRecord = CatalogRecords[CatalogKind]
export interface NameDraft { name: string; description: string | null }
export interface CatalogCreates { departments: NameDraft; teams: NameDraft & { departmentId: string }; categories: NameDraft & { parentId: string | null } }
export interface CatalogUpdates { departments: NameDraft & { version: number }; teams: NameDraft & { version: number }; categories: NameDraft & { parentId: string | null; version: number } }
export interface CatalogSearch {
  page: number; pageSize: number; search?: string; isActive?: boolean; sortBy: 'name' | 'createdAt'; descending: boolean
  departmentId?: string; parentId?: string; rootsOnly?: boolean
}
export interface LookupSearch { page: number; pageSize: number; search?: string }
export interface LookupOption { id: string; name: string }
export interface TeamAccess { canCreate: boolean; canChangeStatus: boolean; canManageLeadership: boolean; canAddMembers: boolean }
export interface TeamMember {
  userId: string; displayName: string; userIsActive: boolean; isActive: boolean; isLeader: boolean
  joinedAt: string; updatedAt: string | null; version: number; canBeLeader: boolean
}
export interface TeamMemberCandidate { id: string; displayName: string; departmentName: string | null }
export interface MemberSearch { page: number; pageSize: number; isActive?: boolean }
export interface MembershipResult { team: SupportTeam; member: TeamMember }
