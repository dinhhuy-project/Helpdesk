import { useEffect, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../shared/api/api-error'
import { isGuid } from '../../../shared/lib/identifier'
import { formatDateTime } from '../../../shared/lib/date-time'
import { Button } from '../../../shared/components/button'
import { Dialog } from '../../../shared/components/dialog'
import { LoadingState } from '../../../shared/components/async-state'
import { PageTitle } from '../../../shared/components/page-title'
import { useApiCommand } from '../../../shared/hooks/use-api-command'
import { useAuth } from '../../auth/hooks/use-auth'
import { createSlaApi } from '../api/sla-api'
import { slaKeys, useSlaPolicy } from '../hooks/use-sla'
import { policyDraft } from '../lib/sla-policy-form'
import { getTicketPriorityLabel } from '../../tickets/types/ticket-enums'
import type { SlaPolicyDraft } from '../lib/sla-policy-form'
import type { SlaPolicy, SlaPolicyInput } from '../types/sla'
import { SlaPolicyForm } from '../components/sla-policy-form'
import { SlaCommandFeedback, SlaError } from '../components/sla-state'

export function SlaPolicyDetailPage() {
  const { id } = useParams()
  const { scope } = useAuth()
  return isGuid(id) ? <PolicyWorkspace key={`${scope}:${id.toLowerCase()}`} id={id.toLowerCase()} />
    : <SlaError error={new ApiError('Mã policy không hợp lệ.', 'http', 404)} />
}
function PolicyWorkspace({ id }: { id: string }) {
  const { client, scope } = useAuth()
  const queries = useQueryClient()
  const command = useApiCommand()
  const [denied, setDenied] = useState<ApiError | null>(null)
  const query = useSlaPolicy(id, !denied)
  const [baseline, setBaseline] = useState<SlaPolicy | null>(null)
  const [draft, setDraft] = useState<SlaPolicyDraft | null>(null)
  const [review, setReview] = useState<SlaPolicy | null>(null)
  const [statusTarget, setStatusTarget] = useState<boolean | null>(null)
  const [success, setSuccess] = useState('')
  const location = useLocation()
  const rejection = command.error ?? query.error
  useEffect(() => {
    let active = true
    if (rejection instanceof ApiError && (rejection.status === 403 || rejection.status === 404)) {
      void queries.cancelQueries({ queryKey: slaKeys.root(scope) }).then(() => {
        if (!active) return
        setDenied(rejection)
        queries.removeQueries({ queryKey: rejection.status === 403 ? slaKeys.root(scope) : slaKeys.detail(scope, id) })
      })
    }
    return () => { active = false }
  }, [rejection, queries, scope, id])
  async function reload() {
    setStatusTarget(null)
    const result = await query.refetch()
    if (command.isCurrent() && result.data && !result.error) setReview(result.data)
  }
  function accept(receipt: SlaPolicy, resetDraft: boolean) {
    setBaseline(receipt); if (resetDraft) setDraft(null)
    setReview(null); setSuccess('Đã lưu policy SLA.')
    queries.setQueryData(slaKeys.detail(scope, id), receipt)
  }
  if (denied || (rejection instanceof ApiError && (rejection.status === 403 || rejection.status === 404))) return <SlaError error={denied ?? rejection} />
  if (query.isError) return <SlaError error={query.error} onRetry={() => { void reload() }} />
  if (!query.data) return <LoadingState />
  const current = baseline ?? query.data
  const changed = current.version !== query.data.version
  const disabled = command.disabled || query.isFetching || changed || !!review
  async function save(value: SlaPolicyInput) {
    if (disabled) return
    setSuccess('')
    const result = await command.run(signal => createSlaApi(client).update(id, value, current.version, signal))
    if (result) accept(result, true)
  }
  return <section className="account-page"><PageTitle title={current.name} /><p className="eyebrow">Chính sách SLA</p><h1>{current.name}</h1>
    <p><Link to="/admin/sla-policies">Về danh sách policy</Link></p>
    {(success || location.state?.slaCreated) && <p role="status" className="success-message">{success || 'Đã tạo policy SLA.'}</p>}
    <dl className="account-summary panel"><div><dt>Trạng thái</dt><dd>{current.isActive ? 'Đang hoạt động' : 'Đã ngừng hoạt động'}</dd></div>
      <div><dt>Ngày tạo</dt><dd>{formatDateTime(current.createdAt)}</dd></div><div><dt>Cập nhật</dt><dd>{formatDateTime(current.updatedAt)}</dd></div>
      <div><dt>Phiên bản đang sửa</dt><dd>{current.version}</dd></div></dl>
    {changed && <p role="status">Có phiên bản policy mới. Bản nhập được giữ; tải lại để xem xét trước khi lưu.</p>}
    {query.isFetching && <LoadingState label="Đang kiểm tra lại policy…" />}
    <div className="panel"><SlaPolicyForm draft={draft ?? policyDraft(current)} onChange={value => { setBaseline(current); setDraft(value) }} onReview={() => setBaseline(current)}
      onSave={value => { void save(value) }} disabled={disabled} pending={command.pending} error={command.error} />
      <SlaCommandFeedback command={command} />
      <p><Button variant="secondary" disabled={command.pending || query.isFetching || command.remaining > 0} onClick={() => { void reload() }}>Tải lại để xem xét</Button></p>
      {review && <div className="workflow-review" role="status"><p>Đã đọc lại policy: {review.name} — {review.isActive ? 'đang hoạt động' : 'đã ngừng hoạt động'}. Kiểm tra bản nhập trước khi chủ động gửi lại.</p>
        <dl className="account-summary"><div><dt>Ưu tiên trên server</dt><dd>{getTicketPriorityLabel(review.priority)}</dd></div>
          <div><dt>Phản hồi trên server</dt><dd>{review.firstResponseMinutes.toLocaleString('vi-VN')} phút</dd></div>
          <div><dt>Giải quyết trên server</dt><dd>{review.resolutionMinutes.toLocaleString('vi-VN')} phút</dd></div>
          <div><dt>Phiên bản mới</dt><dd>{review.version}</dd></div></dl>
        <Button variant="secondary" disabled={command.uncertain || query.isFetching || command.pending || query.data.version !== review.version} onClick={() => {
          if (!command.resetAfterVerification()) return
          setBaseline(review); setReview(null)
        }}>Dùng phiên bản mới để tiếp tục</Button></div>}
      <div className="workflow-section"><h2>Trạng thái policy</h2>
        <p>Ngừng hoạt động chỉ ngăn áp dụng cho Ticket mới. SLA Ticket cũ và lịch sử được giữ nguyên.</p>
        {draft && <p className="field-hint">Bật/tắt policy không lưu bản nhập thông tin ở trên.</p>}
        <Button variant={current.isActive ? 'danger' : 'secondary'} disabled={disabled} onClick={() => { setBaseline(current); setStatusTarget(!current.isActive) }}>
          {current.isActive ? 'Ngừng policy' : 'Kích hoạt policy'}</Button>
      </div>
    </div>
    <Dialog open={statusTarget !== null && !disabled} onOpenChange={open => { if (!open) setStatusTarget(null) }} title="Xác nhận trạng thái policy SLA">
      <p>{statusTarget ? 'Kích hoạt policy cho Ticket mới cùng priority. Mỗi priority chỉ có một policy hoạt động.' : 'Ngừng áp dụng policy cho Ticket mới. Deadline Ticket cũ giữ nguyên.'}</p>
      <Button disabled={disabled} variant={statusTarget ? 'primary' : 'danger'} onClick={async () => {
        if (statusTarget === null || disabled) return
        const target = statusTarget; setStatusTarget(null); setSuccess('')
        const result = await command.run(signal => createSlaApi(client).status(id, target, current.version, signal))
        if (result) accept(result, false)
      }}>Xác nhận trạng thái</Button>
    </Dialog>
  </section>
}
