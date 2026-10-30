# Phòng ban, nhóm, thành viên và danh mục — frontend bước 8

Đã kiểm tra lại phần 7 trước khi triển khai: 272 kiểm thử frontend đạt. Bước 8 nằm trong `fe/src/features/catalogs`, dùng API thật và các component form/table/dialog/pagination chung. Giữ React/package hiện tại và backend .NET/EF Core/Npgsql 8.x; không có migration, secret hoặc grant mới.

## Màn hình và quyền

| Route | Permission | Chức năng |
| --- | --- | --- |
| `/admin/departments` | `department.manage` | Tìm/lọc active/sort/phân trang server, tạo phòng ban |
| `/admin/departments/new`, `/admin/departments/:id` | `department.manage` | Tạo/sửa tên/mô tả, activate/deactivate; mở nhóm thuộc phòng ban khi có `team.manage` |
| `/admin/teams` | `team.manage` | Danh sách theo scope, lọc theo tên/trạng thái/phòng ban, phân trang server |
| `/admin/teams/new` | `team.manage` và server `canCreate` | Chọn phòng ban active bằng lookup đúng scope; tạo nhóm |
| `/admin/teams/:id` | `team.manage` và scope tài nguyên | Sửa metadata, quản lý membership; status và leadership chỉ theo capability server |
| `/admin/categories` | `category.manage` | Cây duyệt từng cấp, phân trang; mở nhánh theo `hasChildren`; tìm/lọc và xem tất cả cấp |
| `/admin/categories/new`, `/admin/categories/:id` | `category.manage` | Tạo/sửa tên/mô tả, đổi parent/chuyển về root, activate/deactivate |

Menu và route guard kiểm tra permission hiện tại từ profile, không có bypass bằng tên SystemAdmin/TeamLeader. Trang lazy load sau guard. GUID, duplicate query keys, sort và page/pageSize được kiểm tra trước khi gọi API. Bộ lọc/trang lưu trên URL; back/forward khôi phục form bộ lọc. Danh sách không mang rows của trang/bộ lọc trước sang lần tải mới.

Team detail kiểm tra endpoint scope trước khi tải chi tiết hoặc membership. Ngoài phạm vi trả 404; thiếu permission trả 403. Khi mất grant/scope, không render lại nội dung cũ trong trạng thái lỗi. Cache theo scope phiên, dữ liệu quản trị không persist vào browser storage; query được hủy/dọn khi rời trang hoặc đổi phiên. Trưởng nhóm không cần `user.manage` hoặc `department.manage` để quản lý thành viên trong phạm vi của mình.

## Contract bổ sung ở backend

Các mutation cũ giữ nguyên rule, transaction, audit và optimistic concurrency. Bổ sung API **đọc**, tất cả dùng `team.manage`, phiên/account active và `Cache-Control: no-store`:

| API dưới `/api/v1/support-teams` | Kết quả |
| --- | --- |
| `GET /administration-access` | `canCreate`, `canChangeStatus`, `canManageLeadership`, `canAddMembers` cho ngữ cảnh chung; không tự cấp thao tác tài nguyên |
| `GET /{id}/administration-access` | Kiểm tra team scope, trả capability cho team hiện tại |
| `GET /department-lookup` | `{items:[{id,name}],page,pageSize,totalCount}`; phòng ban active. Admin có team.manage thấy toàn bộ active; leader chỉ thấy phòng ban của team đang có scope |
| `GET /{id}/member-candidates` | User active chưa có membership **kể cả inactive** trong team. DTO `{id,displayName,departmentName}`; kiểm tra team scope/availability trước danh tính/count |

Hai lookup nhận `page`, `pageSize` 1–100, `search` tối đa 200 ký tự. Candidate tìm theo tên hiển thị, không expose email/password/token/quyền đầy đủ. Tài khoản có thể thuộc phòng ban khác hoặc chưa thuộc phòng ban: membership N–N độc lập với DepartmentId của user. Giao diện hiển thị tên, phòng ban và mã ngắn giúp phân biệt người trùng tên; không nhập UUID tự do.

DTO team bổ sung `departmentName`, lấy tên cả phòng ban lịch sử. DTO member bổ sung `canBeLeader`: server xét account/membership/team/department active và grant `team.manage`; boolean này là tính hợp lệ của target, không thay quyền chỉ định leader của actor. DTO category bổ sung `parentName`, `canActivate`; tên cha lịch sử vẫn hiển thị khi nhánh inactive, activate chỉ được bật khi tổ tiên khả dụng.

