import { useState } from 'react'
import { getFieldError } from '../../../shared/api/api-error'
import { ErrorState, LoadingState } from '../../../shared/components/async-state'
import { Button } from '../../../shared/components/button'
import { Dialog } from '../../../shared/components/dialog'
import { FormField, Select, Textarea } from '../../../shared/components/form-field'
import type { TicketWorkflowActions } from '../hooks/use-ticket-workflow-actions'
import { getTicketStatusLabel, type TicketStatus } from '../types/ticket-enums'
import { emptyWorkflowDraft } from '../types/ticket-workflow'
import { TicketAssignmentPicker } from './ticket-assignment-picker'

const actionLabels: Partial<Record<TicketStatus, string>> = {
  2: 'Bắt đầu / tiếp tục xử lý', 3: 'Chờ người yêu cầu', 4: 'Giải quyết yêu cầu',
  5: 'Xác nhận và đóng', 6: 'Hủy yêu cầu', 7: 'Mở lại yêu cầu',
}
const errorMessages: Record<string, string> = {
  VersionConflict: 'Yêu cầu đã thay đổi. Bản nhập được giữ lại; hãy tải lại và xem xét trước khi gửi tiếp.',
  InvalidTransition: 'Trạng thái hiện tại không cho phép thao tác này. Hãy tải lại và chọn bước phù hợp.',
  InvalidAssignment: 'Nhóm hoặc người xử lý không còn hợp lệ. Hãy chọn lại từ danh sách hiện tại.',
  ResolutionRequired: 'Vui lòng nhập kết quả giải quyết.',
}
export function TicketWorkflowPanel({ id, ticketVersion, actions, externallyBlocked = false }: { id: string; ticketVersion: number; actions: TicketWorkflowActions; externallyBlocked?: boolean }) {
  const { workflow, command } = actions
  const [validation, setValidation] = useState<string>()
  const [confirm, setConfirm] = useState<'assign' | 'transition' | null>(null)
  if (workflow.isPending) return <LoadingState label="Đang kiểm tra thao tác được phép…" />
  if (workflow.isError) return <ErrorState error={workflow.error} onRetry={actions.reload} />
  const context = workflow.data
  const draft = actions.draft ?? emptyWorkflowDraft(context.version)
  const changed = draft.version !== context.version || ticketVersion !== context.version
  const writeBlocked = actions.blocked || changed || externallyBlocked
  const unavailable = draft.status !== null && !context.transitions.includes(draft.status)
  const selectedStatus = draft.status
  const assignmentReady = context.canAssign && !!draft.team && draft.agentChosen && draft.assignmentReviewed
  const transitionReady = !!selectedStatus && !unavailable
  function askTransition() {
    if (!transitionReady || writeBlocked) return
    if (selectedStatus === 4 && (!draft.resolutionSummary.trim() || draft.resolutionSummary.length > 4000)) {
      setValidation('Vui lòng nhập kết quả giải quyết, tối đa 4.000 ký tự.'); return
    }
    setValidation(undefined); setConfirm('transition')
  }
  return <section className="ticket-card ticket-workflow" aria-labelledby="ticket-workflow-heading">
    <h2 id="ticket-workflow-heading">Phân công và xử lý</h2>
    <dl className="ticket-information">
      <div><dt>Nhóm hiện tại</dt><dd>{context.assignedTeamName ?? 'Chưa phân công nhóm'}</dd></div>
      <div><dt>Người xử lý hiện tại</dt><dd>{context.assignedAgentName ?? 'Chưa phân công người xử lý'}</dd></div>
    </dl>
    {!context.assignmentIsValid && context.status !== 5 && context.status !== 6 && <p className="field-hint">Cần nhóm và người xử lý hợp lệ trước khi bắt đầu, tiếp tục hoặc giải quyết yêu cầu.</p>}
    {workflow.isFetching && <LoadingState label="Đang kiểm tra lại thao tác…" />}
    {changed && <p role="status">Có phiên bản mới. Bản nhập vẫn được giữ; tải lại để xem xét trước khi gửi.</p>}
    {command.error && <div className="auth-message auth-message--error" role="alert">
      <p>{errorMessages[command.error.code ?? ''] ?? command.error.message}</p>
      {command.error.traceId && <p className="field-hint">Mã tra cứu: {command.error.traceId}</p>}
      {command.remaining > 0 && <p>Vui lòng chờ {command.remaining} giây trước khi thử lại.</p>}
    </div>}
    {command.uncertain && <div role="alert" className="ticket-uncertain"><h3>Chưa xác định được kết quả thay đổi</h3>
      <p>Thao tác có thể đã được ghi nhận. Hãy tải lại để kiểm tra; hệ thống giữ khóa gửi và không tự gửi lại.</p></div>}
    {(changed || !!command.error || !!actions.review) && <Button variant="secondary" disabled={command.pending || actions.refreshing || command.remaining > 0} onClick={actions.reload}>Tải lại để xem xét</Button>}
    {actions.review && <div className="workflow-review" role="status"><p>Đã đọc lại: {getTicketStatusLabel(actions.review.status)}. Kiểm tra bản nhập và thao tác còn được phép trước khi tiếp tục. Phân công cần chọn lại nhóm/người xử lý.</p>
      <Button variant="secondary" disabled={command.uncertain || workflow.isFetching || command.pending} onClick={actions.acceptReview}>Dùng phiên bản mới để tiếp tục</Button></div>}
    {(context.canAssign || draft.team) && <div className="workflow-section"><h3>Phân công</h3>
      <p>Nhóm dự kiến: <strong>{draft.team ? `${draft.team.name} · ${draft.team.departmentName}` : 'Chưa chọn'}</strong></p>
      <p>Người xử lý dự kiến: <strong>{draft.agentChosen ? draft.agent ? `${draft.agent.displayName} · ${draft.agent.id.slice(-8)}` : 'Chỉ giao nhóm' : 'Chưa chọn'}</strong></p>
      {!context.canAssign ? <p>Bạn hiện không được thay đổi phân công.</p> : <>
        <div className="workflow-actions"><TicketAssignmentPicker id={id} kind="team" disabled={writeBlocked} onSelect={team => {
          actions.setDraft({ ...draft, team, agent: null, agentChosen: false, assignmentReviewed: false })
        }} />
          {draft.team && <TicketAssignmentPicker key={draft.team.id} id={id} kind="agent" teamId={draft.team.id} disabled={writeBlocked} onSelect={agent => {
            actions.setDraft({ ...draft, agent, agentChosen: true, assignmentReviewed: true })
          }} />}
          <Button disabled={writeBlocked || !assignmentReady} onClick={() => setConfirm('assign')}>Lưu phân công</Button></div>
        <p className="field-hint">Đổi nhóm phải chọn lại người xử lý hoặc xác nhận chỉ giao nhóm. Phân công không tự bắt đầu xử lý.</p>
      </>}
    </div>}
    <form className="workflow-section" aria-label="Chuyển trạng thái" noValidate onSubmit={event => { event.preventDefault(); askTransition() }}>
      <h3>Chuyển trạng thái</h3>
      {context.transitions.length === 0 && <p>Hiện không có bước chuyển trạng thái được phép.</p>}
      {(context.transitions.length > 0 || selectedStatus !== null) && <>
        <FormField label="Thao tác" required>{field => <Select {...field} disabled={writeBlocked} value={selectedStatus ?? ''} onChange={event => {
          actions.setDraft({ ...draft, status: event.target.value ? Number(event.target.value) as TicketStatus : null }); setValidation(undefined)
        }}><option value="">Chọn thao tác</option>
          {unavailable && selectedStatus && <option value={selectedStatus} disabled>{actionLabels[selectedStatus]} (không còn được phép)</option>}
          {context.transitions.map(status => <option key={status} value={status}>{actionLabels[status] ?? getTicketStatusLabel(status)}</option>)}
        </Select>}</FormField>
        {(selectedStatus === 4 || draft.resolutionSummary) && <FormField label="Kết quả giải quyết" required={selectedStatus === 4}
          error={validation ?? (command.error ? getFieldError(command.error, 'resolutionSummary') : undefined)}
          hint={selectedStatus === 4 ? `${draft.resolutionSummary.length.toLocaleString('vi-VN')} / 4.000 ký tự. Nội dung sẽ xuất hiện trong lịch sử công khai.` : 'Bản nhập được giữ lại. Chỉ gửi nội dung này khi chọn giải quyết yêu cầu.'}>
          {field => <Textarea {...field} rows={5} maxLength={4000} value={draft.resolutionSummary} disabled={command.pending}
            onChange={event => { actions.setDraft({ ...draft, resolutionSummary: event.target.value }); setValidation(undefined) }} />}
        </FormField>}
        {unavailable && <p role="alert">Thao tác đã chọn không còn được phép. Hãy chọn lại; nội dung bạn nhập vẫn được giữ.</p>}
        <Button type="submit" disabled={writeBlocked || !transitionReady}>Xem lại chuyển trạng thái</Button>
      </>}
    </form>
    <Dialog open={confirm !== null && !writeBlocked} onOpenChange={open => { if (!open) setConfirm(null) }} title={confirm === 'assign' ? 'Xác nhận phân công' : 'Xác nhận chuyển trạng thái'}>
      <p>{confirm === 'assign' ? `Giao cho ${draft.team?.name ?? ''} — ${draft.agent?.displayName ?? 'chỉ giao nhóm'}.` : actionLabels[selectedStatus ?? 1]}</p>
      {confirm === 'transition' && selectedStatus === 4 && <p className="ticket-plain-text">{draft.resolutionSummary}</p>}
      {confirm === 'transition' && selectedStatus === 7 && <p>Mở lại sẽ xóa kết quả giải quyết hiện tại; lịch sử vẫn được giữ.</p>}
      {confirm === 'transition' && (selectedStatus === 5 || selectedStatus === 6) && <p>Ticket kết thúc sau thao tác này và không thể chuyển trạng thái tiếp.</p>}
      <Button variant={selectedStatus === 6 && confirm === 'transition' ? 'danger' : 'primary'} disabled={writeBlocked || (confirm === 'assign' ? !assignmentReady : !transitionReady)} onClick={() => {
        if (!confirm) return
        const kind = confirm; setConfirm(null); void actions.submit(kind, draft)
      }}>Xác nhận thao tác</Button>
    </Dialog>
  </section>
}
