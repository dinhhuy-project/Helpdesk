# Kế hoạch backend sau Identity và phân quyền

Phần 6–9 đã có code/API/tests và người dùng xác nhận review/apply database. Phần 10 đã có [scope Ticket dùng chung, API đọc và guard ghi](backend-ticket-access.md), không cần migration hoặc grant mới. Các bước 11–18 dưới đây là kế hoạch tiếp theo. Ưu tiên các phần nhỏ có API, test và tài liệu để kiểm tra độc lập; frontend tích hợp theo từng phần đã ổn định.

## Điểm xuất phát

Đã có năm phần nền tảng: cấu hình/lỗi/bảo mật, seed Identity/bootstrap admin, login và `/users/me`, refresh rotation/logout, permission policies đọc database. Đã có GitHub Actions cho backend, frontend và Docker.

Domain và EF mapping đã có 19 entity trong bốn schema, gồm audit Identity bổ sung cho 18 entity lõi, với InitialCreate, AccountAdministration, DepartmentAdministration, SupportTeamAdministration và TicketCategoryAdministration. API đã có Auth, Users/me, quản trị User/Identity, Department, SupportTeam/membership, TicketCategory, Ticket GET list/detail/access và WeatherForecast từ template. Chưa có command tạo Ticket/workflow; frontend còn starter và chưa có auth client.

Tận dụng model hiện có; chỉ tạo migration mới khi cần thay đổi field/index/constraint đã review. Không chỉnh InitialCreate đã áp dụng và không tự apply migration vào database đang dùng.

## Thứ tự thực hiện

| Bước | Phần triển khai | Phạm vi chính và điều kiện hoàn thành |
| --- | --- | --- |
| 6 | Quản trị tài khoản và quyền quản trị | Danh sách/chi tiết/tạo/cập nhật/active user; gán role theo phạm vi được phép; đổi mật khẩu và thu hồi phiên; bảo vệ admin cuối cùng. Tách quyền quản lý user khỏi quyền cấp role/grant. |
| 7 | Phòng ban | Tạo, sửa, danh sách và deactivate Department; gán user vào phòng ban hợp lệ; giữ dữ liệu lịch sử. Có policy quản trị và quy tắc tên trùng rõ ràng. |
| 8 | Nhóm hỗ trợ và thành viên | SupportTeam thuộc phòng ban; thêm/gỡ/deactivate membership; kiểm tra user/team/department hợp lệ; chốt phạm vi trưởng nhóm. Dùng `team.manage` và kiểm tra phạm vi tài nguyên khi cần. |
| 9 | Danh mục Ticket | Cây TicketCategory, tạo/sửa/deactivate; chặn parent không hợp lệ và vòng lặp; lookup danh mục active cho form tạo Ticket. Chốt permission quản trị danh mục riêng. |
| 10 | Phạm vi truy cập Ticket | Ma trận đọc/sửa/phân công theo requester, assignee, team và permission; dùng chung cho truy vấn danh sách và chi tiết. Có negative tests chống đọc/sửa Ticket ngoài phạm vi. |
| 11 | Ticket cơ bản | Tạo, danh sách, lọc, phân trang và chi tiết; TicketNumber do database sinh; danh tính requester/creator từ phiên hoặc quyền tạo hộ đã chốt; ghi TicketActivity ngay khi tạo. |
| 12 | Phân công và workflow trạng thái | Chọn team/agent hợp lệ; kiểm tra chuyển trạng thái; bắt buộc resolution summary khi resolve; optimistic concurrency bằng Version/xmin; thay đổi và activity cùng transaction. |
| 13 | Comment và internal note | Bình luận công khai/nội bộ, phân trang; author từ phiên; requester không đọc nội dung nội bộ qua detail/list/activity. Activity và thay đổi comment tuân thủ cùng ACL. |
| 14 | File đính kèm MinIO | Bucket private, giới hạn file, metadata, upload/download có ACL; quyền file gắn với Ticket/comment và internal visibility; xử lý object mồ côi. |
| 15 | Notification và SignalR | Persist notification, danh sách/đánh dấu đã đọc; phát sự kiện sau commit cho đúng người; kiểm tra JWT và quyền vào group. Xác định nhu cầu outbox trước khi cần delivery tin cậy. |
| 16 | SLA MVP | Policy theo priority, tính 24/7; snapshot deadline; first public Agent response; resolved/overdue và worker idempotent. Thay policy không làm đổi deadline Ticket cũ. |
| 17 | Dashboard và báo cáo | Thống kê theo phạm vi được phép, trạng thái/nhóm/agent/SLA; server-side aggregation và kiểm tra index/query thực tế. |
| 18 | Chuẩn bị vận hành | Deploy workflow, HTTPS/secrets, readiness, backup/restore thử nghiệm, retention lịch sử phiên, quan sát lỗi và rà soát phân quyền toàn luồng. Tests/CI vẫn thực hiện ở mọi bước trước đó. |