Không mở rộng policy của `GET /users`, `GET /departments` hoặc lookup cho form tạo Ticket. Frontend quản trị chọn parent qua API quản trị category, không đòi thêm `ticket.create`. Swagger sinh DTO mới từ controller/record; không đổi method/body của API ghi hiện có.

## Quy tắc dữ liệu

- Phòng ban: tên duy nhất toàn bộ catalog, kể cả inactive. Team: tên duy nhất trong cùng phòng ban. Category: tên duy nhất giữa các node có cùng parent, bao gồm các root. So sánh dùng rule `lower(btrim(name))` ở PostgreSQL: không tự bỏ dấu hoặc gộp khoảng trắng bên trong.
- Tên tối đa 200 ký tự, mô tả 2.000; DTO riêng từng thao tác. Team chỉ chọn DepartmentId khi tạo, PATCH không gửi trường này. Đổi tổ chức cần tạo team mới và ngừng team cũ theo backend; không có xóa department/team/category trên UI.
- Parent category được gửi rõ `parentId`, kể cả null khi chuyển về root. Form giữ parent cũ mặc định để sửa metadata trên lịch sử inactive. Picker phân trang, mở từng nhánh, chỉ chọn node `isAvailable`; khóa chính node đang sửa và nhánh tự duyệt dưới nó. Kết quả tìm kiếm có thể chứa node hậu duệ, nên server tiếp tục là nơi kiểm tra đầy đủ `CategoryCycle`/`InvalidParent`; lỗi giữ bản nhập để chọn lại. Không tải toàn bộ cây để tự xác định ACL hoặc thay rule server.
- `isActive` và khả dụng là hai trạng thái riêng. Tắt cha không đổi cờ active của danh mục con nhưng làm nhánh không xuất hiện trong lookup Ticket. UI hiển thị điều này và chặn activate khi server báo tổ tiên chưa khả dụng.
- Deactivate phòng ban/team có confirm và giữ user/team/membership/Ticket lịch sử. Không chuyển Ticket hoặc thu hồi phiên chỉ vì thay đổi tổ chức. Danh mục mới khả dụng xuất hiện trong form Ticket; danh mục bị tắt không dùng cho Ticket mới, Ticket cũ vẫn giữ liên kết.

## Membership và leadership

Danh sách membership có page/pageSize/isActive riêng, lưu bằng `memberPage`, `memberPageSize`, `memberIsActive` trên URL team detail. Thêm member dùng candidate lookup rồi confirm, chỉ gửi `{userId,teamVersion}`; server tạo thành viên thường. Đã có row inactive phải dùng kích hoạt lại, không POST lại.

Status gửi `{isActive,version,teamVersion}`. Leadership gửi `{isLeader,version,teamVersion}`. Gỡ dùng đúng `DELETE /{id}/members/{userId}?version=...&teamVersion=...`, là ngừng tham gia và trả 204, không xóa row/JoinedAt/IsLeader. Không có endpoint xóa user hoặc cấp role từ màn hình này.

Một command lock chung cho workspace team ngăn hai thao tác cùng dùng version cũ. Receipt `{team,member}` được dùng cập nhật teamVersion mới và refetch members; bản nhập metadata chưa lưu được giữ khi thao tác thành viên. DELETE thành công phải đọc lại team/member để lấy version mới; nếu đọc sau commit thất bại, báo thao tác đã thành công và khóa ghi cho đến khi tải lại. Không biến lỗi đọc này thành yêu cầu retry DELETE.

Trưởng nhóm chỉ quản lý thành viên thường trong team active/department active thuộc scope. Không có nút status team, leadership hoặc thay membership của leader nếu capability không cho phép. Chỉ định leader cần server `canManageLeadership` và target `canBeLeader`; gỡ leadership trên lịch sử inactive vẫn được hỗ trợ khi backend cho phép. Membership không cấp UserRole, không tự tạo quyền Agent và không thay `/users/{id}/roles`.

## Version, lỗi và cache

