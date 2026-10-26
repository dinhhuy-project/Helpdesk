# Review migration AccountAdministration

Migration mới: `20261026134032_AccountAdministration`, sau `20261022150705_InitialCreate`. InitialCreate và SQL cũ giữ nguyên. Chưa áp dụng migration này vào database Helpdesk thật trong lượt triển khai code.

Artifacts để review:

- [Migration C#](../be/Infrastructure/Persistence/Migrations/20261026134032_AccountAdministration.cs).
- [SQL nâng cấp idempotent](../be/Infrastructure/Persistence/Migrations/Sql/AccountAdministration.sql).
- [SQL rollback về InitialCreate](../be/Infrastructure/Persistence/Migrations/Sql/AccountAdministration.Rollback.sql).

## Thay đổi schema và dữ liệu

1. Preflight nhóm email theo `lower(btrim(email))`; nếu trùng, dừng migration với thông báo cố định, không ghi email/credential vào lỗi. Không tự gộp, đổi hoặc xóa tài khoản.
2. Thêm `identity.users.normalized_email` là generated stored column, cùng unique index. Email gốc không bị sửa; index email gốc vẫn giữ.
3. Thêm `identity.roles.updated_at` nullable để cập nhật version của aggregate khi thay đổi grant.
4. Thêm `identity.identity_audit_logs` với FK Restrict tới actor/target user/role, jsonb metadata và indexes.
5. EF mapping thêm User.Version/Role.Version tới `xmin`. SQL thực tế không ADD/DROP system column `xmin`; Npgsql bỏ các operation này khi sinh SQL.

Tổng model hiện có 19 entity: 18 entity lõi và IdentityAuditLog. Migration không thêm password, đổi hash, sửa role/grant, revoke session, tạo admin hay cấp quyền mới. Bổ sung catalog/grant là lệnh chủ động sau nâng cấp.

Generated stored column/unique index có thể rewrite và khóa bảng users trong thời gian thực hiện. Với database đã có dữ liệu, backup trước, kiểm tra kích thước/thời gian khóa và chọn thời điểm bảo trì phù hợp. Script dùng transaction; lỗi preflight/unique/FK dừng nâng cấp, không cập nhật migration history thành công.

Có thể kiểm tra số nhóm email xung đột bằng query read-only trước:

```sql
SELECT count(*) AS duplicate_groups
FROM (
    SELECT lower(btrim(email))
    FROM identity.users
    GROUP BY lower(btrim(email))
    HAVING count(*) > 1
) AS conflicts;
```

Nếu count khác 0, operator review các tài khoản bằng công cụ quản trị được phép, xử lý nghiệp vụ rồi mới nâng cấp. Test đã xác minh trường hợp email legacy có hoa/thường/khoảng trắng được giữ nguyên và generated email chuẩn hóa đúng; dữ liệu mơ hồ bị từ chối mà không xóa user.

## Áp dụng sau khi review

Dừng API cũ/mới trong thời gian nâng cấp. Dùng đúng connection configuration cho database muốn nâng cấp; lệnh EF không cần khởi chạy API hoặc auto seeder. Ví dụ từ root, local development đọc user-secrets:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef database update AccountAdministration --project be/be.csproj
dotnet run --project be/be.csproj --no-launch-profile -- seed-identity
```

Nếu SystemAdmin role đã tồn tại từ trước, startup seed giữ nguyên grant cũ. Để operator chủ động bật khả năng quản trị mới:

```powershell
dotnet run --project be/be.csproj --no-launch-profile -- grant-account-administration
```

Lệnh này bổ sung riêng `user.manage` và `role.manage` cho SystemAdmin nếu thiếu, ghi audit và có thể chạy lặp an toàn. Đây là thay đổi đặc quyền chủ động của người có quyền vận hành CLI/database; không đặt vào startup hoặc workflow tự động. Các grant còn lại, password và UserRole không đổi. SystemAdmin mới được tạo bởi seed từ đầu nhận đủ permission mặc định, không cần lệnh bổ sung (12 mã ở phần 6; phần 7 thêm `department.manage`, xem [review mới](department-administration-review.md)).

Sau đó F5 profile **Container (Dockerfile)** và dùng [API quản trị](backend-accounts.md). Không có secret mới. Nếu chưa migration, initializer dừng startup do pending migration; tắt cờ seed không làm code mới tương thích với schema cũ.

## Rollback và kiểm chứng

Rollback SQL loại bảng audit, generated column/index và roles.updated_at; mất audit nếu đã có dữ liệu trong bảng đó. Không hoàn tác password, thay đổi role/grant hoặc status đã thực hiện qua API. Cần backup/retention phù hợp và dùng application version tương thích schema sau rollback. Không tự chạy rollback trên database đang dùng.

Suite test trên PostgreSQL riêng đã kiểm tra SQL nâng cấp chạy lặp, rollback khi database còn rỗng, nâng cấp lại, snapshot/SQL khớp migration và từ chối dữ liệu email trùng. Không sửa SQL InitialCreate để giả lập schema mới.

Tham khảo: [Npgsql xmin concurrency](https://www.npgsql.org/efcore/modeling/concurrency.html), [PostgreSQL expression indexes](https://www.postgresql.org/docs/16/indexes-expressional.html).
