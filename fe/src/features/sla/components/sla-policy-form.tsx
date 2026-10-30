import { useState } from 'react'
import { getFieldError } from '../../../shared/api/api-error'
import type { ApiError } from '../../../shared/api/api-error'
import { FormField, Input, Select } from '../../../shared/components/form-field'
import { Button } from '../../../shared/components/button'
import { Dialog } from '../../../shared/components/dialog'
import { ticketPriorityLabels, getTicketPriorityLabel } from '../../tickets/types/ticket-enums'
import { parsePolicyDraft, slaMaximumMinutes } from '../lib/sla-policy-form'
import type { SlaPolicyDraft } from '../lib/sla-policy-form'
import type { SlaPolicyInput } from '../types/sla'

export function SlaPolicyForm({ draft, onChange, onSave, onReview, disabled, pending, error, creating = false }: {
  draft: SlaPolicyDraft; onChange: (value: SlaPolicyDraft) => void; onSave: (value: SlaPolicyInput) => void
  disabled: boolean; pending: boolean; error: ApiError | null; creating?: boolean
  onReview?: () => void
}) {
  const [errors, setErrors] = useState<Partial<Record<keyof SlaPolicyDraft, string>>>({})
  const [confirm, setConfirm] = useState<SlaPolicyInput | null>(null)
  const change = (field: keyof SlaPolicyDraft, value: string) => { onChange({ ...draft, [field]: value }); setErrors({}) }
  const fieldError = (field: keyof SlaPolicyDraft) => errors[field] ?? (error ? getFieldError(error, field) : undefined)
  return <><form className="account-form sla-policy-form" aria-label="Thông tin policy SLA" noValidate onSubmit={event => {
    event.preventDefault(); if (disabled) return
    const result = parsePolicyDraft(draft); setErrors(result.errors)
    if (result.value) { onReview?.(); setConfirm(result.value) }
  }}>
    <FormField label="Tên policy" required error={fieldError('name')}>{field => <Input {...field} value={draft.name} maxLength={200} disabled={pending}
      onChange={event => change('name', event.target.value)} />}</FormField>
    <FormField label="Mức ưu tiên" required error={fieldError('priority')}>{field => <Select {...field} value={draft.priority} disabled={pending} onChange={event => change('priority', event.target.value)}>
      {Object.entries(ticketPriorityLabels).map(([priority, label]) => <option key={priority} value={priority}>{label}</option>)}
    </Select>}</FormField>
    <FormField label="Phản hồi đầu tiên (phút)" required error={fieldError('firstResponseMinutes')}>{field => <Input {...field} type="number" min={1} max={slaMaximumMinutes} step={1}
      value={draft.firstResponseMinutes} disabled={pending} onChange={event => change('firstResponseMinutes', event.target.value)} />}</FormField>
    <FormField label="Giải quyết (phút)" required error={fieldError('resolutionMinutes')}>{field => <Input {...field} type="number" min={1} max={slaMaximumMinutes} step={1}
      value={draft.resolutionMinutes} disabled={pending} onChange={event => change('resolutionMinutes', event.target.value)} />}</FormField>
    <p className="field-hint">Tính 24/7 theo phút liên tục. Phản hồi đầu tiên không lớn hơn thời gian giải quyết. Mỗi priority chỉ có một policy đang hoạt động.</p>
    <p className="field-hint">{creating ? 'Policy mới được bật ngay và chỉ áp dụng cho Ticket tạo sau đó có cùng priority.' : 'Thay đổi chỉ áp dụng cho Ticket tạo sau này; snapshot và deadline Ticket cũ giữ nguyên.'}</p>
    <Button type="submit" pending={pending} disabled={disabled}>{creating ? 'Tạo policy' : 'Lưu policy'}</Button>
  </form>
    <Dialog open={confirm !== null && !disabled} onOpenChange={open => { if (!open) setConfirm(null) }} title={creating ? 'Xác nhận tạo policy SLA' : 'Xác nhận lưu policy SLA'}>
      {confirm && <><p><strong>{confirm.name}</strong> · {getTicketPriorityLabel(confirm.priority)}</p><p>Phản hồi: {confirm.firstResponseMinutes.toLocaleString('vi-VN')} phút. Giải quyết: {confirm.resolutionMinutes.toLocaleString('vi-VN')} phút.</p>
        <p>Không thay deadline hoặc gán SLA hồi tố cho Ticket đã tạo.</p>
        <Button disabled={disabled} onClick={() => { setConfirm(null); onSave(confirm) }}>Xác nhận lưu</Button></>}
    </Dialog>
  </>
}