Phụ thuộc chính: tài khoản + tổ chức + danh mục → ACL Ticket → Ticket cơ bản → workflow/comment → attachment/notification/SLA → báo cáo. Trong bước 11 cần chốt cách gắn SLA ngay từ thiết kế use case tạo Ticket: nếu triển khai Ticket trước SLA, ghi rõ Ticket nào được phép chưa có SLA; không tự gán deadline hồi tố ở bước 16.

## Phạm vi cụ thể cho bước 6

Đã triển khai các phạm vi 6A/6B/6C dưới đây; contract và quy tắc thực tế xem [backend-accounts.md](backend-accounts.md).

### 6A. Danh sách và quản trị tài khoản cơ bản

API quản trị tài khoản hiện có:

| API | Mục đích |
| --- | --- |
| `GET /api/v1/users` | Danh sách, tìm theo email/tên, lọc active, phân trang và sắp xếp ổn định |
| `GET /api/v1/users/{id}` | Chi tiết tài khoản cho người có `user.manage`; trả DTO |
| `POST /api/v1/users` | Tạo tài khoản nội bộ; chuẩn hóa email, hash mật khẩu và mặc định role Requester do server gán |
| `PATCH /api/v1/users/{id}` | Cập nhật các field được cho phép như displayName; không cho sửa role/password/active bằng DTO chung |
| `PUT /api/v1/users/{id}/status` | Activate/deactivate; kiểm tra admin cuối cùng và thu hồi phiên khi deactivate |

Lookup/gán department đã được nối ở [bước 7](backend-departments.md); không tự tạo phòng ban mẫu để vượt qua validation. Đăng ký công khai và quên mật khẩu qua email nằm ngoài lượt 6A.

Danh sách dùng quy ước đề xuất `page=1`, `pageSize=20`, tối đa 100, sort allowlist và tie-break theo ID; response gồm `items`, `page`, `pageSize`, `totalCount`. Chỉ tạo helper dùng chung khi endpoint thực sự cần. Password, token/hash và field nội bộ không xuất hiện trong response/log.

### 6B. Cấp role và kiểm soát đặc quyền

- Cho phép đọc catalog role/permission và cập nhật danh sách role của user qua API riêng.
- Review quyền quản lý role/grant trước khi mở API gán đặc quyền. `user.manage` không tự đồng nghĩa được tự cấp SystemAdmin hoặc sửa mọi RolePermission.
- Đã có permission riêng `role.manage`, scope cấp role và điều kiện SystemAdmin khi sửa grant. Seed bảo toàn role cũ; operator dùng lệnh chủ động để bật quyền quản trị mới nếu cần.
- Bảo vệ ít nhất một SystemAdmin active khi gỡ role hoặc khóa user; kiểm tra bằng cơ chế đồng bộ trong database để hai thao tác đồng thời không vượt qua rule.
- Bảo toàn lịch sử và thu hồi phiên khi quy tắc quản trị yêu cầu; role/grant thay đổi đã được permission checker đọc lại ở request tiếp theo.
- Đã có audit quản trị riêng trong identity.identity_audit_logs cho thay đổi role/status/password, cùng transaction; TicketActivity tiếp tục chỉ phục vụ Ticket.

CRUD role và sửa grant có thể triển khai thành lượt riêng sau khi gán role có sẵn ổn định. Không sửa lại grant tùy chỉnh bằng seed startup.

### 6C. Đổi mật khẩu và hoàn thiện an toàn tài khoản

