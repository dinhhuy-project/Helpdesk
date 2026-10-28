import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { Permission, hasPermissions, readTicketRule } from './permissions'
import { visibleNavigation } from '../../../app/navigation/navigation'

describe('permission contract and navigation', () => {
  it('matches the actual backend permission catalog', () => {
    const source = readFileSync(resolve(process.cwd(), '../be/Domain/Constants/PermissionCodes.cs'), 'utf8')
    const backend = Object.fromEntries([...source.matchAll(/public const string (\w+) = "([^"]+)";/g)]
      .map(match => [match[1], match[2]]))
    expect(Permission).toEqual(backend)
  })
  it.each([Permission.TicketReadOwn, Permission.TicketReadTeam, Permission.TicketReadAll])(
    'accepts each explicitly granted Ticket read scope: %s', code => {
      expect(hasPermissions([code], readTicketRule)).toBe(true)
    },
  )
  it('does not imply Ticket read permission from admin/SLA/user management grants', () => {
    expect(hasPermissions([Permission.UserManage, Permission.RoleManage, Permission.TicketManageSla], readTicketRule)).toBe(false)
  })
  it('requires all explicit grants for an allOf rule and fails closed for empty policies', () => {
    const rule = { allOf: [Permission.UserManage, Permission.RoleManage] }
    expect(hasPermissions([Permission.UserManage], rule)).toBe(false)
    expect(hasPermissions([Permission.UserManage, Permission.RoleManage], rule)).toBe(true)
    expect(hasPermissions([], { anyOf: [] })).toBe(false)
    expect(hasPermissions([], { allOf: [] })).toBe(false)
  })
  it('separates user administration from role/grant administration', () => {
    const items = visibleNavigation([Permission.UserManage], true)
    expect(items.some(item => item.id === 'users')).toBe(true)
    expect(items.some(item => item.id === 'roles')).toBe(false)
  })
  it('uses the union of current permissions without selecting a single role', () => {
    const items = visibleNavigation([Permission.TicketCreate, Permission.TeamManage], true)
    expect(items.some(item => item.id === 'create-ticket')).toBe(true)
    expect(items.some(item => item.id === 'teams')).toBe(true)
    expect(items.some(item => item.id === 'users')).toBe(false)
  })
  it('shows nothing permission-sensitive before the profile is verified', () => {
    expect(visibleNavigation(Object.values(Permission), false)).toEqual([])
  })
})
