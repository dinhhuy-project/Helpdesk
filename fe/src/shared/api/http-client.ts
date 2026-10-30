import axios, { AxiosHeaders } from 'axios'
import type { AxiosAdapter, AxiosRequestConfig } from 'axios'
import { environment } from '../config/environment'
import { ApiError, toApiError } from './api-error'

export type RequestOptions<TBody = unknown> = Omit<AxiosRequestConfig<TBody>,
  'url' | 'baseURL' | 'auth' | 'withCredentials' | 'adapter' | 'signal' | 'headers'>
  & { signal?: AbortSignal; headers?: AxiosHeaders | Record<string, string> }

export type AuthorizeRequest = <T>(
  send: (accessToken: string, sessionSignal: AbortSignal) => Promise<T>,
  signal?: AbortSignal,
) => Promise<T>

export function createHttpClient(options: { baseURL: string; adapter?: AxiosAdapter; authorize?: AuthorizeRequest }) {
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
    async function send(accessToken?: string, sessionSignal?: AbortSignal) {
      try {
        const signal = sessionSignal && config?.signal
          ? AbortSignal.any([sessionSignal, config.signal]) : sessionSignal ?? config?.signal
        if (signal?.aborted) throw new ApiError('Yêu cầu đã được hủy.', 'cancelled')
        const headers = new AxiosHeaders(config?.headers)
        if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
        const response = await transport.request<TResponse>({ ...config, headers, signal, url: path })
        if (signal?.aborted) throw new ApiError('Yêu cầu đã được hủy.', 'cancelled')
        return response.data
      } catch (error) {
        // Do not retain Axios config/request: it may contain credentials or file data.
        throw toApiError(error)
      }
    }
    return options.authorize ? options.authorize(send, config?.signal) : send()
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
    delete: <TResponse = void>(path: string, config?: RequestOptions) => request<TResponse>(path, { ...config, method: 'DELETE' }),
  }
}

export type HttpClient = ReturnType<typeof createHttpClient>
export const apiClient = createHttpClient({ baseURL: environment.apiBaseUrl })
