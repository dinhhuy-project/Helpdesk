# Quản trị tài khoản và quyền quản trị

Đã triển khai phần 6 của roadmap: quản lý user, cấp role theo phạm vi, cập nhật grant của role hiện có, tự đổi mật khẩu, thu hồi phiên và audit. Giữ .NET/EF Core/Npgsql 8.x. Người dùng đã xác nhận review SQL và nâng cấp AccountAdministration. Code phần 7 có [migration DepartmentAdministration riêng](department-administration-review.md), cần review/apply trước khi F5 bản mới.

## Bắt đầu sử dụng

Sau khi review/nâng cấp database, chạy `seed-identity` để bổ sung catalog. Role SystemAdmin mới được seed nhận đủ 13 permission (đã bổ sung `department.manage` ở phần 7); role đã tồn tại vẫn giữ grant hiện có. Operator muốn bật quản trị tài khoản cho SystemAdmin cũ chạy lệnh chủ động `grant-account-administration` theo tài liệu review. Lệnh chỉ bổ sung `user.manage`/`role.manage` còn thiếu và ghi audit, không reset password hay grant khác. Quyền phòng ban có [lệnh grant riêng](backend-departments.md). Startup không tự gọi các lệnh grant này.

Profile Visual Studio **Container (Dockerfile)** tiếp tục dùng port 5000, seed/bootstrap admin theo cấu hình hiện có. Sau nâng cấp, F5 lại và dùng Swagger. Credentials/JWT options vẫn lấy từ user-secrets; không có secret mới cho module này.

## API và quyền

| API | Điều kiện | Kết quả thành công |
| --- | --- | --- |
| `GET /api/v1/users` | `user.manage` | 200, danh sách phân trang |
| `GET /api/v1/users/{id}` | `user.manage` | 200, DTO user |
| `POST /api/v1/users` | `user.manage` | 201 + Location; server gán Requester |
| `PATCH /api/v1/users/{id}` | `user.manage` + scope target | 200, DTO/version mới |
| `PUT /api/v1/users/{id}/status` | `user.manage` + scope target | 200, DTO/version mới |
| `PUT /api/v1/users/{id}/roles` | `role.manage` + scope cấp role | 200, DTO/version mới |
| `POST /api/v1/users/me/password` | Phiên hợp lệ và mật khẩu hiện tại đúng | 204, yêu cầu login lại |
| `GET /api/v1/identity/roles` | `role.manage` | 200, ID/name/description/version/permission codes |
| `GET /api/v1/identity/permissions` | `role.manage` | 200, catalog ID/code đã đăng ký |
| `PUT /api/v1/identity/roles/{id}/permissions` | SystemAdmin có `role.manage` | 200, role/version mới |
| `GET /api/v1/identity/audit-logs` | SystemAdmin có `role.manage` | 200, audit phân trang |

`GET /api/v1/users/me` giữ nguyên quyền truy cập cho mọi user có phiên hợp lệ/active. Quyền `user.manage` không bao gồm quyền gán role hoặc sửa grant. Role/grant thay đổi được kiểm tra ở request tiếp theo trên cùng JWT; không cần refresh để lấy quyền mới.

### Danh sách và DTO

Query: `page=1`, `pageSize=20` (1–100), `search` tối đa 200 ký tự, `isActive`, `sortBy=createdAt|email|displayName`, `descending=true`. Page tối đa 1.000.000; sort có tie-break ID. Search email/tên không phân biệt hoa/thường, được tham số hóa qua EF. Response: `items`, `page`, `pageSize`, `totalCount`; count và items là các query đọc thông thường nên có thể thay đổi giữa hai query khi hệ thống đang được cập nhật.

DTO user quản trị gồm `id`, `email`, `displayName`, `isActive`, `departmentId`, `createdAt`, `updatedAt`, `version`, `roles: [{id,name}]`. Không có password/hash, token/hash hoặc EF entity. Các endpoint quản trị và đổi mật khẩu dùng `Cache-Control: no-store` qua controller; lỗi không echo credential.

