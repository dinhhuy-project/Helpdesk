# Frontend bước 10 — File đính kèm

Ticket detail có panel **File đính kèm** với danh sách phân trang, upload và download qua API có Bearer. Mỗi bình luận đã lưu có nút **File của bình luận** để mở danh sách và đính kèm đúng comment. Giữ .NET/EF/Npgsql 8.x và các thư viện frontend hiện có; không có migration, permission, package hoặc biến môi trường mới.

## Contract và capability

Frontend dùng các API [file hiện có](backend-attachments.md): POST multipart, GET list, GET metadata và GET download. Bổ sung một API đọc để nhận cấu hình và quyền hiện tại:

```text
GET /api/v1/tickets/{ticketId}/attachments/options?commentId={optional-guid}
```

Response gồm `access: {canReadInternal,canUploadPublic,canUploadInternal}` và `policy: {enabled,maxFileSizeBytes,maxFilesPerVisibility,maxTotalSizeBytesPerVisibility,fileTypes:[{extension,contentType}]}`. Phạm vi Ticket và comment được kiểm tra trước khi trả kết quả; comment không đọc được/sai Ticket trả 404. Capability của comment chỉ cho tác giả upload và chỉ đúng visibility của comment. Requester của Ticket không đọc nội bộ kể cả có role/grant internal. Ticket Closed/Cancelled đọc lịch sử được nhưng không upload. Khi storage disabled, metadata vẫn đọc được, capability upload là false.

Policy lấy từ chính validator và MinioOptions của backend; không hardcode giới hạn mặc định trong form. Chỉ công khai cấu hình giới hạn, không trả bucket, endpoint, object key, credential hoặc số file nội bộ đang chiếm quota. Capability là hỗ trợ UX; backend tiếp tục kiểm tra lại tại reserve/finalize/download. Không đổi các quy tắc ACL hoặc quota đang có.

## Upload

- Tạo Ticket trước, mở detail rồi chọn file. Với comment: gửi nội dung, chờ comment được tạo, sau đó mở **File của bình luận**. File upload lỗi không tạo lại Ticket/comment đã thành công.
- Chọn một file cho mỗi request; danh sách định dạng, kích thước mỗi file và quota từng visibility lấy từ policy. Frontend kiểm tra tên/extension/MIME/size để báo sớm; backend vẫn kiểm tra signature/UTF-8 và quota cả metadata lẫn reservation đang chờ. Nếu hệ điều hành không cung cấp MIME, dùng MIME ứng với extension đã được policy cho phép.
- File gắn Ticket gửi đúng `file` và `isInternal`. File gắn comment gửi đúng `file` và `commentId`, không gửi `isInternal` để server kế thừa. Không gửi author/uploader, Version, bucket/object key hoặc các field tạo Ticket/comment.
- Axios gửi FormData, browser đặt multipart boundary. Thanh tiến trình là số byte đã gửi; 100% hiển thị **đang xác nhận lưu**, chỉ báo thành công khi nhận receipt metadata hợp lệ. Timeout request là 150 giây để phù hợp cửa sổ storage 2 phút; không tự retry upload khi network/timeout/5xx hoặc kết quả chưa xác định.
- File draft, tiến trình và trạng thái command sống ở boundary Ticket, giữ qua revalidation. Một upload tại một thời điểm; khóa thay đổi workflow/gửi comment trong lúc upload. File/Blob/FormData không vào Query mutation cache, Zustand, URL hoặc browser storage. Logout/rời Ticket hủy request và chặn response về muộn.
- 400/409 quota/413/415 giữ file đã chọn và thông báo/traceId. 429 tôn trọng Retry-After. Timeout/network/5xx khóa gửi trong workspace vì file có thể đã lưu; tải lại danh sách để đối chiếu trước khi chủ động mở lại workspace. Không tự gửi POST lần nữa. Backend có journal cleanup object mồ côi; frontend không gọi xóa object.

## Danh sách, visibility và cache

Danh sách dùng page/pageSize=10/descending=true, phân trang phía server. Panel Ticket hiển thị file trực tiếp và file của comment trong cùng visibility; file của comment có nhãn liên kết. Dialog comment truyền thêm commentId và cố định visibility. Dialog chỉ mở mới gọi API, không tạo một query cho từng comment trên trang.

