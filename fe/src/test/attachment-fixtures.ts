import type { AttachmentOptions } from '../features/tickets/types/ticket-attachment'

export const noAttachmentUpload: AttachmentOptions = {
  access: { canReadInternal: false, canUploadPublic: false, canUploadInternal: false },
  policy: { enabled: false, maxFileSizeBytes: 10485760, maxFilesPerVisibility: 20, maxTotalSizeBytesPerVisibility: 104857600,
    fileTypes: [{ extension: '.txt', contentType: 'text/plain' }] },
}