### Tạo tài khoản

```json
{
  "email": "requester@example.test",
  "displayName": "Requester mới",
  "password": "<mật khẩu ban đầu 12–128 ký tự>"
}
```

Ví dụ chỉ minh họa contract; thay placeholder bằng password hợp lệ khi thử. Email được trim/lowercase, displayName trim, user mới active và chỉ được gán Requester bởi server. Unique index trên `normalized_email = lower(btrim(email))` chặn trùng cả khi tạo đồng thời hoặc khi có writer khác. Mật khẩu dùng PasswordHasher/PBKDF2 210.000 iterations và không được trả lại trong response.

DTO tạo/cập nhật riêng từ chối JSON field ngoài contract: gửi thêm `roleIds`, `isActive`, `passwordHash` vào API tạo hoặc `password` vào PATCH không giúp thay đổi các field đó; trả 400. Email hiện không sửa qua PATCH.

### Cập nhật profile, status và role

Lấy `version` từ GET/detail hoặc response cập nhật gần nhất. Mọi request sau phải gửi version hiện tại; thiếu/0 trả 400, stale trả 409 `VersionConflict`.

```json
{ "displayName": "Tên mới", "departmentId": null, "version": 123 }
```

PATCH chỉ cập nhật displayName/departmentId; trường departmentId vắng/null có nghĩa xóa liên kết. Gán/đổi Department phải chọn ID tồn tại và active. Giữ nguyên ID hiện tại khi sửa displayName vẫn được phép nếu phòng ban đã inactive, để bảo toàn liên kết lịch sử. Xem [API Department và lookup](backend-departments.md). Gán user và deactivate Department được đồng bộ bằng transaction lock chung.

```json
{ "isActive": false, "version": 123 }
```

Status phải được gửi rõ ràng. Khi trạng thái thay đổi, thu hồi toàn bộ refresh token chưa bị revoke trong cùng transaction với cập nhật user/audit. Activate lại không hồi sinh phiên cũ, user login lại. Hành động no-op không đổi version hoặc tạo audit mới.

```json
{ "roleIds": ["<ID role đã có>"], "version": 123 }
```

Gửi danh sách đầy đủ role mong muốn; `[]` gỡ toàn bộ role của target thông thường. ID phải tồn tại, không rỗng/trùng, tối đa 50. Version user được tăng khi membership thay đổi. Các API không cho tự chỉnh role của chính mình.

Phạm vi cấp quyền:

- SystemAdmin có `role.manage` được cập nhật role của user khác, với guard admin cuối cùng.
- Người có `role.manage` nhưng không phải SystemAdmin chỉ được thay đổi các role thông thường mà toàn bộ permission của cả role cũ lẫn role mới nằm trong grant hiện tại của mình. Không được gán/gỡ SystemAdmin hoặc role chứa `user.manage`/`role.manage`.
- Profile/status của user giữ SystemAdmin hoặc có grant `user.manage`/`role.manage` chỉ cho SystemAdmin có cả quyền quản lý user và role chỉnh sửa. Người được ủy quyền quản lý user không sửa được tài khoản đặc quyền.
- SystemAdmin vẫn phải có permission tương ứng; tên role không thay thế grant.

Hệ thống giữ ít nhất một SystemAdmin active khi deactivate/gỡ role, kể cả hai request đồng thời trên nhiều API instance. SystemAdmin có đủ quyền được tự deactivate khi còn admin active khác; tự chỉnh role luôn bị từ chối. Quy tắc áp dụng cho thao tác qua module/initializer này, không thay thế kiểm soát truy cập SQL trực tiếp của operator.

### Cập nhật grant của role

```json
{ "permissionIds": ["<ID permission từ catalog>"], "version": 123 }
```