Form giữ snapshot đang sửa khi refetch nền mang version mới; không tự đè draft. `409 VersionConflict` khóa ghi tiếp, giữ bản nhập và yêu cầu chủ động tải lại cả tài nguyên/membership. Không lấy version mới rồi tự retry ghi. Success cập nhật detail/versions đã xác nhận, đánh dấu các query liên quan stale, bao gồm user department lookup, Ticket category lookup và Ticket lists.

`NameInUse`, `MembershipExists`, `InvalidDepartment`, `InactiveTeam`, `InvalidUser`, `InvalidLeader`, `InvalidParent`, `CategoryCycle`, `InsufficientScope` có thông báo cùng traceId. 429 tôn trọng Retry-After. Không tự retry POST/PATCH/PUT/DELETE khi timeout/network/5xx hoặc receipt chưa xác định; tạo mới cần kiểm tra theo tên/phạm vi trong danh sách trước khi mở yêu cầu khác. Response về muộn sau logout/rời workspace không được điều hướng hoặc cập nhật private cache.

Command hook chung ở `shared/hooks/use-api-command.ts`; module tài khoản bước 7 giữ wrapper tên cũ và cùng hành vi. Credential vẫn chỉ ở auth memory. Chưa tích hợp realtime tổ chức: thay quyền/leadership từ phiên khác được kiểm tra ở HTTP request/refetch/focus tiếp theo, backend tiếp tục kiểm tra trong transaction ghi.

## Chạy và kiểm tra

Restart API Visual Studio **Container (Dockerfile)** để nạp endpoint/DTO đọc mới, chạy `npm run dev` từ `fe/`. Không cần migrate, seed lại hoặc sửa `.env`. Tài khoản cần grant hiện tại tương ứng; seed không tự khôi phục permission đã gỡ.

```powershell
cd fe
npm run lint
npm test
npm run build
cd ..
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~DepartmentAdministration|FullyQualifiedName~SupportTeamAdministration|FullyQualifiedName~TicketCategoryAdministration|FullyQualifiedName~FrontendCatalogJourney'
```

Frontend tests kiểm tra permission/direct link, scope trước members, filter/pagination trên URL, duplicate/cycle, parent inactive, version conflict giữ draft, không retry khi kết quả chưa rõ, hai version membership, leadership, DELETE 204 rồi lỗi đọc và response muộn sau logout. Backend tests dùng PostgreSQL tạm kiểm tra scope/capability/lookup, không lộ identity ngoài scope, eligibility leader và parent lịch sử.

`FrontendCatalogJourneyTests` chạy chính API/auth TypeScript của ứng dụng qua loopback HTTP tới backend và PostgreSQL test: tạo phòng ban/team/category, scope leader, thêm/gỡ/kích hoạt membership, version conflict, tạo/phân công Ticket từ dữ liệu mới, tắt cha/phòng ban và xác nhận liên kết Ticket không đổi. CI backend đã cài Node/npm dependencies nên test này không cần secret/job mới.

Browser mode tùy chọn dùng các biến giống [bước 7](frontend-accounts.md): `HELPDESK_PLAYWRIGHT_MODULE`, `HELPDESK_FRONTEND_URL`, `HELPDESK_BROWSER_CHANNEL`, `HELPDESK_BROWSER_ARTIFACTS`. Bật Vite rồi chạy filter `FrontendCatalogJourney`; credential fixture truyền stdin, browser forward toàn bộ API sang application/container tạm. Không thử trên database Helpdesk thật. CI mặc định chạy nhánh client/API; Playwright local không là dependency runtime.

Nghiệm thu local ngày 29/10/2026: toàn bộ frontend 311 tests/25 files đạt; 77 integration tests backend phòng ban/team/category cùng CLI smoke checks đạt; `FrontendCatalogJourneyTests` đạt cả nhánh client/API và browser mode Edge với ba context admin/leader/requester. Browser đã tạo phòng ban/team, thêm/chỉ định/gỡ/kích hoạt thành viên, tạo cây danh mục và dùng node con vừa tạo trên form Ticket. Viewport 390px không tràn trang; vùng bảng cuộn ngang riêng, focus bàn phím hoạt động và không có browser console/page error. Không chạy toàn bộ suite backend hoặc thao tác trên database Helpdesk cá nhân trong lần thay đổi này.

[Bước 9 — phân công và workflow](frontend-ticket-workflow.md) đã bổ sung lookup team/agent riêng theo Ticket cho `ticket.assign`; không dùng các API quản trị team ở đây như một quyền thay thế.
