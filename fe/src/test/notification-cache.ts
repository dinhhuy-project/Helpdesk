import type { QueryClient } from '@tanstack/react-query'
import { expect } from 'vitest'

/** A verified session may load its new unread count after clearing a previous permission scope. */
export function expectOnlyCurrentNotificationCount(client: QueryClient, scope: string | null) {
  expect(client.getQueryCache().getAll().filter(query => !(query.queryKey.length === 3 && query.queryKey[0] === 'notifications'
    && query.queryKey[1] === scope && query.queryKey[2] === 'unread-count'))).toHaveLength(0)
}