Thay thế danh sách grant của role hiện có; ID phải đăng ký, không trùng, tối đa 100. Chỉ SystemAdmin có `role.manage` thực hiện được. SystemAdmin role phải giữ cả `user.manage` và `role.manage` để tránh mất kênh quản trị. Role khác có thể nhận grant nhạy cảm nếu operator quản trị chủ động cấp, nhưng scope bảo vệ tài khoản/cấp đặc quyền vẫn được kiểm tra.

Version của role được tăng khi grant thay đổi qua API/CLI quản trị. Seed tiếp tục không tự khôi phục grant đã gỡ. Catalog role hiện có được dùng cho cấp quyền; tạo/xóa/đổi tên role chưa nằm trong API này.

### Tự đổi mật khẩu

```json
{ "currentPassword": "<mật khẩu hiện tại>", "newPassword": "<mật khẩu mới 12–128 ký tự>" }
```

Không cần `user.manage`; danh tính lấy từ `sub`/`sid`, không có target ID trong body. New password không được trùng mật khẩu hiện tại. Sai mật khẩu trả 400; tài khoản chỉ dùng external identity không có password nội bộ trả 409. Endpoint dùng chung rate limit authentication với login/refresh/logout, mặc định 5 request/60 giây/IP; 429 có Retry-After.

Hash mới, revoke tất cả phiên và audit được commit cùng nhau. Sau 204, client bỏ cả hai token và login lại bằng mật khẩu mới. Login kiểm tra lại password hash dưới khóa trước khi tạo session: request đã verify password cũ trước lúc đổi mật khẩu không tạo được phiên mới sau khi việc đổi mật khẩu đã commit. Refresh dùng cùng khóa user nên cặp token vừa rotation cũng nằm trong phạm vi thu hồi nếu password change thành công. Client nên điều phối refresh để tránh nó làm request đổi password dùng access token cũ bị 401.

Reset mật khẩu bởi quản trị/quên mật khẩu qua email cần workflow một lần/expiry riêng; PATCH tài khoản không phải đường tắt để đặt password của user khác.

## Transaction, version và audit

Mutation lấy advisory transaction lock `734205001` dùng chung với initializer, rồi khóa user. Sau khóa, backend kiểm tra lại actor active/session/permission trong database trước khi ghi. Last-admin/scope/grant check được serialize giữa API instance; không dựa vào lock trong process. Password/refresh cùng khóa hàng user để tránh phiên mới thoát thu hồi. `xmin` dùng làm Version cho aggregate User/Role; cập nhật membership/grant sẽ cập nhật UpdatedAt của aggregate để version thay đổi.

Audit `identity.identity_audit_logs` có actor/target user/target role, action, metadata whitelist và UTC timestamp. Event hiện gồm UserCreated, UserUpdated, UserStatusChanged, UserRolesChanged, RoleGrantsChanged, PasswordChanged và AccountAdministrationEnabled. Không ghi raw body, password/hash, token/hash hoặc email/displayName vào metadata. CLI operator có ActorId null. Audit nằm trong cùng transaction với thay đổi; chưa có endpoint xóa/sửa audit hoặc job retention.

HTTP lỗi dùng ProblemDetails có traceId; lỗi nghiệp vụ có thêm `code`: NotFound (404), VersionConflict/EmailInUse/LastActiveAdmin/ProtectedAdminGrant/PasswordlessAccount (409), InsufficientScope (403), InvalidDepartment/InvalidRole/InvalidCurrentPassword (400), Unauthenticated (401). Validation JSON/field có errors theo cơ chế chung. Lỗi database không dự kiến tiếp tục được che chi tiết.

## Kiểm tra

```powershell
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Tests dùng PostgreSQL tạm: CRUD/search/paging, DTO an toàn/overposting, unique email đồng thời, stale version, scope cấp role, guard grant quản trị, last-admin race, status/password revoke, login/password race, audit và CLI idempotence. Migration tests tạo database con tạm trong cùng container để thử nâng cấp legacy email và duplicate preflight; không chạm database thật. CI hiện có tự chạy toàn bộ suite; không cần secret mới.
