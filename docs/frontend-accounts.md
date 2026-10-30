# Quản trị tài khoản và quyền — frontend bước 7

Phần 6 đã được kiểm tra lại trước khi triển khai. Feature mới nằm trong `fe/src/features/accounts`, dùng typed API, Query cache theo phiên, form và dialog chung; không thêm package, migration hoặc secret. Backend vẫn .NET/EF Core/Npgsql 8.x.

## Màn hình và quyền

| Route frontend | Quyền và chức năng |
| --- | --- |
| `/admin/users` | `user.manage`: tìm theo tên/email, lọc active, sort, phân trang server; lưu bộ lọc và trang trên URL |
| `/admin/users/new` | `user.manage`: tạo bằng email/tên/mật khẩu ban đầu; server gán Requester; thành công mở detail |
| `/admin/users/:id` | `user.manage`: thông tin, role hiện tại, cập nhật tên/phòng ban, kích hoạt/vô hiệu hóa theo capability server |
| `/admin/roles` | `role.manage`: catalog role hiện có, grant hiện tại và phạm vi có thể cấp |
| `/admin/roles/assignments` | `role.manage`: tìm và chọn tài khoản trong phạm vi cấp, độc lập với quyền đọc trang user |
| `/admin/roles/assignments/:id` | `role.manage`: thay thế toàn bộ role của target bằng các role được server cho phép |
| `/admin/roles/:id` | `role.manage`: xem; chỉ khi server trả `canManageGrants` mới có form thay thế grant |
| `/admin/roles/audit` | `role.manage` và `canReadAudit`: nhật ký Identity phân trang, thời gian local, actor/target/action/metadata |

Menu tách hai nhánh user và role, không có quyền vượt policy chỉ nhờ tên SystemAdmin. Các trang quản trị được tải theo route để không tăng toàn bộ bundle khởi động. Guard xác nhận phiên/quyền trước khi render nội dung. GUID, query trùng, phân trang và sort được kiểm tra trước khi gọi API. Truy vấn không mang rows cũ sang bộ lọc hoặc phiên khác.

## Contract backend bổ sung

Các endpoint ghi cũ giữ nguyên policy và kiểm tra trong transaction. Bổ sung API **đọc** để frontend không tự quyết định phạm vi từ role name:

| API | Policy | Response |
| --- | --- | --- |
| `GET /api/v1/users/{id}/administration-access` | `user.manage` | `canUpdateProfile`, `canChangeStatus`, `isLastActiveAdmin` |
| `GET /api/v1/identity/administration-access` | `role.manage` | `canManageGrants`, `canReadAudit`, `assignableRoleIds` |
| `GET /api/v1/identity/assignment-users` | `role.manage` | DTO user phân trang; cùng bộ lọc/sort với user list nhưng lọc phạm vi trước count/page |
| `GET /api/v1/identity/assignment-users/{id}` | `role.manage` | User/version/role hiện tại trong phạm vi cấp; ngoài phạm vi, chính mình hoặc không tồn tại trả 404 |

API assignment dành riêng cho người quản lý role; không mở rộng quyền của `GET /users` và không cấp thêm `user.manage` chỉ để chọn target. Người được ủy quyền không thấy target giữ role có quyền vượt grant của mình, SystemAdmin hoặc quyền quản trị user/role. Backend xác nhận lại session active và quyền hiện tại. Capability là ảnh chụp phục vụ UX; mutation vẫn có thể từ chối nếu dữ liệu/quyền thay đổi sau khi đọc.

Các response quản trị dùng `Cache-Control: no-store`. Không có tạo/xóa/đổi tên role, reset mật khẩu của người khác hoặc endpoint thu hồi phiên tùy ý. Audit chỉ đọc; metadata hiển thị bằng text, không render HTML.

DTO user bổ sung `departmentName` nullable, được project từ database kể cả phòng ban inactive. Detail hiển thị tên liên kết hiện tại sau khi lưu/tải lại, không yêu cầu nhập hoặc tra UUID thủ công; không thêm request quản trị Department hay thay đổi schema.

## Quy tắc cập nhật

- Tạo user chỉ gửi `{email, displayName, password}`. Password 12–128 ký tự, không vào URL, browser storage, Query data hoặc mutation cache; thành công bỏ draft password. Role và phòng ban thực hiện sau khi tạo theo quyền riêng. Email hiện chỉ đọc sau khi tạo.
- PATCH chỉ gửi `{displayName, departmentId, version}`. Lookup `/departments/lookup` phân trang chỉ trả phòng ban active, không cần `department.manage`. Không nhập UUID mới tự do. Giữ ID phòng ban cũ mặc định, kể cả đã inactive, để bảo toàn liên kết lịch sử; có thể bỏ liên kết bằng null.
- Status và role/grant đều gửi version đọc từ server. `409 VersionConflict` giữ bản nhập, chặn lưu tiếp cho đến khi người dùng chủ động tải lại và kiểm tra. Không tự ghi đè bằng version mới.
- Role assignment và grant là **replace toàn bộ**, có dialog xem lại trước khi lưu. Danh sách rỗng nghĩa là gỡ hết. Không làm rơi grant lạ ngoài catalog; trường hợp này khóa form để kiểm tra.
- Target được bảo vệ ngoài scope có form chỉ đọc; admin active cuối cùng không có nút deactivate khả dụng. Server tiếp tục bảo vệ last-admin khi race, tự gán role và grant quản trị bắt buộc. Các lỗi `LastActiveAdmin`, `ProtectedAdminGrant`, `InsufficientScope`, `InvalidDepartment`, `EmailInUse` có thông báo và traceId.
- Không retry mutation khi timeout/network/5xx hoặc receipt chưa xác định. Với tạo user, kiểm tra theo email trong danh sách trước khi mở yêu cầu mới. Với cập nhật, tải lại kiểm tra trạng thái thực tế trước khi gửi tiếp. 429 tôn trọng Retry-After và khóa gửi trong thời gian chờ.

