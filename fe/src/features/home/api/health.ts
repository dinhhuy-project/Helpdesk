import { apiClient } from '../../../shared/api/http-client'
import { ApiError } from '../../../shared/api/api-error'

/** Liveness only: this endpoint does not verify database/MinIO readiness. */
export async function getHealth(signal?: AbortSignal): Promise<void> {
  const response = await apiClient.get<string>('/health/live', { signal, responseType: 'text' })
  if (typeof response !== 'string' || response.trim() !== 'Healthy') {
    throw new ApiError('Dịch vụ phản hồi không hợp lệ. Vui lòng thử lại sau.', 'unexpected')
  }
}
