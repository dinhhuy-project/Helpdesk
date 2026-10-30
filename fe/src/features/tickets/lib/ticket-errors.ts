import { ApiError } from '../../../shared/api/api-error'

export function isTicketAccessError(error: unknown): error is ApiError {
  return error instanceof ApiError && (error.status === 403 || error.status === 404)
}
