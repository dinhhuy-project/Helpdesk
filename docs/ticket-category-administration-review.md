# Review migration TicketCategoryAdministration

Người dùng đã xác nhận review/apply SupportTeamAdministration (phần 8). Phần 9 tạo migration mới `20261026151829_TicketCategoryAdministration`; không chỉnh các migration đã áp dụng. SQL mới chưa được tự apply lên database Helpdesk, API không auto migrate.

## Artifacts và DDL

- [Migration C#](../be/Infrastructure/Persistence/Migrations/20261026151829_TicketCategoryAdministration.cs).
- [SQL forward idempotent](../be/Infrastructure/Persistence/Migrations/Sql/TicketCategoryAdministration.sql).
- [Rollback về SupportTeamAdministration](../be/Infrastructure/Persistence/Migrations/Sql/TicketCategoryAdministration.Rollback.sql).

Thêm generated stored `helpdesk.ticket_categories.normalized_name varchar(200)` = lower(btrim(name)), NOT NULL. Version của TicketCategory ánh xạ system xmin; Npgsql không thêm/xóa cột xmin trong SQL.

Thay index parent_id cũ bằng hai partial unique index:

- `ix_ticket_categories_normalized_name` trên normalized_name khi parent_id IS NULL: tên duy nhất ở cấp gốc.
- `ix_ticket_categories_parent_id_normalized_name` trên (parent_id,normalized_name) khi parent_id IS NOT NULL: tên duy nhất giữa các node cùng parent, hỗ trợ truy vấn con trực tiếp.

Thêm check tên không trống dấu cách và parent_id khác id. Giữ nguyên Name/Description, ParentId, cờ active, timestamp, ID và FK Ticket.CategoryId/parent Restrict. Không thêm bảng hoặc xóa/gộp/đổi tên dữ liệu. Rule API xem [backend-ticket-categories.md](backend-ticket-categories.md).

Preflight dừng transaction nếu có tên trống U+0020, trùng tên chuẩn hóa cùng parent (bao gồm NULL/root), hoặc node không nối được về root do cycle/parent lỗi. Thông báo cố định không chứa tên/ID. Cây legacy có ancestor inactive và con active vẫn được nâng cấp nguyên trạng; lookup sẽ loại nhánh mất khả dụng.

## Kiểm tra trước khi áp dụng

Chọn đúng server/database, backup theo quy trình môi trường và dừng writer/API trong lúc nâng schema. Thêm stored column, index và check cần khóa bảng; lên lịch phù hợp nếu catalog lớn. Xác nhận history có đủ bốn migration phần trước:

```sql
SELECT current_database();
SELECT migration_id FROM helpdesk.__ef_migrations_history ORDER BY migration_id;

SELECT id, parent_id, name, is_active
FROM helpdesk.ticket_categories WHERE length(btrim(name)) = 0;

SELECT parent_id, lower(btrim(name)) AS normalized_name, count(*) AS duplicates
FROM helpdesk.ticket_categories
GROUP BY parent_id, lower(btrim(name)) HAVING count(*) > 1;

WITH RECURSIVE reachable(id) AS (
    SELECT id FROM helpdesk.ticket_categories WHERE parent_id IS NULL
    UNION
    SELECT child.id FROM helpdesk.ticket_categories child
    JOIN reachable parent ON child.parent_id = parent.id
)
SELECT id, parent_id, name FROM helpdesk.ticket_categories
WHERE id NOT IN (SELECT id FROM reachable);
```

Ba query kiểm tra dữ liệu phải không có dòng. Query cuối liệt kê cả node trong cycle và những node phụ thuộc vào cycle; không tự chọn node cần cắt/gộp. Xử lý bằng quyết định nghiệp vụ, giữ các ID đang được Ticket tham chiếu; chạy lại preflight trước upgrade. API còn từ chối tên toàn whitespace khác như tab/newline.

## Áp dụng sau review

Dùng forward SQL bằng công cụ PostgreSQL dừng khi lỗi hoặc EF với cấu hình kết nối đã xác nhận. Những lệnh sau là thao tác operator sau review, không phải lệnh đã chạy vào Helpdesk trong lượt triển khai:

```powershell
dotnet ef database update TicketCategoryAdministration --project be/be.csproj
dotnet run --project be/be.csproj --no-launch-profile -- seed-identity
dotnet run --project be/be.csproj --no-launch-profile -- grant-category-administration
```

Permission mới category.manage làm catalog tăng lên 14 mã. Seed bảo toàn grant role cũ; lệnh cuối là thao tác chủ động thêm riêng quyền quản lý danh mục cho SystemAdmin nếu thiếu, có audit và lặp an toàn. Có thể cấp bằng API grant phần 6 thay thế lệnh. Startup không tự gọi lệnh grant và không reset password/user/role membership.

Kiểm tra history đủ năm migration, F5 Docker port 5000. Login admin thử tạo root → child → grandchild, reparent, phiên bản cũ, tên trùng và cycle. Dùng Requester thử lookup: branch dưới ancestor inactive không xuất hiện, API quản trị trả 403. Ticket cũ vẫn giữ CategoryId. Dữ liệu sample không được seed tự động.

## Rollback

Chỉ dùng khi migration này là bản cuối và code phụ thuộc schema mới đã dừng. SQL bỏ hai partial index/check/generated column, khôi phục index parent_id và xóa đúng history phần 9. Không xóa category/tree/Ticket, không đổi parent hoặc active; xmin là system column được giữ.

Rollback bỏ uniqueness, check self-parent và normalizedName mới; writer có thể tạo dữ liệu làm upgrade sau bị chặn. Grant/permission/audit đã ghi không tự bị gỡ khi rollback schema; quản trị theo quy trình riêng. Các constraint index không thay thế kiểm tra cycle nhiều node ở use case.

## Xác minh tự động

Model tests so snapshot và SQL EF sinh với artifacts. PostgreSQL fixture kiểm tra apply/replay/rollback/reapply trên database rỗng. Test upgrade từ phần 8 thêm cây cũ có root inactive/con active và Ticket tham chiếu trước khi chạy SQL, xác nhận dữ liệu được giữ. Các ca duplicate root, duplicate sibling, tên trống, self-parent và cycle nhiều node dừng upgrade, history giữ ở phần 8. Tất cả dùng database helpdesk_mapping_test_* trong container tạm riêng.
