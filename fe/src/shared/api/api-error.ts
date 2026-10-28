import axios from 'axios'

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  code?: string
  traceId?: string
  errors?: Record<string, string[]>
}

export type ApiErrorKind = 'http' | 'network' | 'timeout' | 'cancelled' | 'unexpected'

export class ApiError extends Error {
  readonly name = 'ApiError'
  readonly kind: ApiErrorKind
  readonly status?: number
  readonly problem: ProblemDetails
  readonly retryAfterSeconds?: number

  constructor(
    message: string,
    kind: ApiErrorKind,
    status?: number,
    problem: ProblemDetails = {},
    retryAfterSeconds?: number,
  ) {
    super(message)
    this.kind = kind
    this.status = status
    this.problem = problem
    this.retryAfterSeconds = retryAfterSeconds
  }

  get code() { return this.problem.code }
  get traceId() { return this.problem.traceId }
  get fieldErrors() { return this.problem.errors ?? {} }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function parseProblemDetails(value: unknown): ProblemDetails {
  if (!isRecord(value)) return {}
  const problem: ProblemDetails = {}
  for (const key of ['type', 'title', 'detail', 'instance', 'code', 'traceId'] as const) {
    if (typeof value[key] === 'string') problem[key] = value[key]
  }
  if (typeof value.status === 'number') problem.status = value.status
  if (isRecord(value.errors)) {
    problem.errors = Object.fromEntries(Object.entries(value.errors).filter(
      (entry): entry is [string, string[]] => Array.isArray(entry[1])
        && entry[1].every((message: unknown) => typeof message === 'string'),
    ))
  }
  return problem
}

const statusMessages: Record<number, string> = {
  400: 'Dữ liệu chưa hợp lệ. Vui lòng kiểm tra lại.',
  401: 'Phiên đăng nhập không hợp lệ hoặc đã hết hạn.',
  403: 'Bạn không có quyền thực hiện thao tác này.',
  404: 'Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.',
  409: 'Dữ liệu đã thay đổi. Vui lòng tải lại trước khi tiếp tục.',
  413: 'Dữ liệu gửi lên vượt quá giới hạn cho phép.',
  415: 'Định dạng dữ liệu không được hỗ trợ.',
  429: 'Bạn thao tác quá nhanh. Vui lòng thử lại sau.',
}

function parseRetryAfter(value: unknown): number | undefined {
  if (typeof value !== 'string' && typeof value !== 'number') return undefined
  const seconds = Number(value)
  if (Number.isFinite(seconds) && seconds >= 0) return Math.ceil(seconds)
  const timestamp = Date.parse(String(value))
  return Number.isNaN(timestamp) ? undefined : Math.max(0, Math.ceil((timestamp - Date.now()) / 1000))
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error
  if (axios.isCancel(error)) return new ApiError('Yêu cầu đã được hủy.', 'cancelled')
  if (!axios.isAxiosError<unknown>(error)) {
    return new ApiError('Có lỗi xảy ra. Vui lòng thử lại.', 'unexpected')
  }
  if (error.response) {
    const status = error.response.status
    const problem = parseProblemDetails(error.response.data)
    return new ApiError(
      problem.title || statusMessages[status] || 'Dịch vụ đang gặp sự cố. Vui lòng thử lại sau.',
      'http', status, problem, parseRetryAfter(error.response.headers['retry-after']),
    )
  }
  if (['ECONNABORTED', 'ETIMEDOUT'].includes(error.code ?? '')) {
    return new ApiError('Yêu cầu quá thời gian chờ. Vui lòng kiểm tra kết quả trước khi thử lại.', 'timeout')
  }
  return new ApiError('Không thể kết nối dịch vụ. Vui lòng kiểm tra kết nối và thử lại.', 'network')
}

/** Match ASP.NET property paths without changing the original validation dictionary. */
export function getFieldError(error: unknown, field: string): string | undefined {
  const entry = Object.entries(toApiError(error).fieldErrors).find(([key]) =>
    key.replace(/^\$\./, '').toLowerCase() === field.toLowerCase())
  return entry?.[1].join(' ')
}
