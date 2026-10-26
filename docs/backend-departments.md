# Quản lý phòng ban

Phần 7 bổ sung API Department, policy `department.manage`, tên duy nhất và concurrency bằng PostgreSQL `xmin`. Gán phòng ban tiếp tục dùng API quản trị user của phần 6. Trước khi chạy code mới, review và áp dụng [DepartmentAdministration](department-administration-review.md). Không có package hoặc secret mới.

## API và quyền

| API | Permission | Thành công |
| --- | --- | --- |
| `GET /api/v1/departments` | `department.manage` | 200, danh sách quản trị gồm cả inactive nếu không lọc |
| `GET /api/v1/departments/{id}` | `department.manage` | 200, chi tiết; không tồn tại trả 404 |
| `POST /api/v1/departments` | `department.manage` | 201 + Location, mặc định active |
| `PATCH /api/v1/departments/{id}` | `department.manage` | 200, sửa tên/mô tả với version hiện tại |
| `PUT /api/v1/departments/{id}/status` | `department.manage` | 200, activate/deactivate với version hiện tại |
| `GET /api/v1/departments/lookup` | `user.manage` | 200, chỉ phòng ban active, DTO `{id,name}` để chọn khi gán user |
| `PATCH /api/v1/users/{id}` | `user.manage` + scope target của phần 6 | 200, gán/đổi/xóa departmentId |

Mọi endpoint yêu cầu user active và phiên hợp lệ; thiếu/sai JWT trả 401, thiếu permission trả 403. `department.manage` không bao gồm quản lý user, role hoặc grant. Người chỉ có `user.manage` được lookup/gán phòng ban nhưng không được sửa Department. SystemAdmin cũng phải có grant tương ứng. Thao tác ghi kiểm tra lại phiên/user/grant trong transaction sau khi khóa; quyền bị gỡ sau bước middleware vẫn bị từ chối tại đây.

Danh sách quản trị nhận `page=1`, `pageSize=20` (tối đa 100), `search` tối đa 200 ký tự, `isActive`, `sortBy=name|createdAt`, `descending=false`. Sort có tie-break ID; search theo tên không phân biệt hoa/thường, ký tự `%`/`_` là text thông thường. Lookup nhận `page`, `pageSize`, `search`, luôn lọc active và sắp theo tên. Cả hai trả `{items,page,pageSize,totalCount}`; count/items là các query đọc thông thường, không phải snapshot chung.

DTO chi tiết gồm `id`, `name`, `description`, `isActive`, `createdAt`, `updatedAt`, `version`; không expose entity hoặc normalizedName. Controller đặt `Cache-Control: no-store`. Swagger có Bearer/401/403, request và response DTO.

## Tên phòng ban và concurrency

```json
{ "name": "Phòng Nhân sự", "description": "Quản lý nhân sự" }
```

Tên bắt buộc, không toàn whitespace, tối đa 200 ký tự trước trim; mô tả tùy chọn, tối đa 2.000 ký tự, trim và chuyển rỗng thành null. API bỏ dấu cách U+0020 ở hai đầu tên. Unique index trên generated column `normalized_name = lower(btrim(name))` bảo vệ cả API đồng thời và SQL writer khác, theo collation PostgreSQL của database. Không bỏ dấu tiếng Việt, không gộp dấu cách ở giữa, không chuẩn hóa Unicode hoặc biến tab thành dấu cách. Ví dụ ` Finance ` và `FINANCE` trùng; `Phòng IT` và `Phong IT` khác nhau.

Tên hiện tại được giữ duy nhất kể cả khi phòng ban inactive. Muốn dùng lại phòng ban cũ, activate hoặc đổi tên rõ ràng; không tự tạo bản ghi mới trùng tên. Trùng khi tạo/sửa trả 409 `NameInUse`; thay đổi và audit cùng rollback khi lỗi.

PATCH nhận đầy đủ name/description và version; bỏ description có nghĩa xóa mô tả:

```json
{ "name": "Phòng Nhân sự", "description": null, "version": 123 }
```

```json
{ "isActive": false, "version": 123 }
```

