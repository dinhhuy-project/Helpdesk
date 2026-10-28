# Review SQL AttachmentStorage

Migration mới: `20261026171234_AttachmentStorage`, sau TicketCategoryAdministration. Code phần 14 đã tạo migration và SQL; **chưa áp dụng vào database helpdesk đang dùng**. Không thay migration đã áp dụng, không chạy migrate khi startup. Phần 12–13 không có schema riêng, nên database đã apply phần 9 và cấp quyền workflow vẫn đi từ migration đó lên bước này.

Artifacts:

- [Forward SQL idempotent](../be/Infrastructure/Persistence/Migrations/Sql/AttachmentStorage.sql).
- [Rollback SQL](../be/Infrastructure/Persistence/Migrations/Sql/AttachmentStorage.Rollback.sql).
- [Migration](../be/Infrastructure/Persistence/Migrations/20261026171234_AttachmentStorage.cs).
- [API và MinIO setup](backend-attachments.md).

## Thay đổi schema và dữ liệu

Chỉ tạo bảng `helpdesk.attachment_upload_intents` và các index/FK/check tương ứng. Không đổi ticket_attachments, ticket_comments, Ticket/Identity hoặc metadata/file có sẵn; không sửa role/grant. Model tăng từ 19 lên 20 entity, tổng sáu migration.

Intent có Id, TicketId, CommentId?, UploadedByUserId, Bucket/ObjectKey, FileName/ContentType/SizeBytes, IsInternal, CreatedAt/ExpiresAt và IsDeleting. Đây là journal/quota vận hành, không được trả qua API. FK Ticket/user/comment đều RESTRICT; composite comment_id/ticket_id giữ đúng Ticket. Check size 1–10 MiB; expires_at > created_at. Unique (bucket,object_key); index expires_at phục vụ worker, (ticket_id,is_internal) phục vụ quota, thêm index cho FK. Không có backfill dữ liệu cũ hoặc sequence/xmin mới.

Không có constraint chéo bảng về visibility hoặc tổng quota; ứng dụng kiểm tra trong transaction với advisory lock chung 734205001 và reload sau I/O. SQL writer ngoài ứng dụng phải theo cùng quy tắc, không đổi comment visibility hoặc ghi public file cho internal comment.

## Trước khi apply

Backup database/metadata và MinIO theo quy trình môi trường. Xác nhận đúng database, đúng migration đã apply và SQL đã review; không chạy script dưới đây bằng tài khoản/connection của database test.

```sql
SELECT current_database();
SELECT migration_id FROM helpdesk.__ef_migrations_history ORDER BY migration_id;

-- Nếu có metadata legacy: visibility sai sẽ bị ẩn, cần xử lý thủ công, không tự xóa/gộp.
SELECT a.id, a.ticket_id, a.comment_id, a.is_internal, c.is_internal AS comment_is_internal
FROM helpdesk.ticket_attachments a
JOIN helpdesk.ticket_comments c ON c.id = a.comment_id AND c.ticket_id = a.ticket_id
WHERE a.is_internal <> c.is_internal;
```

Review quyền CREATE trong schema helpdesk và REFERENCES trên bảng liên quan; giữ bucket hiện có private/unversioned, chuẩn bị app credentials giới hạn phạm vi. Bảng mới rỗng nên không quét/chuyển đổi bảng Ticket lớn; DDL/FK vẫn lấy lock trong transaction, chọn thời điểm nâng cấp phù hợp. Không chạy EF application mới có startup seed khi database còn migration pending; seeder sẽ dừng như các phần trước.

Sau khi review, operator áp dụng **forward SQL** bằng công cụ PostgreSQL đang dùng, với dừng ngay khi lỗi. Ví dụ CLI nếu đã cấu hình PGHOST/PGPORT/PGDATABASE/PGUSER và cơ chế mật khẩu an toàn:

```powershell
psql --set ON_ERROR_STOP=1 --file be/Infrastructure/Persistence/Migrations/Sql/AttachmentStorage.sql
```

Không đặt password vào câu lệnh chia sẻ/log. Script có transaction và history guard, lặp lại không tạo thêm bảng/index. Không tự xác nhận migration thành công chỉ vì API bật: kiểm tra history/bảng trước.

## Sau khi apply

```sql
SELECT migration_id FROM helpdesk.__ef_migrations_history ORDER BY migration_id;
SELECT count(*) AS pending_uploads FROM helpdesk.attachment_upload_intents;
```

Kỳ vọng có AttachmentStorage trong history và bảng journal mới rỗng trước upload. Bật integration theo [setup storage](backend-attachments.md); thử public file, internal file từ member, requester bị 404 khi đoán internal ID, kiểm tra anonymous MinIO không đọc được object. Metadata/API không trả object key hoặc root credential. Không cần grant mới.

## Rollback

Dừng nhận upload mới và để worker dọn hết intent (bao gồm intent IsDeleting đang retry). **Không tắt worker trước khi drain**, không xóa journal để vượt điều kiện. Nếu storage mất kết nối/credential không còn phù hợp, khôi phục để cleanup hoàn tất trước; không chạy delete bucket/volume.

```sql
SELECT count(*) AS must_be_zero FROM helpdesk.attachment_upload_intents;
```

Rollback SQL có guard RAISE EXCEPTION nếu bảng còn bất kỳ intent; transaction không drop bảng/history trong trường hợp đó. Khi journal rỗng và đã dừng mọi API/worker phần 14, rollback drop bảng/index/FK journal và xóa đúng history của AttachmentStorage, không xóa metadata attachments đã commit hoặc object MinIO. Deploy lại code/model phần trước sau rollback. SQL rollback không idempotent, chỉ dùng khi migration này đang là mới nhất.

Metadata và object đã commit không được dọn bởi rollback vì là lịch sử có thể dùng khi bật lại storage. Nếu cần retention/xóa thật, thiết kế quy trình riêng. Không rollback khi một instance cũ phần 14 vẫn có thể reserve/finalize.

## Kiểm chứng trong lượt triển khai

Model tests đối chiếu snapshot + SQL forward/rollback với EF Core 8; fixture PostgreSQL tạm apply/replay/rollback khi rỗng rồi apply lại. Integration test giữ intent và xác nhận downgrade bị P0001, history và journal còn nguyên. Test MinIO/PostgreSQL dùng container riêng; chưa review/apply thay operator trên database Helpdesk thật.
