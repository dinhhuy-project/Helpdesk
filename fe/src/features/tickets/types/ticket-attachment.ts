export interface TicketAttachment {
  id: string; ticketId: string; commentId: string | null; uploadedByUserId: string
  fileName: string; contentType: string; sizeBytes: number; isInternal: boolean; createdAt: string
}
export interface AttachmentOptions {
  access: { canReadInternal: boolean; canUploadPublic: boolean; canUploadInternal: boolean }
  policy: { enabled: boolean; maxFileSizeBytes: number; maxFilesPerVisibility: number; maxTotalSizeBytesPerVisibility: number
    fileTypes: { extension: string; contentType: string }[] }
}
export interface AttachmentTarget { commentId: string | null; isInternal: boolean }
export interface AttachmentSearch extends AttachmentTarget { page: number; pageSize: number; descending: boolean }
