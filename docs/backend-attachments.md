# File đính kèm MinIO

Phần 14 bổ sung file trên [Ticket](backend-tickets.md) và [comment/internal note](backend-ticket-comments.md). Giữ .NET/EF Core/Npgsql 8.x, dùng [MinIO SDK 6.0.5](https://www.nuget.org/packages/Minio/6.0.5). Catalog vẫn 17 permission; dùng ticket.comment.public/internal, không cần grant mới. Có entity vận hành AttachmentUploadIntent, tổng 20 entity, và [migration AttachmentStorage cần review/apply](attachment-storage-review.md). Không tự migrate hoặc cấu hình bucket thật khi API khởi động.

## API

Mọi route yêu cầu Bearer, account active và session hợp lệ. Response no-store.

| API | Kết quả |
| --- | --- |
| `POST /api/v1/tickets/{ticketId}/attachments` | multipart/form-data, 201 metadata + Location |
| `GET /api/v1/tickets/{ticketId}/attachments` | Danh sách metadata trong ACL, phân trang |
| `GET /api/v1/tickets/{ticketId}/attachments/{attachmentId}` | Metadata file đọc được |
| `GET /api/v1/tickets/{ticketId}/attachments/{attachmentId}/download` | Bytes qua API sau kiểm tra ACL; không redirect/presign |

POST nhận đúng **một file** tên field `file`, cùng `commentId` và `isInternal` tùy chọn. Không nhận bucket/objectKey/uploader/sizeBytes/version hoặc field khác, không nhận field trùng hoặc nhiều file. UploadedByUserId từ phiên; object key là attachments/<GUID> do server sinh, không dùng tên file làm path. Chưa mở sửa/xóa/move file hoặc gắn lại file vào comment khác. Thêm file vào Ticket không sửa Version/xmin, UpdatedAt, status hoặc SLA của Ticket.

Metadata DTO gồm id/ticketId/commentId/uploadedByUserId/fileName/contentType/sizeBytes/isInternal/createdAt. Không trả bucket, object key, credential hoặc URL storage. Timestamp UTC precision microsecond. File tạo mới là bất biến; mỗi POST hợp lệ là một bản mới, chưa có idempotency key. Không tự retry POST sau mất kết nối khi chưa biết kết quả.

## ACL Ticket và comment

- **Đọc public:** cùng read scope Ticket. Không cần permission ghi file/comment.
- **Ghi public:** read scope + ticket.comment.public, Ticket chưa Closed/Cancelled.
- **Đọc/ghi internal:** cùng ACL internal note: read scope + ticket.comment.internal, không phải RequesterId của Ticket, và read.all hoặc membership/team/department active ở AssignedTeam. Ghi còn yêu cầu Ticket chưa terminal. Requester nhiều role/admin vẫn không được xem internal file của chính Ticket.
- Không gửi commentId: file gắn trực tiếp Ticket, isInternal mặc định false.
- Có commentId: comment phải đọc được và thuộc đúng Ticket, visibility lấy từ comment. Nếu gửi isInternal khác comment thì 400; không cho biến file của internal note thành public. Chỉ author của comment được thêm file vào comment đó; kể cả admin muốn đính kèm trên comment người khác cũng không bypass. Có thể gắn file trực tiếp Ticket nếu có scope/permission tương ứng.
- Author/uploader cũ mất scope không giữ quyền download. File lịch sử trên Closed/Cancelled vẫn đọc được theo ACL, không upload mới. CommentId sai Ticket hoặc nội bộ bị che trả 404; thiếu quyền ghi trên resource đọc được trả 403.

Metadata query lọc Ticket scope + internal visibility **trước count/page**. File legacy có flag khác visibility của comment bị ẩn với mọi role; operator cần review/sửa dữ liệu trước khi sử dụng. Composite FK đã có từ InitialCreate bảo đảm CommentId/TicketId cùng parent. Không tin ID uploader, header hoặc role do client khai.

GET list nhận page/pageSize (mặc định 1/20, tối đa 1.000.000/100), descending (mặc định false), isInternal và commentId tùy chọn. Sort CreatedAt rồi Id. isInternal=true trên requester chỉ nhận count=0/items rỗng. Filter commentId không đọc được trả 404. Count/items là query đọc riêng, không cam kết snapshot chung khi upload đồng thời. ID file dưới Ticket khác, file không tồn tại hoặc ngoài scope cùng trả 404.

Download kiểm tra metadata/ACL trước storage I/O, đọc vào buffer tạm có giới hạn rồi **kiểm tra lại ACL** trước response; grant/membership/session bị thu hồi trong lúc đọc không được dùng để trả file. File dùng Content-Disposition: attachment, X-Content-Type-Options: nosniff, CSP sandbox; không hỗ trợ inline preview, presigned URL hoặc byte range ở bước này. FileStream tạm được xóa khi response/exception đóng stream. Backend không giữ database transaction trong lúc đọc object.

## Định dạng và hạn mức

| Loại | MIME lưu bởi server | Kiểm tra nội dung |
| --- | --- | --- |
| .txt | text/plain | UTF-8 hợp lệ, không có NUL/control trừ CR/LF/tab |
| .pdf | application/pdf | Bắt đầu bằng %PDF- |
| .png | image/png | PNG signature |
| .jpg/.jpeg | image/jpeg | JPEG signature |

Extension và MIME phải khớp allowlist; không tin Content-Type đơn lẻ. Tên tối đa 255 ký tự, không path separator/drive/control/ký tự filename nguy hiểm, không bắt đầu dấu chấm, không chứa `..`, không khoảng trắng/dấu chấm cuối tên. File rỗng bị từ chối. Kiểm tra magic/UTF-8 là validation định dạng, **chưa có antivirus hoặc phân tích toàn bộ PDF/image**; file luôn tải xuống dưới dạng attachment. Các định dạng khác cần thêm validation/tests trước khi mở.

Mặc định file tối đa **10 MiB**, 20 file và tổng 100 MiB **cho mỗi Ticket/visibility**. Pending reservations cũng tính quota để không vượt hạn mức khi upload đồng thời. Tách ngân sách public/internal để hạn mức public không tiết lộ tổng file nội bộ. Có thể cấu hình giảm MaxFileSizeMiB (1–10), MaxFilesPerVisibility (1–100), MaxTotalSizeMiBPerVisibility (1–500).

HTTP body hard limit 12 MiB, multipart field nhỏ/tối đa 8; file thực tế vẫn phải nằm dưới cấu hình. Rate limit mặc định 10 upload/phút theo IP, queue=0, 429 + Retry-After; cấu hình Minio:UploadsPerMinute (1–1000). Nếu dùng reverse proxy, cần cấu hình trusted forwarded headers ở bước vận hành trước khi dùng client IP từ proxy. Buffer/validator kiểm tra số byte thực, không chỉ size khai báo; dùng file tạm DeleteOnClose, không giữ cả file trong RAM.

400 cho file/path/signature/size/input sai; 415 UnsupportedFile cho extension/MIME ngoài allowlist; 409 AttachmentQuotaExceeded hoặc UploadExpired; 401/403/404 theo ACL; 503 StorageUnavailable khi disabled/config/quyền storage/policy/versioning/network chưa phù hợp. Lỗi dùng ProblemDetails/traceId, không trả raw exception, key, credential hoặc nội dung file. Request quá hard limit có thể trả 413 từ HTTP server/proxy.

## Upload, transaction và object mồ côi

1. Validate metadata và buffer nội dung có giới hạn; không gọi MinIO cho file không hợp lệ.
2. Bắt đầu thời hạn tổng hai phút cho reservation/Put/finalize. Mở transaction ngắn, advisory lock 734205001 → user → Ticket, kiểm tra session/read/write/comment/author và quota. Insert AttachmentUploadIntent với bucket/key/metadata do server tạo, lease 15 phút, rồi commit.
3. Put object ngoài transaction, có CancellationToken/deadline. Không phát metadata ra API khi chưa finalize.
4. Transaction mới lấy cùng khóa, reload intent và Ticket; kiểm tra lại lease/claim/session/grant/scope/status/comment. Insert TicketAttachment + activity và xóa intent trong **cùng transaction**. Không dùng quyền đã kiểm tra trước I/O như quyền vĩnh viễn.

Lỗi Put, mất quyền, timeout, lỗi metadata commit hoặc crash giữ lại intent bền vững, kể cả Put đã tạo object nhưng caller chưa biết kết quả. Không xóa object ngay trong catch vì kết quả commit có thể chưa rõ. Worker khi Minio.Enabled/CleanupEnabled=true chạy mỗi phút, nhận tối đa 10 intent hết hạn trong transaction và đánh dấu IsDeleting=true/đặt thời điểm retry sau 5 phút. Claim loại object đã có metadata; finalize không còn được chấp nhận khi claim hoặc hết lease.

Worker commit claim **trước I/O**, abort incomplete multipart và xóa object, sau đó xóa intent. Delete lỗi giữ intent và retry; một item lỗi không chặn các item khác. Crash sau delete trước xóa intent được retry idempotent. Mỗi object do API tạo đều có journal trước Put; worker không quét/xóa object tùy ý ngoài journal, không xóa object đã commit metadata hoặc bucket khác. Deadline upload ngắn hơn lease để không có Put đang chạy đến lúc cleanup bình thường bắt đầu. Không đổi bucket/credential phạm vi khi còn intent chưa dọn; không xóa thủ công journal để giải phóng quota.

Metadata và hoạt động được rollback cùng nhau nếu save/commit lỗi. Action PublicAttachmentAdded/InternalAttachmentAdded theo visibility, payload chỉ AttachmentId/CommentId/IsInternal. Timeline dùng cùng internal ACL, không trả tên file, key hoặc bytes; requester không nhận internal event/count/ID. [Phần 15](backend-notifications.md) thêm notification trong cùng transaction, worker chỉ phát ID sau commit và kiểm tra lại ACL.

## Bucket và cấu hình

Bucket dành riêng cho attachments phải **private, không có bucket policy và chưa bật versioning**. SDK kiểm tra policy/versioning trước I/O và từ chối bucket public/versioned/suspended; không tự sửa bucket hiện có. IAM policy của application user/service account là riêng, không đặt làm bucket policy. Ứng dụng không trả URL storage để vượt qua ACL đã thay đổi.

MinioOptions mặc định Enabled=false; các API cũ vẫn có thể chạy khi chưa bật storage, upload/download trả 503. Sau khi apply migration đã review, thiết lập:

| Key | Giá trị local/ý nghĩa |
| --- | --- |
| Minio:Enabled | true sau setup |
| Minio:Endpoint | localhost:9000 trên host, minio:9000 trong Compose; không gồm scheme/path |
| Minio:HostOverride | profile Container đặt host.docker.internal, giữ port từ Endpoint |
| Minio:AccessKey / SecretKey | App credential qua user-secrets/environment |
| Minio:Bucket | helpdesk-attachments |
| Minio:UseSsl | false local; true khi endpoint đã có TLS, khai đúng port |
| Minio:CleanupEnabled | true để worker retry journal |

Với `.env` đã có MINIO_ENABLED=true và app credentials, từ root chỉ cần:

```powershell
docker compose up -d --build --wait postgres redis minio
```

Image MinIO chứa cả server và client build từ source cố định phiên bản. Entrypoint chạy server, chờ S3 local sẵn sàng rồi tạo/kiểm tra bucket, app user và IAM policy trước khi ghi readiness marker. Healthcheck yêu cầu cả marker và endpoint live; --wait chỉ thành công sau bootstrap. Lần build đầu hoặc sau xóa images cần mạng và có thể mất vài phút. Config mc nằm trong thư mục tạm riêng với umask 077, được xóa sau bootstrap; không lưu root vào user-secrets.

Bootstrap tạo bucket nếu thiếu, kiểm tra không có bucket policy/versioning, tạo app user nếu thiếu và gán IAM policy helpdesk-attachments-app từ [attachments-policy.json](../deploy/minio/attachments-policy.json). Quyền app chỉ trên bucket helpdesk-attachments/prefix attachments/*, không có CreateBucket/PutBucketPolicy/admin. Chạy lặp giữ credentials .env/password user/bucket settings; cập nhật nội dung policy có tên riêng theo file trong repo. Existing user có secret không khớp làm setup thất bại, không tự reset password. Bucket public/versioned làm setup dừng, không tự thay policy/versioning. MINIO_ENABLED=false thì bỏ qua bootstrap attachment.

Giữ `.env` sau khi dọn Docker: xóa containers/images/volumes rồi chạy lại cùng lệnh sẽ tự tạo cấu hình MinIO từ các credentials đó; không cần chạy minio-init hoặc PowerShell setup lần nữa. Restart cũng xóa readiness marker cũ và kiểm tra lại. PID 1 chuyển tiếp tín hiệu stop và chờ server thoát; bootstrap lỗi không báo healthy. Dữ liệu file đã xóa cùng volume không được khôi phục bởi bootstrap.

Máy mới chưa có app credentials có thể chạy `& ./be/scripts/Initialize-ComposeStorage.ps1` một lần để sinh key trong .env, bật storage và gọi Compose. Service minio-init chỉ còn là công cụ quản trị tùy chọn trong profile storage-setup; API chờ trực tiếp MinIO healthy. Đồng bộ cấu hình MinIO sang user-secrets cho profile Container khi cấu hình thay đổi:

```powershell
& ./be/scripts/Initialize-DevelopmentStorage.ps1 -UseComposeConfiguration
```

Switch UseComposeConfiguration đồng bộ đúng các key Minio từ .env, gồm Enabled, Endpoint localhost với MINIO_API_PORT, app credential và defaults local; giữ nguyên JWT/Identity/các user-secrets khác. Không có switch thì script tiếp tục chỉ bổ sung key còn thiếu, không overwrite cấu hình đã có. F5 Container tiếp tục port 5000, dùng host override host.docker.internal; Compose API dùng minio:9000. Không commit .env/user-secrets.

Kiểm tra credentials và policy sau setup (tạo một object thử tên UUID trong attachments/, đọc rồi xóa đúng object đó):

```powershell
docker compose exec -T minio /bin/sh /opt/helpdesk/verify.sh
```

Kiểm tra còn xác nhận bucket private/unversioned, upload ngoài prefix và thao tác admin bị chặn. Không dùng file/Ticket thật cho probe; không đổi dữ liệu PostgreSQL. Không chia sẻ output của docker compose config không có --quiet, docker inspect, mc debug hoặc dotnet user-secrets list vì có thể lộ credentials.

Lệnh ba service chỉ khởi tạo hạ tầng. Nếu xóa volume PostgreSQL, database Helpdesk tạo lại sẽ rỗng: cần áp dụng lại các SQL migration đã review trước F5/seed Identity. Bootstrap MinIO không tự chạy migrations hoặc tạo lại Ticket/user/file đã mất.

Nếu không dùng Compose để khởi tạo, CLI initialize-storage và switch Initialize-DevelopmentStorage.ps1 -BootstrapBucket vẫn chỉ tạo/kiểm tra bucket bằng bootstrap credentials, không tạo app user. Tạo app user/policy riêng trước khi bật storage trong môi trường đó.

## Kiểm chứng và bước tiếp theo

```powershell
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
& ./be/tests/Run-ComposeBootstrapTests.ps1
```

Script tạo PostgreSQL + MinIO tên GUID, credential ngẫu nhiên, port ngẫu nhiên và không mount volume; cleanup container trong finally. Các test kiểm tra SDK MinIO thật, anonymous bị chặn, bucket public bị từ chối, HTTP upload/download thật, file signatures/UTF-8, strict multipart, ACL và visibility comment, quyền đổi giữa I/O, quota pending, metadata/activity rollback, GC claim chặn finalize và retry. Fixture review/apply/replay/rollback SQL chỉ trong database tạm; rollback journal không rỗng bị chặn. CI build MinIO từ deploy/minio rồi chạy suite, không dùng secrets/database Helpdesk thật.

Run-ComposeBootstrapTests dùng project GUID, ports ngẫu nhiên và volumes test riêng; kiểm tra đúng luồng up --build --wait với ba service, restart và xóa/tạo lại volumes test bằng cùng credentials. Mỗi giai đoạn kiểm tra app upload/download/delete và giới hạn quyền. Cleanup xác minh tên/nhãn project trước khi xóa volumes, không dùng .env hoặc volumes ứng dụng. CI Docker job cũng chạy kiểm tra này.

Kết quả kiểm chứng phần 14: build Release với -warnaserror đạt 0 warning/error; toàn bộ **432/432 tests đạt, 0 skipped** trên PostgreSQL/MinIO tạm, cùng smoke test CLI seed/grant. Lượt cấu hình Compose sau đó đã build/run minio-init trên MinIO local, xác nhận upload/download/delete bằng app user và chặn quyền ngoài prefix/admin. Database/migration vẫn là thao tác riêng; chưa chạy workflow trên GitHub.

Lượt [rà soát hoàn thành phần 14](attachment-completion-review.md) chạy lại nhóm attachment/MinIO/model: 36/36 đạt, 0 skipped. [Phần 15](backend-notifications.md) đã bổ sung notification theo visibility activity khi metadata file commit; không notify khi mới reserve/upload object hoặc khi finalize rollback. Cần [review/apply NotificationDelivery](notification-delivery-review.md).

Hạ tầng MinIO Community local giữ bản source đã pin trong repo; xem [backend-setup.md](backend-setup.md) về phạm vi triển khai. Readiness storage, AV, TLS, backup và quan sát cảnh báo cleanup cần hoàn thiện trước vận hành theo phần 18. Bước tiếp theo: 16 — SLA MVP.
