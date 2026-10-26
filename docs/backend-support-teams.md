# Nhóm hỗ trợ và thành viên

Phần 8 triển khai API SupportTeam/membership với `team.manage` và kiểm tra phạm vi theo từng nhóm. Giữ .NET 8, EF/Npgsql 8.x, không có package/secret mới. Phần 7 đã được người dùng xác nhận review/apply; code này cần [SupportTeamAdministration](support-team-administration-review.md) trước khi F5.

## Quyền và phạm vi trưởng nhóm

Mọi endpoint bên dưới yêu cầu user active, phiên hợp lệ và permission `team.manage` hiện tại từ database.

| Người gọi | Phạm vi |
| --- | --- |
| SystemAdmin có `team.manage` | Đọc/quản trị mọi nhóm, kể cả nhóm/phòng ban inactive; tạo nhóm, đổi trạng thái nhóm, chỉ định/gỡ trưởng nhóm |
| Người có `team.manage` và membership `IsActive && IsLeader` | Chỉ đọc/sửa tên/mô tả và quản lý **thành viên thường** của đúng nhóm đang active trong phòng ban active |
| Chỉ có role TeamLeader, chỉ có `team.manage` hoặc chỉ có membership | Không tự có phạm vi quản trị nhóm |

`IsLeader` lưu trên từng SupportTeamMember: một người có thể làm trưởng nhóm A, thành viên thường nhóm B và không thuộc nhóm C. Không suy ra quyền trưởng nhóm từ tên role toàn hệ thống. Ngoài SystemAdmin, người chỉ có `team.manage` mà chưa được chỉ định trưởng nhóm nhận danh sách rỗng; detail/members/ghi vào nhóm ngoài phạm vi trả 404. Scope được áp dụng **trước count và phân trang**.

Chỉ SystemAdmin có `team.manage` được gọi endpoint leadership. Để chỉ định trưởng nhóm, target phải là user active, membership active và đã có grant `team.manage`; nhóm/phòng ban phải active. Người được chỉ định không bắt buộc mang tên role TeamLeader: permission và membership quyết định quyền. Role TeamLeader mặc định mới được seed có `team.manage`, nhưng grant cũ vẫn được bảo toàn.

Trưởng nhóm được thêm/deactivate/reactivate/gỡ thành viên thường, không tự chỉnh quyền trưởng nhóm, không deactivate/reactivate chính membership leader của mình hoặc của leader khác. SystemAdmin có thể chỉ định nhiều trưởng nhóm hoặc không có trưởng nhóm; không có guard "leader cuối cùng" vì SystemAdmin vẫn có đường quản trị. Membership/leadership không cấp UserRole/RolePermission và không bao gồm `user.manage`, `role.manage` hay `department.manage`.

Thiếu/sai/thu hồi JWT hoặc user inactive trả 401; thiếu `team.manage` hoặc thao tác chỉ dành cho SystemAdmin trả 403. Gỡ grant, demote/deactivate membership, deactivate nhóm/phòng ban có hiệu lực ở request tiếp theo với cùng JWT. Luồng ghi kiểm tra lại phiên/quyền/scope bên trong transaction.

## API

Base route: `/api/v1/support-teams`.

| Method và route | Thành công | Phạm vi bổ sung |
| --- | --- | --- |
| `GET /` | 200, danh sách phân trang | Theo scope người gọi |
| `GET /{id}` | 200, chi tiết | Theo scope người gọi |
| `POST /` | 201 + Location nhóm | Chỉ SystemAdmin |
| `PATCH /{id}` | 200, chi tiết/version mới | Admin hoặc leader của nhóm |
| `PUT /{id}/status` | 200, chi tiết/version mới | Chỉ SystemAdmin |
| `GET /{id}/members` | 200, membership phân trang | Theo scope nhóm cha |
| `POST /{id}/members` | 201 + Location danh sách members | Admin hoặc leader; chỉ thêm thành viên thường |
| `PUT /{id}/members/{userId}/status` | 200, `{team,member}` | Leader chỉ được sửa thành viên thường |
| `DELETE /{id}/members/{userId}?version=...&teamVersion=...` | 204 | Cùng rule deactivate membership, không xóa row |
| `PUT /{id}/members/{userId}/leadership` | 200, `{team,member}` | Chỉ SystemAdmin |

