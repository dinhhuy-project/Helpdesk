# Review migration DepartmentAdministration

Người dùng đã xác nhận review SQL và nâng cấp database sau phần 6. Phần 7 tạo migration mới `20261026143539_DepartmentAdministration`; không sửa InitialCreate/AccountAdministration đã áp dụng. SQL mới được chuẩn bị để operator review trước khi áp dụng lên Helpdesk. Không tự nâng cấp database thật hoặc migrate lúc API startup.

## Thay đổi DDL

- Thêm `organization.departments.normalized_name varchar(200)` generated stored `lower(btrim(name))`, NOT NULL.
- Unique index `ix_departments_normalized_name` áp dụng cho cả active/inactive.
- Check `ck_departments_name_not_blank`: `length(btrim(name)) > 0`.
- Department.Version dùng system column `xmin`; Npgsql không tạo/xóa cột xmin trong SQL.
- Không sửa Name, Description, IsActive, timestamp, user/team FK hoặc dữ liệu phiên. Không thêm bảng mới, không thay DeleteBehavior.Restrict.
- Preflight chặn tên trống U+0020 hoặc trùng normalizedName bằng thông báo cố định, không echo tên. Nếu lỗi, transaction rollback; operator phải xử lý dữ liệu theo nghiệp vụ trước khi chạy lại. API còn từ chối tên toàn whitespace (tab/newline...).

Artifacts:

- [Migration C#](../be/Infrastructure/Persistence/Migrations/20261026143539_DepartmentAdministration.cs)
- [SQL forward idempotent](../be/Infrastructure/Persistence/Migrations/Sql/DepartmentAdministration.sql)
- [SQL rollback về AccountAdministration](../be/Infrastructure/Persistence/Migrations/Sql/DepartmentAdministration.Rollback.sql)

## Kiểm tra trước khi áp dụng

Chọn đúng server/database, backup theo quy trình của môi trường, tạm dừng các writer/API trong lúc nâng schema. Generated stored column và tạo unique index cần khóa bảng; lên lịch phù hợp nếu departments đã lớn. Kiểm tra history có InitialCreate và AccountAdministration:

```sql
SELECT current_database();
SELECT migration_id FROM helpdesk.__ef_migrations_history ORDER BY migration_id;

SELECT id, name, is_active
FROM organization.departments
WHERE length(btrim(name)) = 0;

SELECT lower(btrim(name)) AS normalized_name, count(*) AS duplicates
FROM organization.departments
GROUP BY lower(btrim(name)) HAVING count(*) > 1;
```

Hai query cuối phải không có dòng. Tên trùng ở phòng ban inactive vẫn cần xử lý rõ ràng; không tự gộp/xóa hoặc thay user/team FK. Quy tắc chi tiết xem [API phòng ban](backend-departments.md).

## Áp dụng sau review

Chạy forward SQL bằng công cụ PostgreSQL có dừng khi lỗi, hoặc dùng EF với cấu hình kết nối đã xác nhận. Lệnh dưới là thao tác operator sau review, không phải bước đã chạy lên Helpdesk trong lượt triển khai:

```powershell
dotnet ef database update DepartmentAdministration --project be/be.csproj
dotnet run --project be/be.csproj --no-launch-profile -- seed-identity
dotnet run --project be/be.csproj --no-launch-profile -- grant-department-administration
```

Seed giữ grant cũ. Lệnh grant riêng chủ động cấp `department.manage` cho SystemAdmin cũ, không đổi password/role user hoặc grant khác; startup không tự gọi. Nếu đã cấp qua API quản trị grant thì có thể bỏ lệnh grant.

Kiểm tra history đủ ba migration, F5 profile Docker port 5000, login và thử tạo/list/update/deactivate; xem lookup chỉ active, gán mới user vào inactive phải bị từ chối, user/team cũ vẫn còn liên kết. Thử version cũ và tên trùng nhận 409.

## Rollback

Chỉ dùng rollback khi DepartmentAdministration là migration cuối cùng, đã dừng code phụ thuộc schema mới và đã review ảnh hưởng. SQL bỏ index/check/generated column và xóa đúng history của migration này. Dữ liệu phòng ban, user/team và liên kết không bị xóa; xmin giữ nguyên. Rollback không tự gỡ permission/grant hoặc audit đã ghi; quản trị các dữ liệu đó qua quy trình riêng. Nếu tiếp tục ghi tên trùng sau rollback thì lần upgrade sau sẽ bị preflight chặn.

## Xác minh tự động

Model test so sánh snapshot và forward/rollback với SQL EF sinh ra. PostgreSQL fixture kiểm tra apply/replay/rollback/reapply trên schema test; test upgrade riêng thêm phòng ban inactive có user/team trước khi chạy SQL, xác nhận dữ liệu được giữ. Dữ liệu legacy trùng/trống làm upgrade dừng, history vẫn ở phần 6. Tất cả chạy trên database `helpdesk_mapping_test_*` trong container riêng.
