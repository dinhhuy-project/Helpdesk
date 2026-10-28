export function safeReturnTo(value: string | null, fallback = '/account'): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || /[\\\s]/.test(value)) return fallback
  const origin = 'https://helpdesk.invalid'
  try {
    const url = new URL(value, origin)
    const pathname = decodeURIComponent(url.pathname).replace(/\/+$/, '').toLowerCase()
    if (url.origin !== origin || pathname === '/login') return fallback
    return `${url.pathname}${url.search}${url.hash}`
  } catch { return fallback }
}