GET list nhận `page=1`, `pageSize=20` (1–100), `search` tối đa 200 ký tự, `departmentId`, `isActive`, `sortBy=name|createdAt`, `descending=false`. `isActive` lọc cờ của nhóm, không tự thay thế trạng thái phòng ban; DTO có thêm `departmentIsActive`. Sort có tie-break ID. Search tên không phân biệt hoa/thường, `%`/`_` là ký tự text thông thường. GET members nhận `page`, `pageSize`, `isActive` (cờ membership). Các response trang có `{items,page,pageSize,totalCount}`; count/items là query đọc thông thường nên có thể thay đổi giữa hai query.

DTO nhóm: `id`, `departmentId`, `name`, `description`, `isActive`, `departmentIsActive`, `createdAt`, `updatedAt`, `version`. DTO member: `userId`, `displayName`, `userIsActive`, `isActive`, `isLeader`, `joinedAt`, `updatedAt`, `version`. Không trả email, password/hash, token hoặc entity EF. API dùng no-store; Swagger có Bearer/401/403 và DTO. `userIsActive` và `departmentIsActive` giúp phân biệt trạng thái thực tế với cờ membership/nhóm được giữ cho lịch sử.

## Tạo và cập nhật nhóm

```json
{ "departmentId": "<department-id>", "name": "IT Support", "description": "Hỗ trợ nội bộ" }
```

Phòng ban phải tồn tại và active, nhóm mới mặc định active. DepartmentId cố định sau khi tạo để không đổi ngữ cảnh lịch sử Ticket; DTO PATCH chỉ nhận name/description/version. Nếu tổ chức lại, tạo nhóm mới và deactivate nhóm cũ theo nghiệp vụ.

Tên bắt buộc, không toàn whitespace, tối đa 200 ký tự trước trim; API bỏ dấu cách U+0020 hai đầu. Mô tả tối đa 2.000 ký tự, trim, rỗng thành null. Unique index trên `(department_id, normalized_name)` với `normalized_name = lower(btrim(name))`: ` IT Support ` và `IT SUPPORT` trùng trong cùng phòng ban, kể cả nhóm inactive; tên giống nhau ở hai phòng ban được phép. Không bỏ dấu tiếng Việt, gộp dấu cách giữa từ hoặc chuẩn hóa Unicode. So sánh theo collation PostgreSQL hiện tại.

```json
{ "name": "IT Support", "description": null, "version": 123 }
```

```json
{ "isActive": false, "version": 123 }
```

Admin có thể sửa metadata/deactivate nhóm lịch sử ở phòng ban inactive; activate nhóm yêu cầu phòng ban active. Không có DELETE nhóm. Thiếu/0 version hoặc field ngoài contract trả 400; stale version trả 409 `VersionConflict`. Trùng tên trả 409 `NameInUse`. Mọi cập nhật và audit cùng transaction; no-op với version hiện tại không ghi audit hoặc đổi version.

## Thành viên và version

Thêm thành viên:

```json
{ "userId": "<active-user-id>", "teamVersion": 123 }
```

User phải tồn tại và active, nhóm/phòng ban phải active. Cho phép user khác phòng ban hoặc chưa có DepartmentId; tổ chức hỗ trợ là quan hệ N–N độc lập với phòng ban của user. Membership không tự cho user khả năng xử lý Ticket; bước phân công Ticket còn phải kiểm tra quyền Agent và các rule riêng.

POST không nhận IsLeader/IsActive; server tạo membership active, IsLeader=false, JoinedAt UTC. Đã có row kể cả inactive trả 409 `MembershipExists`; dùng endpoint status để kích hoạt lại, không tạo row mới hoặc đổi JoinedAt gốc.

```json
{ "isActive": false, "version": 456, "teamVersion": 124 }
```

```json
{ "isLeader": true, "version": 456, "teamVersion": 124 }
```

`version` là xmin của member, `teamVersion` là xmin của nhóm. Mọi thay đổi membership/leadership bump UpdatedAt/version của **cả member và nhóm**; POST member mới trả version mới cho nhóm. Status/leadership trả `{team,member}` để client dùng tiếp. DELETE nhận hai version qua query, trả 204; tải lại nhóm/member nếu cần tiếp tục sửa. No-op vẫn kiểm tra cả hai version. Nếu gặp 409, tải lại cả nhóm và member trước khi quyết định thử lại.

