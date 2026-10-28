# Review SQL NotificationDelivery

Migration `20261028115316_NotificationDelivery` đi sau `20261026171234_AttachmentStorage`, phục vụ phần 15. Model vẫn 20 entity, tổng bảy migration. **Chưa apply lên database Helpdesk đang dùng**, không sửa migration cũ hoặc tự migrate khi API startup.

- [Forward SQL idempotent](../be/Infrastructure/Persistence/Migrations/Sql/NotificationDelivery.sql).
- [Rollback SQL có guard](../be/Infrastructure/Persistence/Migrations/Sql/NotificationDelivery.Rollback.sql).
- [Migration](../be/Infrastructure/Persistence/Migrations/20261028115316_NotificationDelivery.cs).
- [Contract và quyết định outbox](backend-notifications.md).

## Thay đổi và tương thích dữ liệu cũ

Thêm bốn cột nullable vào `communication.notifications`: ticket_activity_id UUID, dispatch_claim_id UUID, dispatch_lease_until timestamptz, dispatched_at timestamptz. Không có default/backfill, không sửa Title/Content/ReadAt của notification cũ, không đổi Ticket/comment/file/Identity hoặc grant.

FK ticket_activity_id tới helpdesk.ticket_activities(id), ON DELETE RESTRICT; index FK riêng; unique partial `(user_id,ticket_activity_id) WHERE ticket_activity_id IS NOT NULL` giữ một notification/người/activity. Index pending `(dispatch_lease_until,created_at) WHERE ticket_activity_id IS NOT NULL AND dispatched_at IS NULL` phục vụ claim; index user/time/unread hiện có giữ nguyên. Không tạo table hoặc xmin mới.

Legacy rows có ticket_activity_id NULL được giữ nguyên nhưng không xuất API hoặc gửi realtime, vì không xác minh được visibility. Không tự suy internal/public từ text. Không backfill notifications cho activity cũ. FK đơn chưa ép TicketId trùng Ticket của activity; ứng dụng kiểm tra cả hai và whitelist action, hàng không hợp lệ bị ẩn. SQL writer ngoài ứng dụng phải giữ quan hệ/visibility bất biến và theo cùng transaction/lock; không thay Action hoặc TicketId của lịch sử để vượt ACL.

DDL chạy trong transaction; thêm FK/index thường có thể khóa/quét bảng notifications hiện có. Cân nhắc kích thước bảng và thời gian triển khai trước khi chạy; đây không phải migration online không khóa. Không tự xóa dữ liệu để xử lý xung đột. Unique index mới không xung đột với legacy vì cột mới toàn NULL.

## Trước apply

Dừng API/worker cũ trong thời gian nâng cấp theo quy trình môi trường, backup DB và xác nhận đúng connection. Các SQL dưới chỉ kiểm tra schema/history, không chứa credentials:

```sql
SELECT current_database();
SELECT migration_id FROM helpdesk.__ef_migrations_history ORDER BY migration_id;
SELECT count(*) AS existing_notifications FROM communication.notifications;
```

History phải có AttachmentStorage; nếu chưa, review/apply [phần 14](attachment-storage-review.md) trước. Tài khoản migration cần ALTER/INDEX trên communication.notifications và REFERENCES trên helpdesk.ticket_activities. SQL forward có history guard nhưng **không áp dụng các migration tiền nhiệm thay operator**.

Sau review, dùng công cụ PostgreSQL đang quản lý môi trường, với dừng ngay khi lỗi. Nếu đã thiết lập PGHOST/PGPORT/PGDATABASE/PGUSER và cơ chế password an toàn:

```powershell
psql --set ON_ERROR_STOP=1 --file be/Infrastructure/Persistence/Migrations/Sql/NotificationDelivery.sql
```

Không đặt password trong lệnh/log, không chạy nhầm bằng connection test. Script có START TRANSACTION/COMMIT và history guard để chạy lại không thêm cột/index lần nữa.

## Sau apply

```sql
SELECT migration_id FROM helpdesk.__ef_migrations_history ORDER BY migration_id;
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema = 'communication' AND table_name = 'notifications'
ORDER BY ordinal_position;
SELECT count(*) AS pending_dispatch
FROM communication.notifications
WHERE ticket_activity_id IS NOT NULL AND dispatched_at IS NULL;
```

Kỳ vọng history có NotificationDelivery và bốn cột mới nullable. F5 Container giữ port 5000, seed Identity/admin/Development giữ quy tắc cũ; không reset dữ liệu mẫu. Không cần permission mới hoặc cài MinIO lại.

Smoke test với hai user đủ quyền: user A thao tác Ticket → user B GET notification → connect hub/watch Ticket → A thêm comment → B nhận NotificationChanged/TicketChanged và refetch REST → B mark-read, unread count giảm. Với internal note, requester không nhận hàng hoặc event; thu hồi membership thì API ẩn notification cũ và dispatcher ngừng gửi ID tương ứng.

## Rollback

Dừng mọi API/worker phần 15 trước khi rollback. Script có guard chặn nếu còn bất kỳ hàng `ticket_activity_id IS NOT NULL`, kể cả đã đọc/đã dispatch: dropping cột sẽ làm mất liên kết visibility và trạng thái delivery. **Drain dispatch không đủ điều kiện rollback**.

```sql
SELECT count(*) AS managed_notifications
FROM communication.notifications
WHERE ticket_activity_id IS NOT NULL;
```

Nếu count > 0: ưu tiên giữ schema và sửa tiến; nếu bắt buộc rollback, cần kế hoạch archive/restore notification được operator phê duyệt. Không tự xóa notification hoặc đặt activity_id NULL để qua guard. Khi không còn managed rows và đã xác nhận backup/code tương thích, rollback chỉ gỡ FK/index/bốn cột và history của NotificationDelivery; legacy rows không bị xóa. SQL rollback không idempotent, chỉ dùng khi đây là migration mới nhất. Không rollback tiếp AttachmentStorage nếu còn journal upload.

## Kiểm chứng tự động

Model tests đối chiếu snapshot và cả hai SQL với EF Core 8. PostgreSQL fixture apply → replay → rollback khi rỗng → apply lại; integration test tạo managed notification, xác nhận downgrade bị P0001 và history/dữ liệu còn nguyên. Không thực hiện các thao tác này trên DB Helpdesk thật.
