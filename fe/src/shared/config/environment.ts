export interface Environment {
  apiBaseUrl: string
}

export function readEnvironment(input: { VITE_API_BASE_URL?: string }, production = false): Environment {
  const value = input.VITE_API_BASE_URL?.trim() || '/'
  if (value === '/') return { apiBaseUrl: value }

  let url: URL
  try {
    url = new URL(value)
  } catch {
    throw new Error('VITE_API_BASE_URL phải là "/" hoặc HTTP(S) origin hợp lệ.')
  }
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password
    || url.search || url.hash || url.pathname !== '/') {
    throw new Error('VITE_API_BASE_URL phải là HTTP(S) origin, không chứa credentials, path hoặc query.')
  }
  if (production && url.protocol !== 'https:') throw new Error('API origin của bản production phải dùng HTTPS.')
  return { apiBaseUrl: url.origin }
}

export const environment = readEnvironment(import.meta.env, import.meta.env.PROD)