Deactivate/DELETE giữ nguyên composite key `(support_team_id,user_id)`, JoinedAt và IsLeader. Kích hoạt lại membership cần user/nhóm/phòng ban active; row có IsLeader=true chỉ SystemAdmin được kích hoạt lại. Demote leader có thể thực hiện trên dữ liệu inactive bởi admin để quản trị lịch sử. Leader không thể tự mở lại membership đã bị deactivate.

Nhóm/phòng ban inactive làm membership mất hiệu lực vận hành nhưng không đổi các cờ membership. Activate lại nhóm/phòng ban khôi phục hiệu lực của những membership vốn vẫn active; membership đã bị deactivate riêng vẫn inactive. User inactive cũng làm membership không dùng được; không tự xóa row.

## Lịch sử, đồng thời và audit

Deactivate nhóm hoặc gỡ membership giữ Ticket.AssignedTeamId/AssignedAgentId, lịch sử Ticket, trạng thái user và phiên đăng nhập. Không tự chuyển Ticket hoặc revoke phiên khi đổi tổ chức. Các FK vẫn Restrict. Trạng thái/tính hợp lệ của assignee cho thao tác phân công mới sẽ được kiểm tra ở bước Ticket, không suy từ việc còn ID trong lịch sử.

Luồng ghi dùng advisory transaction lock `734205001`, sau đó khóa user người gọi rồi nhóm/member, cùng thứ tự với tài khoản/phòng ban/Identity. Gán member/activate/leadership không vượt qua deactivate user hoặc phòng ban đã commit qua module quản trị. Khóa ở PostgreSQL nên có hiệu lực giữa nhiều API instance. SQL writer ngoài ứng dụng phải tuân thủ cùng quy tắc nếu thay đổi trực tiếp dữ liệu tổ chức.

Audit trong `identity.identity_audit_logs`: `SupportTeamCreated`, `SupportTeamUpdated`, `SupportTeamStatusChanged`, `TeamMemberAdded`, `TeamMemberStatusChanged`, `TeamLeadershipChanged`. Data whitelist chứa TeamId, DepartmentId/UserId khi cần, tên field hoặc trạng thái trước/sau; không copy tên/mô tả/body tự do. Audit dùng chung transaction, đọc qua API audit phần 6 với scope quản trị hiện có. Chưa có cache/realtime hoặc UI cho module này; frontend vẫn là starter.

## Bắt đầu sử dụng

Sau review/apply migration mới:

```powershell
dotnet run --project be/be.csproj --no-launch-profile -- seed-identity
# Chỉ khi muốn bổ sung team.manage cho role TeamLeader đã tồn tại:
dotnet run --project be/be.csproj --no-launch-profile -- grant-team-leadership
```

Lệnh grant bổ sung đúng `team.manage` cho role TeamLeader, không gán user vào nhóm hoặc tự đánh dấu leader. Lặp an toàn, audit `TeamLeadershipEnabled` chỉ khi thêm grant. Startup không tự chạy lệnh grant; role/grant cũ không bị seed reset. Operator cũng có thể cập nhật grant bằng API phần 6. SystemAdmin mặc định đã có team.manage từ các phần trước; nếu đã cố ý gỡ thì cần cấp lại qua API grant để quản trị module.

Trình tự sử dụng: admin tạo nhóm trong phòng ban active → cấp role/grant phù hợp cho user → thêm member → gọi leadership với IsLeader=true cho đúng nhóm. Dùng JWT của user đó để kiểm tra chỉ nhìn thấy/quản lý nhóm được chỉ định. Profile Container (Dockerfile) vẫn port 5000 và seed/bootstrap theo cấu hình cũ; không auto migrate hoặc cấp leadership cho dữ liệu cũ.

## Kiểm tra

```powershell
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Suite PostgreSQL kiểm tra scope trước phân trang, truy cập chéo nhóm, quyền trên cùng JWT và recheck trong transaction, validation/overposting, version của nhóm/membership, remove mềm, trạng thái parent/user, thao tác đồng thời, bảo toàn Ticket/phiên và migration/replay/rollback với dữ liệu legacy. Script tạo PostgreSQL riêng, không mount volume Helpdesk; có smoke CLI seed và grant-team-leadership trên database test.