Lấy version từ GET/response gần nhất. Thiếu/0 trả 400; stale trả 409 `VersionConflict`, cần tải lại trước khi sửa. `isActive` phải gửi rõ ràng. Gửi field ngoài contract (id, createdAt, isActive trong POST/PATCH...) trả 400. No-op với version hiện tại không đổi version/thời gian và không tạo audit.

## Gán user và bảo toàn lịch sử

Gán phòng ban qua PATCH user, dùng **version của user**:

```json
{ "displayName": "Nguyễn Văn A", "departmentId": "<department-id>", "version": 456 }
```

Đổi/gán mới chỉ nhận ID tồn tại và active; ID không hợp lệ trả 400 `InvalidDepartment`. `departmentId` vắng hoặc null xóa liên kết. Nếu user đang gắn phòng ban inactive, gửi lại chính ID đó để giữ liên kết khi sửa displayName vẫn hợp lệ; không được chuyển user khác vào phòng ban inactive. Quy tắc scope tài khoản đặc quyền của phần 6 vẫn áp dụng.

Deactivate Department giữ nguyên bản ghi, User.DepartmentId, SupportTeam.DepartmentId, trạng thái user/team và các phiên đăng nhập. Không xóa hoặc tự di chuyển dữ liệu cũ; không có DELETE Department. FK vẫn Restrict. Activate lại cho phép gán mới; user đã được xóa/chuyển liên kết không tự quay về phòng ban cũ.

Gán user và thay đổi Department cùng dùng advisory transaction lock `734205001` với module quản trị tài khoản/Identity, theo thứ tự khóa chung. Nếu gán hoàn tất trước deactivate thì liên kết được giữ; nếu deactivate hoàn tất trước kiểm tra gán thì gán bị từ chối. Khóa nằm ở PostgreSQL, có hiệu lực giữa nhiều API instance; không giữ khóa qua dịch vụ ngoài. Các writer SQL ngoài ứng dụng phải tuân thủ cùng quy tắc nếu thay đổi tổ chức.

Audit `DepartmentCreated`, `DepartmentUpdated`, `DepartmentStatusChanged` được ghi trong `identity.identity_audit_logs` cùng transaction. Data whitelist chứa DepartmentId, tên field thay đổi hoặc trạng thái trước/sau; không copy request, tên/mô tả tự do hay credential. Audit quản trị hiện đọc qua `/api/v1/identity/audit-logs` với scope SystemAdmin + `role.manage`. Module này chưa có cache hoặc realtime; frontend hiện còn starter.

## Sử dụng với database đã có

Sau khi áp dụng SQL mới, chạy:

```powershell
dotnet run --project be/be.csproj --no-launch-profile -- seed-identity
dotnet run --project be/be.csproj --no-launch-profile -- grant-department-administration
```

Role SystemAdmin mới được seed nhận đủ permission mặc định (hiện 14 mã, gồm category.manage ở phần 9). Role đã tồn tại giữ nguyên grant; seed startup không tự cấp quyền mới vào role cũ. Lệnh thứ hai chủ động bổ sung đúng `department.manage` cho SystemAdmin, lặp an toàn, bump version role và ghi một audit `DepartmentAdministrationEnabled` khi thực sự thêm grant. Operator có thể cấp permission qua API grant của phần 6 thay thế lệnh này. Không cần đổi JWT hoặc đăng nhập lại để thấy grant mới.

Profile **Container (Dockerfile)** giữ port 5000 và seed/bootstrap đã cấu hình. Startup không apply migration và không chạy lệnh grant chủ động. Sau nâng cấp schema/cấp grant, F5 và thử API bằng Swagger.

## Kiểm tra

```powershell
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Test PostgreSQL gồm API CRUD/status, phân trang/validation, permission âm và tách quyền, recheck phiên/grant/user, tên trùng kể cả inactive/tạo đồng thời, conflict version, audit rollback, bảo toàn user/team/phiên, gán user sau deactivate và upgrade/rollback/replay SQL trên dữ liệu cũ. Script dùng PostgreSQL riêng không mount volume Helpdesk.
