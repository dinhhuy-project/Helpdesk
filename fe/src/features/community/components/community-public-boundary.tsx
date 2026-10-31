import { useEffect } from 'react'
import { Outlet } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { environment } from '../../../shared/config/environment'
import { publicCommunityKey } from '../hooks/use-community'

export function CommunityPublicBoundary() {
  const queries = useQueryClient()
  useEffect(() => {
    const url = environment.apiBaseUrl === '/' ? '/hubs/community' : new URL('/hubs/community', environment.apiBaseUrl).href
    const connection = new HubConnectionBuilder().withUrl(url, { withCredentials: false })
      .withAutomaticReconnect([0, 2000, 10000, 30000]).configureLogging(LogLevel.None).build()
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | undefined
    const refresh = () => { void queries.resetQueries({ queryKey: publicCommunityKey }) }
    // Public invalidations only. No JWT, Ticket subscription or private notification group.
    connection.on('CommunityChanged', refresh)
    connection.onreconnected(refresh)
    const start = async () => {
      try { await connection.start(); if (!stopped) refresh() }
      catch { if (!stopped) timer = setTimeout(() => { void start() }, 10000) }
    }
    connection.onclose(() => { if (!stopped) timer = setTimeout(() => { void start() }, 10000) })
    void start()
    return () => { stopped = true; clearTimeout(timer); connection.off('CommunityChanged', refresh); void connection.stop().catch(() => {}) }
  }, [queries])
  return <Outlet />
}
