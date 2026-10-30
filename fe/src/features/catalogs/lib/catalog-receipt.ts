import { ApiError } from '../../../shared/api/api-error'
import { isGuid } from '../../../shared/lib/identifier'
import type { CatalogRecord } from '../types/catalog'

export function verifyCatalogReceipt<T extends CatalogRecord>(value: T, id?: string): T {
  if (!isGuid(value?.id) || (id && value.id !== id) || !Number.isInteger(value.version) || value.version < 1) {
    throw new ApiError('Chưa xác nhận được thay đổi đã lưu.', 'unexpected')
  }
  return value
}
