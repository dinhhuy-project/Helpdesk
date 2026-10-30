import { useState } from 'react'
import type { ApiError } from '../../../shared/api/api-error'
import { getFieldError } from '../../../shared/api/api-error'
import { Button } from '../../../shared/components/button'
import { FormField, Textarea } from '../../../shared/components/form-field'
import type { CommentDraft, TicketDiscussionAccess } from '../types/ticket-discussion'
import type { useAddComment } from '../hooks/use-add-comment'

export type CommentCommand = ReturnType<typeof useAddComment>
export function TicketCommentComposer({ access, draft, onDraftChange, command }: {
  access: TicketDiscussionAccess; draft: CommentDraft; onDraftChange: (draft: CommentDraft) => void; command: CommentCommand
}) {
  const [validation, setValidation] = useState<string>()
  const writable = draft.isInternal ? access.canCommentInternal : access.canCommentPublic
  function changeMode(isInternal: boolean) {
    if (isInternal === draft.isInternal) return
    onDraftChange({ content: '', isInternal }); setValidation(undefined)
  }
  if (!access.canCommentPublic && !access.canCommentInternal) return <p className="field-hint">Bạn có thể đọc lịch sử nhưng hiện không được gửi bình luận mới.</p>
  return <form className={`comment-composer${draft.isInternal ? ' is-internal' : ''}`} aria-label="Gửi bình luận" noValidate onSubmit={event => {
    event.preventDefault()
    if (!writable || command.disabled) return
    if (!draft.content.trim() || draft.content.length > 20_000) {
      setValidation(!draft.content.trim() ? 'Vui lòng nhập nội dung.' : 'Nội dung tối đa 20.000 ký tự.'); return
    }
    setValidation(undefined)
    void command.submit({ content: draft.content.trim(), isInternal: draft.isInternal })
  }}>
    <div className="comment-modes" role="group" aria-label="Loại nội dung gửi">
      {access.canCommentPublic && <Button variant="secondary" disabled={command.pending} aria-pressed={!draft.isInternal} onClick={() => changeMode(false)}>Bình luận công khai</Button>}
      {access.canCommentInternal && <Button variant="secondary" disabled={command.pending} aria-pressed={draft.isInternal} onClick={() => changeMode(true)}>Ghi chú nội bộ</Button>}
    </div>
    <p className="field-hint">Đổi loại nội dung sẽ xóa bản nháp hiện tại để tránh gửi nhầm phạm vi.</p>
    {writable ? <><h3>{draft.isInternal ? 'Thêm ghi chú nội bộ' : 'Thêm bình luận công khai'}</h3>
      <p className="field-hint">{draft.isInternal ? 'Nội dung nội bộ chỉ dành cho người hỗ trợ có quyền. Người yêu cầu không xem được.' : 'Người yêu cầu và người hỗ trợ có quyền đọc Ticket sẽ xem được nội dung này.'}</p>
      <FormField label="Nội dung bình luận" required error={validation || (command.error ? getFieldError(command.error, 'content') : undefined)}
        hint={`${draft.content.length.toLocaleString('vi-VN')} / 20.000 ký tự.`}>{props => <Textarea {...props} rows={5} maxLength={20_000}
          value={draft.content} disabled={command.pending} onChange={event => { onDraftChange({ ...draft, content: event.target.value }); setValidation(undefined) }} />}</FormField>
      <Button type="submit" pending={command.pending} disabled={command.disabled}>{draft.isInternal ? 'Gửi ghi chú nội bộ' : 'Gửi bình luận'}</Button>
    </> : <p>Chọn loại nội dung bạn được phép gửi.</p>}
    {command.error && <CommentError error={command.error} remaining={command.remaining} />}
    {command.uncertain && <div className="ticket-uncertain" role="alert"><h3>Chưa xác định được kết quả gửi</h3>
      <p>Bình luận có thể đã được ghi nhận. Hãy tải lại thảo luận để kiểm tra trước khi gửi lại.</p>
      <Button variant="secondary" onClick={command.allowResubmit}>Tôi đã kiểm tra, cho phép gửi lại</Button>
    </div>}
  </form>
}
function CommentError({ error, remaining }: { error: ApiError; remaining: number }) {
  return <div className="auth-message auth-message--error" role="alert"><p>{error.message}</p>
    {remaining > 0 && <p>Vui lòng chờ {remaining} giây trước khi thử lại.</p>}
    {error.traceId && <p className="field-hint">Mã tra cứu: {error.traceId}</p>}
  </div>
}
