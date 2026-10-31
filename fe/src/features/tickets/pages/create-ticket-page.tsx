import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../../auth/hooks/use-auth'
import { TicketKnowledgeSuggestions } from '../../community/components/ticket-knowledge'
import { hasPermissions, readTicketRule } from '../../auth/lib/permissions'
import { getFieldError } from '../../../shared/api/api-error'
import { Button } from '../../../shared/components/button'
import { FormField, Input, Select, Textarea } from '../../../shared/components/form-field'
import { PageTitle } from '../../../shared/components/page-title'
import { TicketCategoryPicker } from '../components/ticket-category-picker'
import { useCreateTicket } from '../hooks/use-create-ticket'
import { validateTicketDraft } from '../lib/create-ticket'
import type { TicketDraftErrors } from '../lib/create-ticket'
import { formatTicketNumber } from '../lib/ticket-search'
import { TicketPriority, ticketPriorityLabels } from '../types/ticket-enums'
import type { TicketCreated } from '../types/ticket'
import type { SelectedTicketCategory } from '../types/ticket-category'

export function CreateTicketPage() {
  const { scope } = useAuth()
  return <CreateTicketContent key={scope} />
}

function CreateTicketContent() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [receipt, setReceipt] = useState<TicketCreated | null>(null)
  function created(value: TicketCreated) {
    if (hasPermissions(user?.permissions ?? [], readTicketRule)) {
      void navigate(`/tickets/${value.id}`, { replace: true })
    } else setReceipt(value)
  }
  return <section className="ticket-page ticket-create-page"><PageTitle title={receipt ? 'Tạo yêu cầu thành công' : 'Tạo yêu cầu'} />
    {receipt ? <div className="ticket-card" role="status"><h1>Tạo yêu cầu thành công</h1>
      <p>Yêu cầu {formatTicketNumber(receipt.ticketNumber)} đã được ghi nhận.</p>
      <p>Tài khoản hiện chưa có quyền đọc Ticket. Quyền tạo không tự cấp quyền xem chi tiết.</p>
      <div className="ticket-filter-actions"><Button onClick={() => setReceipt(null)}>Tạo yêu cầu khác</Button><Link to="/account">Về tài khoản</Link></div>
    </div> : <><header className="ticket-page-heading"><p className="eyebrow">Hỗ trợ</p><h1>Tạo yêu cầu</h1>
      <p>Gửi yêu cầu cho chính bạn. Mã yêu cầu sẽ được cấp sau khi tạo thành công.</p></header>
      <CreateTicketForm onCreated={created} /></>}
  </section>
}

function CreateTicketForm({ onCreated }: { onCreated: (receipt: TicketCreated) => void }) {
  const { user } = useAuth()
  const canRead = hasPermissions(user?.permissions ?? [], readTicketRule)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [priority, setPriority] = useState<string>(String(TicketPriority.Normal))
  const [category, setCategory] = useState<SelectedTicketCategory | null>(null)
  const [validation, setValidation] = useState<TicketDraftErrors>({})
  const form = useCreateTicket({ onCreated, onInvalidCategory: () => {
    setCategory(null)
    setValidation(previous => ({ ...previous, categoryId: 'Danh mục không còn khả dụng. Vui lòng chọn lại.' }))
  } })
  const fieldError = (field: keyof TicketDraftErrors) => validation[field] || (form.error ? getFieldError(form.error, field) : undefined)
  return <form className="ticket-card ticket-create-form" aria-label="Tạo yêu cầu hỗ trợ" noValidate onSubmit={event => {
    event.preventDefault()
    if (form.disabled) return
    const result = validateTicketDraft({ title, description, categoryId: category?.category.id ?? '', priority })
    if (result.errors) { setValidation(result.errors); return }
    setValidation({})
    void form.submit(result.value)
  }}>
    <FormField label="Tiêu đề" required error={fieldError('title')} hint="Tối đa 300 ký tự.">{props => <Input {...props}
      name="title" maxLength={300} value={title} onChange={event => {
        setTitle(event.target.value); setValidation(previous => ({ ...previous, title: undefined }))
      }} disabled={form.pending} />}</FormField>
    <FormField label="Nội dung yêu cầu" required error={fieldError('description')} hint={`${description.length.toLocaleString('vi-VN')} / 20.000 ký tự.`}>{props => <Textarea {...props}
      name="description" rows={7} maxLength={20_000} value={description} onChange={event => {
        setDescription(event.target.value); setValidation(previous => ({ ...previous, description: undefined }))
      }} disabled={form.pending} />}</FormField>
    <FormField label="Độ ưu tiên" required error={fieldError('priority')}>{props => <Select {...props} name="priority" value={priority}
      onChange={event => { setPriority(event.target.value); setValidation(previous => ({ ...previous, priority: undefined })) }} disabled={form.pending}>
      {Object.entries(ticketPriorityLabels).map(([id, label]) => <option key={id} value={id}>{label}</option>)}
    </Select>}</FormField>
    <TicketCategoryPicker value={category} disabled={form.pending} error={fieldError('categoryId')} onChange={value => {
      setCategory(value); setValidation(previous => ({ ...previous, categoryId: undefined }))
    }} />
    {form.error && <div className="auth-message auth-message--error" role="alert"><p>{form.error.message}</p>
      {form.error.traceId && <p className="field-hint">Mã tra cứu: {form.error.traceId}</p>}
      {form.remaining > 0 && <p>Vui lòng chờ {form.remaining} giây trước khi thử lại.</p>}
    </div>}
    {form.uncertain && <div className="ticket-uncertain" role="alert"><h2>Chưa xác định được kết quả tạo</h2>
      <p>Yêu cầu có thể đã được ghi nhận. Hãy kiểm tra {canRead ? 'danh sách yêu cầu' : 'với bộ phận hỗ trợ'} trước khi gửi lại để tránh tạo trùng.</p>
      <div className="ticket-filter-actions">{canRead && <Link to="/tickets">Kiểm tra danh sách yêu cầu</Link>}
        <Button variant="secondary" onClick={form.allowResubmit}>Tôi đã kiểm tra, cho phép gửi lại</Button></div>
    </div>}
    <TicketKnowledgeSuggestions title={title} />
    <div className="ticket-create-actions"><Button type="submit" pending={form.pending} disabled={form.disabled}>Gửi yêu cầu</Button>
      <Link to={canRead ? '/tickets' : '/account'}>{canRead ? 'Về danh sách yêu cầu' : 'Về tài khoản'}</Link></div>
  </form>
}