- Endpoint đổi mật khẩu của chính mình lấy user ID từ principal, kiểm tra mật khẩu hiện tại, validate mật khẩu mới và hash theo cấu hình hiện tại.
- Đổi mật khẩu và revoke toàn bộ refresh session của user trong cùng transaction; sau đó yêu cầu login lại. Không gắn yêu cầu `user.manage` vào thao tác tự đổi mật khẩu.
- Reset mật khẩu bởi quản trị cần policy/audit riêng. Luồng quên mật khẩu bằng email cần token một lần, expiry và kênh gửi; chỉ triển khai khi hạ tầng tương ứng đã sẵn sàng.
- Test tài khoản passwordless, sai mật khẩu hiện tại, account inactive, phiên bị thu hồi và cập nhật đồng thời.

## Các quyết định cần chốt trước phần liên quan

| Vấn đề trong model hiện tại | Cách xử lý khi triển khai |
| --- | --- |
| Email chuẩn hóa đã có unique index trong AccountAdministration | Review xung đột trước khi nâng cấp; không tự gộp/xóa user. Constraint bảo vệ cả create đồng thời. |
| Đã có `department.manage`, `role.manage`, `category.manage` | Quyền quản trị tách riêng, có lệnh grant chủ động cho role cũ. Seed tiếp tục bảo toàn grant cũ. Lookup danh mục dùng ticket.create. |
| Trưởng nhóm đã được chốt theo SupportTeamMember.IsLeader | team.manage + membership leader active trong đúng nhóm/phòng ban active; SystemAdmin có team.manage quản trị toàn cục. Role TeamLeader không tự có scope. |
| User/Role/Department/Team/Member/Category đã có xmin Version | Membership change bump cả member và team. Category version chỉ cho field của node; IsAvailable còn phụ thuộc ancestor hiện tại. |
| Scope Ticket đã chốt theo requester, assignee và membership hiện tại | Read own/team/all kết hợp OR; xử lý cần update.status và assignee/leader hoặc read.all. Assign cần ticket.assign và leader nguồn/đích hoặc read.all. Dùng TicketScope/WriteGuard chung. |
| Quyền reopen/confirm/cancel/create-on-behalf chưa có mã tách riêng | Chốt transition, actor và permission khi thiết kế bước 10–12; không suy ra chỉ từ role hoặc tên trạng thái. |
| Refresh token hiện truyền JSON, frontend chưa có auth client | Khi tích hợp frontend, giữ token trong memory và điều phối một request refresh tại một thời điểm. Nếu cần duy trì qua reload bằng cookie HttpOnly, thiết kế HTTPS/CSRF/CORS cùng thay đổi transport. |

## Tiêu chí hoàn thành mỗi phần

- Controller gọn, DTO/validation rõ ràng, Application giữ use case, Infrastructure giữ EF/integration; không trả entity hoặc IQueryable ra API.
- Policy được gắn vào endpoint thật; thao tác với Ticket/file kiểm tra cả quyền và phạm vi dữ liệu. List query phải scope trước khi tính tổng/phân trang.
- Các thay đổi nhiều bước có transaction, kiểm tra concurrency và lỗi 400/401/403/404/409 được định nghĩa nhất quán; test từ chối truy cập là bắt buộc.
- Có PostgreSQL integration tests cho constraint/transaction/race quan trọng; các module file/realtime có integration tests tương ứng khi thêm.
- Cập nhật OpenAPI và tài liệu; GitHub Actions chạy các kiểm tra hiện có cùng test mới.
- Nếu có migration: tạo migration mới, review SQL/rollback và dữ liệu hiện có trước khi áp dụng. Không tự migrate lúc startup.

## Tích hợp frontend

Có thể bắt đầu auth UI sau khi contract hiện tại ổn định: login/logout, `/users/me`, điều phối refresh và trang 401/403. Sau bước 6–9, thêm UI quản trị user/tổ chức/danh mục. Khi có bước 11–13, tích hợp luồng tạo/xem/xử lý Ticket trước attachment/realtime.

Các tính năng AI, business-hour SLA và tối ưu cache dành sau MVP. Dùng Redis khi đã có query pattern và quy tắc invalidation rõ ràng; không cache permission mặc định để thay thế việc đọc quyền hiện tại.

Bước tiếp theo đề xuất: **11 — Ticket cơ bản**. GET list/detail/access đã nối scope ở phần 10; bổ sung tạo Ticket, kiểm tra category khả dụng trong transaction, requester/creator từ phiên và TicketActivity lúc tạo. Sửa nội dung/transition/confirm/reopen/cancel cần use case và permission phù hợp, không suy từ read scope hoặc tự dùng ticket.update.status để sửa mọi field.
