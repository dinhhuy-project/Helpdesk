import { useEffect, useState } from 'react'
import { ApiError, getFieldError } from '../../../shared/api/api-error'
import { Button } from '../../../shared/components/button'
import { EmptyState, ErrorState, LoadingState } from '../../../shared/components/async-state'
import { FormField, Input } from '../../../shared/components/form-field'
import { Pagination } from '../../../shared/components/pagination'
import { formatDateTime } from '../../../shared/lib/date-time'
import { useAttachmentDownload, useAttachmentList, useAttachmentOptions, type AttachmentUpload } from '../hooks/use-ticket-attachments'
import { formatFileSize, sameAttachmentTarget } from '../lib/attachment-file'
import type { AttachmentOptions, AttachmentTarget } from '../types/ticket-attachment'
import type { TicketComment } from '../types/ticket-discussion'

interface AttachmentProps { id: string; comment?: TicketComment; upload: AttachmentUpload; blocked?: boolean; onDenied: () => void }
export function TicketAttachments(props: AttachmentProps) {
  const { onDenied } = props
  const query = useAttachmentOptions(props.id, props.comment?.id ?? null)
  const denied = query.error instanceof ApiError && (query.error.status === 403 || query.error.status === 404)
  useEffect(() => { if (denied) onDenied() }, [denied, onDenied])
  if (query.isPending || query.isFetching) return <LoadingState label="Đang kiểm tra quyền và giới hạn file…" />
  if (query.isError) return <ErrorState error={query.error} onRetry={() => { void query.refetch() }} />
  return <VerifiedAttachments key={`${query.data.access.canReadInternal}:${query.data.access.canUploadPublic}:${query.data.access.canUploadInternal}`}
    {...props} options={query.data} refresh={() => { void query.refetch() }} />
}
function VerifiedAttachments(props: AttachmentProps & { options: AttachmentOptions; refresh: () => void }) {
  const { id, comment, options, upload, onDenied } = props
  const [isInternal, setInternal] = useState(comment?.isInternal ?? false)
  const [page, setPage] = useState(1)
  const allowed = !isInternal || options.access.canReadInternal
  const { forgetInternal } = upload
  useEffect(() => { if (!options.access.canReadInternal) forgetInternal() }, [options.access.canReadInternal, forgetInternal])
  if (!allowed) return <p role="alert">Bạn không có quyền đọc file nội bộ.</p>
  const target = { commentId: comment?.id ?? null, isInternal }
  return <section className={`attachment-panel${isInternal ? ' is-internal' : ''}`} aria-label={comment ? 'File của bình luận' : 'File của yêu cầu'}>
    <div className="comment-section-heading"><h2>{comment ? 'File của bình luận' : 'File đính kèm'}</h2>
      <Button variant="secondary" disabled={upload.command.pending} onClick={props.refresh}>Tải lại quyền file</Button></div>
    {!comment && <div className="comment-modes" role="group" aria-label="Phạm vi file hiển thị">
      <Button variant="secondary" aria-pressed={!isInternal} disabled={upload.command.pending} onClick={() => { setInternal(false); setPage(1); upload.choose(null, target) }}>File công khai</Button>
      {options.access.canReadInternal && <Button variant="secondary" aria-pressed={isInternal} disabled={upload.command.pending} onClick={() => { setInternal(true); setPage(1); upload.choose(null, target) }}>File nội bộ</Button>}
    </div>}
    <p className="field-hint">{isInternal ? 'File nội bộ chỉ dành cho người hỗ trợ có quyền.' : 'File công khai được người yêu cầu và người hỗ trợ có quyền xem.'}
      {comment && ' File mới kế thừa phạm vi của bình luận; chỉ tác giả được đính kèm.'}</p>
    <AttachmentRows key={`${target.commentId}:${isInternal}`} id={id} target={target} page={page} setPage={setPage} onDenied={onDenied} />
    <AttachmentUploader target={target} options={options} upload={upload} blocked={props.blocked ?? false} />
  </section>
}
function AttachmentRows({ id, target, page, setPage, onDenied }: {
  id: string; target: AttachmentTarget; page: number; setPage: (page: number) => void; onDenied: () => void
}) {
  const query = useAttachmentList(id, { ...target, page, pageSize: 10, descending: true })
  const download = useAttachmentDownload(id, target, onDenied)
  const denied = query.error instanceof ApiError && (query.error.status === 403 || query.error.status === 404)
  useEffect(() => { if (denied) onDenied() }, [denied, onDenied])
  if (query.isPending || query.isFetching) return <LoadingState label="Đang tải danh sách file…" />
  if (query.isError) return <ErrorState error={query.error} onRetry={() => { void query.refetch() }} />
  return <div className="attachment-files">
    {!query.data.items.length ? <EmptyState title="Chưa có file trong phạm vi này" /> : <ul className="attachment-list">{query.data.items.map(file =>
      <li key={file.id}><div><strong>{file.fileName}</strong><p className="field-hint">{formatFileSize(file.sizeBytes)} · {file.isInternal ? 'Nội bộ' : 'Công khai'} · {formatDateTime(file.createdAt)}{file.commentId && !target.commentId ? ' · Gắn với bình luận' : ''}</p>
        <details><summary>Thông tin file</summary><dl><dt>Định dạng</dt><dd>{file.contentType}</dd><dt>Mã tham chiếu</dt><dd>{file.id}</dd></dl></details></div>
        <Button variant="secondary" aria-label={`Tải xuống ${file.fileName}`} pending={download.pending === file.id} disabled={!!download.pending || download.remaining > 0}
          onClick={() => { void download.download(file) }}>Tải xuống</Button></li>)}</ul>}
    {download.error && <ErrorState error={download.error} />}
    <Pagination page={query.data.page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={setPage} />
  </div>
}
const uploadErrors: Record<string, string> = {
  InvalidFile: 'File hoặc tên file chưa hợp lệ. Kiểm tra kích thước, nội dung và chọn lại.',
  UnsupportedFile: 'Định dạng file không được hỗ trợ.', AttachmentQuotaExceeded: 'Đã hết hạn mức file của phạm vi này, kể cả upload đang chờ xử lý.',
  UploadExpired: 'Phiên tải file đã hết hạn. Kiểm tra danh sách trước khi thực hiện yêu cầu khác.',
  StorageUnavailable: 'Dịch vụ lưu file chưa sẵn sàng. Kiểm tra danh sách để xác định kết quả trước khi thử lại.',
}
function AttachmentUploader({ target, options, upload, blocked }: { target: AttachmentTarget; options: AttachmentOptions; upload: AttachmentUpload; blocked: boolean }) {
  const own = sameAttachmentTarget(upload.selection?.target ?? null, target)
  const canUpload = target.isInternal ? options.access.canUploadInternal : options.access.canUploadPublic
  const fieldError = upload.validation ?? (upload.command.error ? getFieldError(upload.command.error, 'file') : undefined)
  return <div className="attachment-upload">
    <p className="field-hint">Tối đa {formatFileSize(options.policy.maxFileSizeBytes)}/file; {options.policy.maxFilesPerVisibility} file và {formatFileSize(options.policy.maxTotalSizeBytesPerVisibility)} cho mỗi phạm vi trên Ticket. Định dạng: {options.policy.fileTypes.map(type => type.extension).join(', ')}.</p>
    {!options.policy.enabled ? <p>Dịch vụ lưu file chưa được bật.</p> : !canUpload ? <p>Bạn hiện không được đính kèm file tại đây.</p> : <form aria-label="Tải file đính kèm" onSubmit={event => { event.preventDefault(); if (!blocked) void upload.submit(target, options) }}>
      <FormField label="Chọn file đính kèm" error={own ? fieldError : undefined} hint="Chọn một file, sau đó xác nhận tải lên. Nội dung file được kiểm tra lại tại server.">{field => <Input {...field} type="file"
        accept={options.policy.fileTypes.map(type => type.extension).join(',')} disabled={blocked || upload.command.disabled} onChange={event => {
          const file = event.target.files?.[0]; if (file) upload.choose(file, target); event.target.value = ''
        }} />}</FormField>
      {own && upload.selection && <p className="attachment-selection">Đã chọn: <strong>{upload.selection.file.name}</strong> · {formatFileSize(upload.selection.file.size)}</p>}
      <Button type="submit" pending={own && upload.command.pending} disabled={!own || blocked || upload.command.disabled}>Tải file lên</Button>
    </form>}
    {own && upload.command.pending && <div role="status"><progress max={100} value={upload.progress} aria-label="Tiến trình tải file" /><p>{upload.progress}% · {upload.progress === 100 ? 'Đã gửi file, đang xác nhận lưu…' : 'Đang gửi file…'}</p></div>}
    {upload.command.error && own && <div role="alert" className="auth-message auth-message--error"><p>{uploadErrors[upload.command.error.code ?? ''] ?? upload.command.error.message}</p>
      {upload.command.error.traceId && <p>Mã tra cứu: {upload.command.error.traceId}</p>}
      {upload.command.remaining > 0 && <p>Vui lòng chờ {upload.command.remaining} giây trước khi thử lại.</p>}</div>}
    {upload.command.uncertain && <p role="alert">Chưa xác định được kết quả tải file. File có thể đã được lưu; hãy tải lại danh sách và kiểm tra trước khi chủ động mở lại workspace. Hệ thống không tự gửi lại.</p>}
  </div>
}