## Phiên và cache sau thay đổi

Query keys chứa scope của phiên, query hủy khi rời trang; response về muộn không được điều hướng hoặc phục hồi dữ liệu sau logout. Lưu thành công đánh dấu các query stale. Thay grant sẽ xác nhận lại `/users/me` qua profile boundary; thay permission/role/phòng ban làm đổi scope và xóa cache trước khi xuất bản danh tính mới. Lỗi xác nhận profile sau commit được xử lý ở boundary, không biến một mutation đã commit thành yêu cầu cần gửi lại.

Tự deactivate thành công xóa token/cache của **đúng scope hiện tại** ngay lập tức, không gọi thêm API revoke. Deactivate tài khoản khác thu hồi phiên ở backend; client của tài khoản đó kết thúc phiên khi request tiếp theo/kiểm tra profile nhận 401 và refresh bị từ chối. Activate lại không hồi sinh phiên cũ. Trang `/account/password` tiếp tục dùng mật khẩu hiện tại, thành công yêu cầu đăng nhập lại và xóa cache. Chưa có realtime tức thì báo thay đổi quyền/status giữa các trình duyệt; dùng kiểm tra profile khi focus/online/visibility và HTTP authorization.

## Kiểm tra và chạy local

```powershell
cd fe
npm run lint
npm test
npm run build
cd ..
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~AccountAdministration|FullyQualifiedName~DepartmentAdministration|FullyQualifiedName~FrontendAccountJourney'
```

FE kiểm tra separation user/role, scope từ server, direct link ngoài phạm vi, cache/response muộn, DTO/password, POST kết quả chưa rõ, version conflict, lookup/historical department, 429, protected admin/grant, self-deactivate và tự mất quyền sau thay grant. Backend tests kiểm tra query scope thực tế với PostgreSQL tạm và cùng access JWT sau khi grant bị gỡ.

`FrontendAccountJourneyTests` chạy Node/Vite SSR tải chính typed API/auth/cache của frontend qua HTTP tới application và PostgreSQL test: tạo user, lookup/gán phòng ban, stale version, role-only manager, sửa grant role tạm, đổi mật khẩu, deactivate/activate và audit. Credential ngẫu nhiên qua stdin; dữ liệu và container được dọn sau test. CI backend đã có Node và npm dependencies cho SignalR/thảo luận, nên test mới không cần job hoặc secret mới.

Browser mode tùy chọn dùng Playwright đã cài bên ngoài repo; không cần thêm dependency runtime. Bật Vite bằng `npm run dev`, cấu hình `HELPDESK_PLAYWRIGHT_MODULE` là đường dẫn tuyệt đối tới `playwright/index.mjs`, `HELPDESK_FRONTEND_URL=http://localhost:5173`, `HELPDESK_BROWSER_CHANNEL=msedge` (hoặc Chrome đã cài), rồi chạy filter `FrontendAccountJourney`. Có thể đặt `HELPDESK_BROWSER_ARTIFACTS` tới thư mục tồn tại để lưu screenshot. Browser chuyển tiếp HTTP tới backend test thật, không dùng API hoặc database Helpdesk đang phát triển; CI mặc định chỉ chạy nhánh client/API.

Nghiệm thu local ngày 29/10/2026: toàn bộ frontend 272 tests/24 files; nhóm tài khoản/phòng ban/client thật 50 integration tests, không skip, cùng CLI smoke checks. Browser Edge với hai context admin và user manager đã tạo user → chọn phòng ban → cấp role → xem audit → deactivate qua UI thật; viewport 390px không tràn ngang và focus bàn phím hoạt động. Đã kiểm tra lại tên phòng ban từ response sau khi lưu, kể cả tên liên kết inactive trong test backend. Đây là kiểm tra trên dữ liệu/container tạm, không phải thao tác hoặc nghiệm thu database Helpdesk cá nhân; chưa chạy toàn bộ suite backend trong lần thay đổi này.

Để dùng ứng dụng: **restart API Visual Studio Container (Dockerfile)** để nạp các API đọc mới, chạy frontend, đăng nhập tài khoản có grant phù hợp. Không cần migrate/seed lại hoặc sửa `.env`. Tiếp theo là [bước 8 — quản trị dữ liệu tổ chức và danh mục](frontend-roadmap.md).
