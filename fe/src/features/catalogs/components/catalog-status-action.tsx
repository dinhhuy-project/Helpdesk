import { useState } from 'react'
import { Button } from '../../../shared/components/button'
import { Dialog } from '../../../shared/components/dialog'
import type { CatalogKind, CatalogRecord } from '../types/catalog'

export function CatalogStatusAction({ kind, resource, allowed = true, disabled, pending, onConfirm }: {
  kind: CatalogKind; resource: CatalogRecord; allowed?: boolean; disabled: boolean; pending: boolean; onConfirm: () => Promise<void>
}) {
  const [open, setOpen] = useState(false)
  if (!allowed) return null
  const title = resource.isActive ? 'Vô hiệu hóa' : 'Kích hoạt'
  const unavailable = kind === 'categories' && 'canActivate' in resource && !resource.isActive && !resource.canActivate
  const explanation = kind === 'departments' ? 'Giữ liên kết user, team, membership và lịch sử Ticket. Không tự đổi các cờ riêng của nhóm/thành viên.'
    : kind === 'teams' ? 'Giữ membership và Ticket đã phân công. Kích hoạt lại nhóm không khôi phục membership đã bị ngừng riêng.'
      : 'Giữ danh mục con và Ticket lịch sử. Nhánh dưới cha bị vô hiệu hóa sẽ không khả dụng trên form tạo Ticket.'
  return <div className="account-status-actions"><h2>Trạng thái</h2><p>{explanation}</p>
    {unavailable && <p className="field-hint">Kích hoạt danh mục cha và nhánh tổ tiên trước.</p>}
    <Button variant={resource.isActive ? 'danger' : 'secondary'} disabled={disabled || unavailable} onClick={() => setOpen(true)}>{title}</Button>
    <Dialog open={open} onOpenChange={value => { if (!pending) setOpen(value) }} title={`${title}: ${resource.name}`} description={explanation}>
      <Button variant={resource.isActive ? 'danger' : 'primary'} disabled={disabled || unavailable} onClick={async () => { await onConfirm(); setOpen(false) }}>{pending ? 'Đang cập nhật…' : 'Xác nhận đổi trạng thái'}</Button>
    </Dialog>
  </div>
}
