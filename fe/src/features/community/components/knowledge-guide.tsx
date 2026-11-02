import { Button } from '../../../shared/components/button'
import { FormField, Input, Textarea } from '../../../shared/components/form-field'
import type { KnowledgeGuide } from '../api/community-api'

export function KnowledgeGuideFields({ guide, disabled, onChange, onNumberSteps }: { guide: KnowledgeGuide; disabled: boolean; onChange: (value: KnowledgeGuide) => void; onNumberSteps: () => void }) {
  return <fieldset className="knowledge-guide-fields" disabled={disabled}><legend>Chuẩn hóa hướng dẫn</legend>
    <p className="field-hint">Có thể lưu bản nháp chưa đầy đủ. Khi xuất bản cần đủ các trường dưới đây và 1–20 bước đánh số liên tục từ 1.</p>
    <FormField label="Phạm vi áp dụng" hint="Đối tượng, dịch vụ, hệ điều hành/phiên bản và trường hợp không áp dụng.">{props => <Textarea {...props} maxLength={2000} value={guide.applicability} onChange={event => onChange({ ...guide, applicability: event.target.value })} />}</FormField>
    <FormField label="Điều kiện trước khi thực hiện" hint="Quyền truy cập, thiết bị và thông tin cần chuẩn bị; ghi rõ nếu không có điều kiện riêng.">{props => <Textarea {...props} maxLength={2000} value={guide.prerequisites} onChange={event => onChange({ ...guide, prerequisites: event.target.value })} />}</FormField>
    <Button type="button" variant="secondary" onClick={onNumberSteps}>Đánh số từng dòng nội dung thành các bước</Button>
    <p className="field-hint">Hãy đọc và biên tập lại sau khi đánh số; thao tác này chỉ định dạng văn bản.</p>
    <FormField label="Kết quả cần đạt">{props => <Textarea {...props} maxLength={2000} value={guide.expectedOutcome} onChange={event => onChange({ ...guide, expectedOutcome: event.target.value })} />}</FormField>
    <FormField label="Khi nào cần gửi Ticket" hint="Dấu hiệu phải dừng tự xử lý, cách yêu cầu hỗ trợ và thông tin an toàn cần cung cấp.">{props => <Textarea {...props} maxLength={2000} value={guide.escalation} onChange={event => onChange({ ...guide, escalation: event.target.value })} />}</FormField>
    <FormField label="Nguồn tài liệu / đơn vị biên soạn">{props => <Input {...props} maxLength={500} value={guide.sourceName} onChange={event => onChange({ ...guide, sourceName: event.target.value })} />}</FormField>
    <FormField label="Liên kết nguồn được phép công khai" hint="Không bắt buộc. Chỉ HTTPS; không nhập URL wiki nội bộ hoặc liên kết chứa token.">{props => <Input {...props} type="url" maxLength={1000} value={guide.sourceUrl ?? ''} onChange={event => onChange({ ...guide, sourceUrl: event.target.value || null })} />}</FormField>
    <FormField label="Ngày cập nhật tại nguồn" hint="Để trống nếu không biết. Không nhập ngày trong tương lai (theo UTC); ngày rà soát bài được server ghi khi xuất bản.">{props => <Input {...props} type="date" min="1900-01-01" value={guide.sourceUpdatedOn ?? ''} onChange={event => onChange({ ...guide, sourceUpdatedOn: event.target.value || null })} />}</FormField>
  </fieldset>
}
export function KnowledgeGuideContext({ guide }: { guide: KnowledgeGuide }) {
  return <section className="community-card"><h2>Trước khi thực hiện</h2><h3>Phạm vi áp dụng</h3><p className="community-content">{guide.applicability}</p>
    <h3>Điều kiện cần chuẩn bị</h3><p className="community-content">{guide.prerequisites}</p></section>
}
export function KnowledgeGuideOutcome({ guide }: { guide: KnowledgeGuide }) {
  return <section className="community-card"><h2>Kết quả cần đạt</h2><p className="community-content">{guide.expectedOutcome}</p>
    <h2>Khi nào cần gửi Ticket</h2><p className="community-content">{guide.escalation}</p>
    <h2>Nguồn tài liệu</h2><p>{guide.sourceName}</p>{guide.sourceUrl && <p><a href={guide.sourceUrl} target="_blank" rel="noopener noreferrer">Mở nguồn tài liệu (tab mới)</a></p>}
    <p className="field-hint">Ngày cập nhật tại nguồn: {guide.sourceUpdatedOn ? guide.sourceUpdatedOn.split('-').reverse().join('/') : 'Chưa có thông tin'}</p>
  </section>
}
