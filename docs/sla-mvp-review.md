# Review SQL SlaMvp

Migration **20261028122927_SlaMvp**, sau NotificationDelivery phần 15. Model vẫn 20 entity, tổng tám migration. Người dùng đã xác nhận migrate phần 16 và read-only history local đã có SlaMvp trong [lượt rà soát tiếp theo](sla-completion-review.md). Hướng dẫn dưới tiếp tục dùng khi nâng cấp database khác; không sửa migration cũ hoặc tự nâng cấp khi startup.

- [Forward SQL idempotent](../be/Infrastructure/Persistence/Migrations/Sql/SlaMvp.sql).
- [Rollback SQL có guard](../be/Infrastructure/Persistence/Migrations/Sql/SlaMvp.Rollback.sql).
- [Migration](../be/Infrastructure/Persistence/Migrations/20261028122927_SlaMvp.cs).
- [Quy tắc SLA, API và cách khởi chạy](backend-sla.md).

## Schema và dữ liệu

SlaPolicy thêm mapping Version/xmin để optimistic concurrency. Npgsql dùng system column có sẵn, **SQL không tạo hoặc drop cột xmin thật**. Thêm unique partial `ix_sla_policies_priority ON helpdesk.sla_policies(priority) WHERE is_active`, check tên trim không trống và first_response_minutes <= resolution_minutes <= 525600; check target dương/priority đã có được giữ.

TicketSla thêm sáu cột nullable: policy_name (varchar 200), priority (varchar 20), first_response_minutes, resolution_minutes, first_response_breached_at và resolution_breached_at (timestamptz). Check snapshot yêu cầu bốn field tên/priority/targets **cùng null hoặc cùng hợp lệ**. Legacy được giữ cả bốn null, không lấy policy hiện tại để backfill lịch sử.

Hai index pending mới lọc marker chưa ghi và response/resolution chưa hoàn thành (`ix_ticket_slas_first_response_due_at1`, `ix_ticket_slas_resolution_due_at1`); các index deadline cũ giữ nguyên để tránh đổi cấu trúc lịch sử ngoài phạm vi migration này. Cần đo query/backlog khi dữ liệu lớn để hợp nhất/index theo query thực tế ở phần vận hành. PK/FK RESTRICT hiện có giữ nguyên, không thêm bảng, không sửa Ticket/comment/file/Identity grants.

SQL chỉ DDL/history, không tạo policy, snapshot, notification hoặc activity. Các Ticket chưa có SLA không được backfill. TicketSla cũ có deadline vẫn có thể được worker xử lý và gửi notification sau khi code mới chạy; xác nhận quy tắc nghiệp vụ trước khi bật worker trên môi trường có dữ liệu SLA legacy.

## Preflight bắt buộc

Backup DB và dừng API/worker theo quy trình triển khai. Xác nhận database và history trước:

```sql
SELECT current_database();
SELECT migration_id FROM helpdesk.__ef_migrations_history ORDER BY migration_id;

-- Phải không còn nhiều policy active cho một priority.
SELECT priority, count(*)
FROM helpdesk.sla_policies WHERE is_active
GROUP BY priority HAVING count(*) > 1;

-- Phải xử lý rõ từng bản ghi không đạt constraint, không tự xóa/đổi target.
SELECT id, priority, first_response_minutes, resolution_minutes
FROM helpdesk.sla_policies
WHERE length(btrim(name)) = 0 OR first_response_minutes <= 0
   OR first_response_minutes > resolution_minutes OR resolution_minutes > 525600;

SELECT count(*) AS legacy_snapshots FROM helpdesk.ticket_slas;

-- Kiểm tra dữ liệu SLA legacy có đồng bộ trạng thái resolve với Ticket không.
SELECT s.ticket_id, s.resolved_at AS sla_resolved_at, t.resolved_at AS ticket_resolved_at
FROM helpdesk.ticket_slas s JOIN helpdesk.tickets t ON t.id = s.ticket_id
WHERE s.resolved_at IS DISTINCT FROM t.resolved_at;
```

Nếu có duplicate active, operator quyết định policy giữ active và deactivate phần còn lại bằng kế hoạch riêng; không để migration tự chọn theo CreatedAt/ID. Nếu target cũ quá giới hạn hoặc response > resolution, cần review mục tiêu; không đổi deadline Ticket đã áp dụng. DDL transaction sẽ thất bại và rollback nếu dữ liệu không thỏa unique/check, thay vì sửa im lặng.

Tài khoản migration cần ALTER/CREATE INDEX trên các bảng SLA. Index/check có thể scan/lock bảng, đây không phải nâng cấp online không khóa. Chọn maintenance window phù hợp kích thước dữ liệu.

## Apply và xác minh

Sau review, dùng công cụ PostgreSQL của môi trường với dừng ngay khi lỗi. Ví dụ khi đã cấu hình PGHOST/PGPORT/PGDATABASE/PGUSER và password an toàn:

```powershell
psql --set ON_ERROR_STOP=1 --file be/Infrastructure/Persistence/Migrations/Sql/SlaMvp.sql
```

Forward có transaction và history guard, chạy lặp không thêm cột/index lần nữa. Không áp dụng các migration tiền nhiệm thay operator, không đặt password trong lệnh/log.

```sql
SELECT migration_id FROM helpdesk.__ef_migrations_history ORDER BY migration_id;
SELECT indexname FROM pg_indexes WHERE schemaname = 'helpdesk'
  AND tablename IN ('sla_policies', 'ticket_slas') ORDER BY indexname;
```

Kỳ vọng history có SlaMvp. F5 Container port 5000; worker tự chạy, không cần secret/grant mới. **Cần tạo policy active qua API để Ticket mới có snapshot**. Có thể tạm đặt Sla__WorkerEnabled=false khi muốn kiểm tra SLA legacy trước; cờ này không tắt side effects của command. Các mẫu Development cũ không tự có SLA mới.

## Rollback

Dừng tất cả API/worker phần 16. Rollback chặn bằng RAISE EXCEPTION khi còn snapshot mới (policy_name khác null) hoặc bất kỳ breach marker; không được vứt dữ liệu snapshot/lịch sử để quay về code cũ.

```sql
SELECT count(*) AS rollback_blocking_rows
FROM helpdesk.ticket_slas
WHERE policy_name IS NOT NULL OR first_response_breached_at IS NOT NULL
   OR resolution_breached_at IS NOT NULL;
```

Nếu còn dữ liệu: ưu tiên sửa tiến; nếu bắt buộc rollback cần kế hoạch archive/restore được operator duyệt. Không tự đặt field null hoặc xóa snapshot để vượt guard. Nếu không có dữ liệu chặn và đã xác nhận backup/code tương thích, rollback chỉ gỡ indexes/check/sáu field mới và history SlaMvp; giữ TicketSla cũ, deadline, FirstRespondedAt, ResolvedAt, policies, activity và notifications. xmin là system column nên không bị xóa.

Rollback SQL không idempotent, chỉ dùng khi SlaMvp là migration mới nhất. Các activity SLA vẫn là lịch sử; code trước phần 16 chưa biết action sẽ ẩn chúng theo whitelist. Không rollback tiếp NotificationDelivery khi còn managed notification; guard phần 15 vẫn áp dụng.

## Kiểm chứng

Model tests so snapshot/SQL với EF Core 8; fixture PostgreSQL apply → replay → rollback khi rỗng → apply lại. Test có snapshot mới xác nhận rollback P0001 và history/dữ liệu còn nguyên. Toàn bộ thao tác migration test dùng DB tạm, không chạy trên database Helpdesk thật.
