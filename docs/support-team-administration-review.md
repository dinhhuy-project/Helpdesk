# Review migration SupportTeamAdministration

Người dùng đã xác nhận review/apply phần 7. Phần 8 tạo migration mới `20261026145925_SupportTeamAdministration`; không chỉnh ba migration đã áp dụng. Migration mới chưa được tự apply vào database Helpdesk; API startup không chạy Migrate/EnsureCreated.

## Artifacts và DDL

- [Migration C#](../be/Infrastructure/Persistence/Migrations/20261026145925_SupportTeamAdministration.cs).
- [SQL forward idempotent](../be/Infrastructure/Persistence/Migrations/Sql/SupportTeamAdministration.sql).
- [SQL rollback về DepartmentAdministration](../be/Infrastructure/Persistence/Migrations/Sql/SupportTeamAdministration.Rollback.sql).

Thêm `organization.support_teams.normalized_name varchar(200)` generated stored `lower(btrim(name))`, NOT NULL. Thay index đơn department_id bằng unique composite `(department_id,normalized_name)`; prefix department_id vẫn phục vụ lọc theo phòng ban. Thêm check `length(btrim(name)) > 0`. Tên giữ duy nhất trong từng phòng ban kể cả nhóm inactive.

Thêm `organization.support_team_members.is_leader boolean NOT NULL DEFAULT FALSE` và `updated_at timestamptz NULL`. Mọi member cũ giữ IsActive/JoinedAt và mặc định không phải leader; không tự suy từ role TeamLeader. Team/Member có Version ánh xạ system column xmin; Npgsql không thêm/xóa cột xmin trong SQL.

Không đổi DepartmentId của nhóm, user/member FK, Ticket assignee, trạng thái, JoinedAt hoặc timestamp cũ; không thêm bảng hoặc hard delete. API giữ DepartmentId cố định sau create. Rule/scope cụ thể xem [backend-support-teams.md](backend-support-teams.md).

## Kiểm tra dữ liệu trước upgrade

Xác nhận đúng server/database và history có đủ InitialCreate, AccountAdministration, DepartmentAdministration. Backup theo quy trình môi trường; dừng writer/API khi nâng schema. Generated stored column và tạo index/check có khóa bảng, cần lịch phù hợp với dữ liệu lớn.

```sql
SELECT current_database();
SELECT migration_id FROM helpdesk.__ef_migrations_history ORDER BY migration_id;

SELECT id, department_id, name, is_active
FROM organization.support_teams WHERE length(btrim(name)) = 0;

SELECT department_id, lower(btrim(name)) AS normalized_name, count(*) AS duplicates
FROM organization.support_teams
GROUP BY department_id, lower(btrim(name)) HAVING count(*) > 1;
```

Hai query cuối phải không có dòng; nhóm cùng tên ở hai phòng ban khác nhau được phép. Preflight trong migration chặn tên trống dấu cách hoặc trùng normalized name trong cùng phòng ban với thông báo cố định không echo tên. Không tự gộp/đổi tên hoặc di chuyển dữ liệu. Nếu lỗi, transaction rollback và history giữ ở phần 7. API còn từ chối tên toàn whitespace khác như tab/newline.

## Áp dụng sau review

Chạy forward SQL bằng công cụ PostgreSQL dừng khi lỗi, hoặc EF với cấu hình kết nối đã xác nhận. Đây là lệnh để operator chạy sau review, không phải lệnh đã chạy vào Helpdesk trong lượt triển khai:

```powershell
dotnet ef database update SupportTeamAdministration --project be/be.csproj
dotnet run --project be/be.csproj --no-launch-profile -- seed-identity
# Tùy chọn bật quản trị thành viên cho role TeamLeader có sẵn:
dotnet run --project be/be.csproj --no-launch-profile -- grant-team-leadership
```

Catalog vẫn 13 permission. Default **TeamLeader mới** nhận thêm team.manage; role đã có giữ grant cũ. Lệnh grant là thao tác chủ động, chỉ thêm team.manage vào TeamLeader, ghi audit nếu thay đổi và không tự gán IsLeader/membership. API grant phần 6 có thể thay thế lệnh này. SystemAdmin vẫn phải có team.manage để quản trị toàn cục. Startup không cấp lại grant bị gỡ và không tự chạy lệnh grant.

Xác nhận history đủ bốn migration; F5 Docker port 5000, thử create team → add member → cấp leadership → dùng JWT leader kiểm tra scope. Group/member version stale trả 409; gán user inactive/nhóm hoặc phòng ban inactive bị từ chối. Xác nhận deactivate/remove giữ Ticket assignment và phiên.

## Rollback

Chỉ rollback khi migration này đang là bản cuối, đã dừng code phụ thuộc schema mới và review dữ liệu. SQL bỏ composite index/check/generated name, khôi phục index department_id, bỏ is_leader/updated_at của member và xóa đúng history phần 8. Không xóa team/member/Ticket hoặc FK; xmin là cột hệ thống được giữ nguyên.

**Rollback mất dữ liệu IsLeader và UpdatedAt của membership.** Xuất/lưu các thông tin này nếu cần khôi phục; upgrade lại mặc định IsLeader=false, không tự khôi phục leader. Rollback cũng bỏ uniqueness mới nên writer có thể tạo tên trùng làm lần upgrade sau bị chặn. Permission/grant/audit đã ghi không tự bị gỡ khi rollback schema; quản trị chúng theo quy trình riêng.

## Xác minh tự động

Model test so snapshot/SQL EF với artifacts forward/rollback. Fixture PostgreSQL kiểm tra apply/replay/rollback/reapply trên database rỗng riêng. Test upgrade từ phần 7 tạo nhóm/phòng ban inactive, member có JoinedAt cũ và Ticket có assigned team/agent trước khi nâng; xác nhận giữ các liên kết và không tự cấp leadership. Tên trùng cùng phòng ban hoặc trống chặn upgrade, tên giống nhau khác phòng ban vẫn hợp lệ. Tất cả chạy trên database test có prefix `helpdesk_mapping_test_*` trong PostgreSQL tạm.
