import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import type { HubConnection, IHttpConnectionOptions } from '@microsoft/signalr'

export type NotificationConnection = Pick<HubConnection, 'start' | 'stop' | 'invoke' | 'on' | 'off' | 'onclose' | 'onreconnecting' | 'onreconnected'>
export type NotificationConnectionFactory = (url: string, accessTokenFactory: () => Promise<string>) => NotificationConnection
export function notificationConnectionFactory(options: Pick<IHttpConnectionOptions, 'transport' | 'httpClient'> = {}): NotificationConnectionFactory {
  return (url, accessTokenFactory) => new HubConnectionBuilder()
    .withUrl(url, { ...options, withCredentials: false, accessTokenFactory })
    .withAutomaticReconnect([0, 2_000, 10_000, 30_000])
    // SDK exceptions can contain access_token URLs. Never log them or copy them to UI state.
    .configureLogging(LogLevel.None).build()
}
