import axios from 'axios'
import type { AxiosAdapter, AxiosRequestConfig } from 'axios'
import { environment } from '../config/environment'
import { toApiError } from './api-error'

type RequestOptions<TBody = unknown> = Omit<AxiosRequestConfig<TBody>,
  'url' | 'baseURL' | 'auth' | 'withCredentials' | 'adapter'>

export function createHttpClient(options: { baseURL: string; adapter?: AxiosAdapter }) {
  const transport = axios.create({
    baseURL: options.baseURL,
    adapter: options.adapter,
    timeout: 15_000,
    withCredentials: false,
    headers: { Accept: 'application/json' },
  })

  async function request<TResponse, TBody = unknown>(path: string, config?: RequestOptions<TBody>): Promise<TResponse> {
    // Keep requests on the configured backend; future Bearer headers must not leak to another origin.
    if (!path.startsWith('/') || path.startsWith('//') || /[\\\s]/.test(path)) {
      throw new Error('API path phải bắt đầu bằng một dấu / và thuộc backend đã cấu hình.')
    }
    try {
      const response = await transport.request<TResponse>({ ...config, url: path })
      return response.data
    } catch (error) {
      // Do not retain Axios config/request: it may later contain credentials or file data.
      throw toApiError(error)
    }
  }

  return {
    request,
    get: <TResponse>(path: string, config?: RequestOptions) => request<TResponse>(path, { ...config, method: 'GET' }),
    post: <TResponse, TBody = unknown>(path: string, data: TBody, config?: RequestOptions<TBody>) =>
      request<TResponse, TBody>(path, { ...config, method: 'POST', data }),
    put: <TResponse, TBody = unknown>(path: string, data: TBody, config?: RequestOptions<TBody>) =>
      request<TResponse, TBody>(path, { ...config, method: 'PUT', data }),
    patch: <TResponse, TBody = unknown>(path: string, data: TBody, config?: RequestOptions<TBody>) =>
      request<TResponse, TBody>(path, { ...config, method: 'PATCH', data }),
  }
}

export const apiClient = createHttpClient({ baseURL: environment.apiBaseUrl })
