import { useState } from 'react'
import { FormField, Input, Textarea } from '../../../shared/components/form-field'
import { Button } from '../../../shared/components/button'
import { getFieldError } from '../../../shared/api/api-error'
import type { useApiCommand } from '../../../shared/hooks/use-api-command'
import { CategoryParentPicker, TeamDepartmentPicker } from './catalog-lookups'
import type { CatalogKind, CatalogRecord, LookupOption, NameDraft } from '../types/catalog'

export interface CatalogEditorDraft extends NameDraft { departmentId: string | null; parentId: string | null }
export function CatalogEditor({ kind, value, command, onSave }: {
  kind: CatalogKind; value?: CatalogRecord; command: ReturnType<typeof useApiCommand>; onSave: (draft: CatalogEditorDraft) => Promise<void>
}) {
  const [name, setName] = useState(value?.name ?? '')
  const [description, setDescription] = useState(value?.description ?? '')
  const [department, setDepartment] = useState<LookupOption | null>(value && 'departmentId' in value ? { id: value.departmentId, name: value.departmentName } : null)
  const [parent, setParent] = useState<LookupOption | null>(value && 'parentId' in value && value.parentId ? { id: value.parentId, name: value.parentName || 'Danh mục cha hiện tại' } : null)
  const rules = kind === 'departments' ? 'Tên duy nhất trong toàn bộ phòng ban, kể cả đã vô hiệu hóa.'
    : kind === 'teams' ? 'Tên duy nhất trong cùng phòng ban; phòng ban của nhóm được giữ cố định sau khi tạo.'
      : 'Tên duy nhất trong cùng danh mục cha, kể cả đã vô hiệu hóa. Đổi cha sẽ chuyển cả nhánh con.'
  return <form className="catalog-editor" onSubmit={async event => {
    event.preventDefault()
    if (command.disabled || !name.trim() || (kind === 'teams' && !value && !department)) return
    await onSave({ name: name.replace(/^ +| +$/g, ''), description: description.trim() || null, departmentId: department?.id ?? null, parentId: parent?.id ?? null })
  }}>
    <p className="field-hint">{rules} So sánh không phân biệt hoa/thường và dấu cách hai đầu.</p>
    <FormField label="Tên" required hint="Tối đa 200 ký tự." error={getFieldError(command.error, 'name')}>{props => <Input {...props} value={name} maxLength={200} disabled={command.pending} onChange={event => setName(event.target.value)} />}</FormField>
    <FormField label="Mô tả" error={getFieldError(command.error, 'description')}>{props => <Textarea {...props} value={description} maxLength={2000} disabled={command.pending} onChange={event => setDescription(event.target.value)} rows={4} />}</FormField>
    {kind === 'teams' && (value && 'departmentId' in value ? <div className="form-field"><span className="account-field-label">Phòng ban</span><p>{value.departmentName}</p>
      {!value.departmentIsActive && <p className="field-hint">Phòng ban đã vô hiệu hóa; nhóm và thành viên được giữ cho lịch sử.</p>}</div>
      : <TeamDepartmentPicker value={department} disabled={command.pending} onChange={setDepartment} />)}
    {kind === 'categories' && <CategoryParentPicker value={parent} excludeId={value?.id} disabled={command.pending} onChange={setParent} />}
    <Button type="submit" disabled={command.disabled || !name.trim() || (kind === 'teams' && !value && !department)}>{command.pending ? 'Đang lưu…' : value ? 'Lưu thay đổi' : 'Tạo mới'}</Button>
  </form>
}
