import type { AttachmentOptions, AttachmentTarget } from '../types/ticket-attachment'

export function formatFileSize(bytes: number) {
  return bytes < 1024 ? `${bytes} B` : bytes < 1024 * 1024 ? `${(bytes / 1024).toLocaleString('vi-VN', { maximumFractionDigits: 1 })} KiB`
    : `${(bytes / (1024 * 1024)).toLocaleString('vi-VN', { maximumFractionDigits: 1 })} MiB`
}
export function attachmentType(file: File, policy: AttachmentOptions['policy']) {
  return policy.fileTypes.find(type => file.name.toLowerCase().endsWith(type.extension))?.contentType
}
export function validateAttachment(file: File, policy: AttachmentOptions['policy']): string | undefined {
  if (!file.size || file.size > policy.maxFileSizeBytes) return `File phải có nội dung và không vượt quá ${formatFileSize(policy.maxFileSizeBytes)}.`
  if (!file.name.trim() || file.name !== file.name.trim() || file.name.length > 255 || file.name.startsWith('.') || file.name.endsWith('.') ||
    file.name.includes('..') || /[<>:"/\\|?*]/.test(file.name) || Array.from(file.name).some(character => {
      const code = character.charCodeAt(0); return code < 32 || (code >= 127 && code <= 159)
    })) return 'Tên file không hợp lệ. Hãy đổi tên rồi chọn lại.'
  const type = attachmentType(file, policy)
  if (!type || (file.type && file.type.toLowerCase().split(';')[0] !== type)) return 'Định dạng file không được hỗ trợ hoặc không khớp phần mở rộng.'
}
export function sameAttachmentTarget(a: AttachmentTarget | null, b: AttachmentTarget) {
  return a !== null && a.commentId === b.commentId && a.isInternal === b.isInternal
}
export function saveAttachmentBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  try {
    anchor.href = url; anchor.download = fileName; anchor.hidden = true
    document.body.append(anchor); anchor.click()
  } finally {
    anchor.remove(); URL.revokeObjectURL(url)
  }
}