Mặc định chỉ tải public (`isInternal=false`). Chỉ khi server cho phép và người dùng chủ động chọn **File nội bộ** mới gọi query internal. Query keys chứa session/scope, Ticket, commentId, visibility và page; không giữ trang cũ khi đổi trang/phạm vi. Capability refetch ẩn/unmount query riêng tư (`gcTime=0`); mất internal sẽ bỏ metadata và file draft nội bộ. API client từ chối response sai Ticket/comment/visibility trước khi cache và chỉ lấy các field public của DTO.

Upload thành công đánh dấu list file và activity của Ticket stale, refetch theo scope hiện tại. Lỗi đọc sau commit không biến thành lỗi POST; nếu mất quyền đọc vẫn giữ thông báo upload đã thành công rồi ẩn Ticket cũ. 403/404 khi đọc/upload/download kích hoạt kiểm tra lại resource/access, không dùng quyền cũ để tiếp tục hiển thị file. Không dùng tên role, uploader hoặc assignee để suy quyền đọc.

## Download

Mỗi lần tải xuống đọc metadata mới rồi gọi `/download` với Bearer qua typed client. Bytes được nhận bằng `responseType=arraybuffer`, tạo Blob theo metadata và kiểm tra kích thước đầy đủ. Sau khi xác nhận component/session/scope còn hiện hành mới tạo object URL và click download; URL và anchor được giải phóng trong finally. Blob/object URL không vào Query cache. Không có preview nội dung hoặc link MinIO/presigned/public bucket.

ProblemDetails dạng binary được giải mã có giới hạn 64 KiB để giữ status/code/validation/traceId và cơ chế refresh 401. Response lỗi không được lưu như file; 429 download có cooldown. Request download về muộn sau logout hoặc mất internal access không tạo object URL.

## Chạy và kiểm tra

Restart API Visual Studio **Container (Dockerfile)** để nạp endpoint options. Chạy `npm run dev` từ `fe/`, mở Ticket detail. Giữ cấu hình MinIO private đã có; không cần migrate/seed lại/sửa `.env`. Nếu storage đang tắt hoặc chưa sẵn sàng, xem [setup MinIO](backend-attachments.md); không đặt credential MinIO trong `VITE_*`.

```powershell
cd fe
npm run lint
npm test
npm run build
cd ..
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~AttachmentOptions|FullyQualifiedName~TicketAttachment|FullyQualifiedName~AttachmentValidation|FullyQualifiedName~AttachmentStorageIntegration|FullyQualifiedName~FrontendAttachmentJourney|FullyQualifiedName~TicketWorkflowRead|FullyQualifiedName~TicketMutation|FullyQualifiedName~FrontendWorkflowJourney'
dotnet build be/be.sln --no-restore -c Release -warnaserror
```

`AttachmentOptionsTests` kiểm tra limits từ config, capability theo author/visibility/terminal/grant/membership và storage disabled. `FrontendAttachmentJourneyTests` chạy TypeScript client/auth thật qua HTTP tới application, PostgreSQL và MinIO tạm: tạo Ticket, upload public, upload vào comment/note, metadata/download đúng bytes, cấm người khác upload comment, che internal với requester và chặn outsider. Binary 404 phải giữ ProblemDetails/traceId. CI job backend hiện có chạy các test mới, không cần secret hoặc job mới.

Browser mode tùy chọn dùng các biến `HELPDESK_PLAYWRIGHT_MODULE`, `HELPDESK_FRONTEND_URL=http://localhost:5173`, `HELPDESK_BROWSER_CHANNEL`, `HELPDESK_BROWSER_ARTIFACTS` như [bước 9](frontend-ticket-workflow.md). Khởi động Vite rồi chạy filter `FrontendAttachmentJourney`. Mọi HTTP được forward tới backend/PostgreSQL/MinIO test; credential fixture qua stdin, không chạy trên dữ liệu Helpdesk cá nhân. CI mặc định chỉ chạy nhánh client/API.

Nghiệm thu local 29/10/2026: toàn bộ frontend **365 tests/27 files** đạt, gồm 32 bài attachment mới; ESLint, TypeScript/Vite và build backend Release `-warnaserror` đạt. **87 backend tests** attachment/options/validation/MinIO/workflow cùng CLI smoke checks đạt, không skip; client journey MinIO thật đạt riêng, browser mode Edge cũng đạt. Browser kiểm tra upload Ticket/comment/internal note, read-only trên comment người khác, download đúng bytes UTF-8, API có Bearer, requester không thấy internal và viewport 390px không tràn ngang/không lỗi console. Không chạy toàn bộ suite backend trong lần này.

Tiếp theo: bước 11 — SLA trên Ticket và quản trị policy.
