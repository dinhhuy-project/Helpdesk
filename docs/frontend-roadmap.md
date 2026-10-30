# Lộ trình triển khai frontend Helpdesk

Backend đã có API phần 1–17. Frontend đã hoàn thành [bước 1 — nền tảng](frontend-foundation.md), [bước 2 — auth/session](frontend-authentication.md), [bước 3 — khung ứng dụng/quyền giao diện](frontend-shell.md), [bước 4 — danh sách/chi tiết Ticket](frontend-tickets.md), [bước 5 — tạo Ticket](frontend-create-ticket.md), [bước 6 — comment/internal note/activity](frontend-ticket-discussion.md), [bước 7 — quản trị tài khoản/quyền](frontend-accounts.md), [bước 8 — tổ chức/danh mục](frontend-catalogs.md), [bước 9 — phân công/workflow](frontend-ticket-workflow.md), [bước 10 — file đính kèm](frontend-attachments.md) và [bước 11 — SLA và quản trị policy](frontend-sla.md). Các bước 12–15 dưới đây là thứ tự triển khai tiếp theo, chưa phải tính năng đã hoàn thành.

Mục tiêu đầu tiên là chạy được luồng **đăng nhập → tạo Ticket → xem Ticket → trao đổi**. Sau đó hoàn thiện quản trị dữ liệu, phân công/xử lý, file, SLA, notification và báo cáo. Mỗi bước tích hợp API thật và nghiệm thu quyền truy cập trước khi chuyển tiếp.

## 1. Chuẩn bị và các quyết định nền tảng

- Giữ stack đang có trong `fe/package.json`: React/TypeScript/Vite, React Router, TanStack Query, Axios, Zustand, Tailwind CSS và SignalR. Không cần đổi framework hoặc nâng major version.
- Môi trường local: frontend Vite, API Visual Studio **Container (Dockerfile)** tại `http://localhost:5000`; đối chiếu CORS với origin frontend thực tế. Dùng biến public như `VITE_API_BASE_URL`; không đưa JWT signing key, database hoặc MinIO credentials vào frontend.
- Dùng Swagger và DTO/controller hiện tại để xác nhận method, route, JSON field, enum, pagination, permission và mã lỗi cho từng màn hình. Không lấy các endpoint đề xuất cũ trong README/guides làm contract đã triển khai.
- Chuẩn bị tài khoản kiểm thử riêng cho Requester, SupportAgent, TeamLeader và SystemAdmin; có membership/leadership và grant phù hợp. Có thể tạo bằng Swagger trước khi có giao diện quản trị. Seed mẫu hiện chỉ tạo admin cùng dữ liệu nghiệp vụ, không tạo đủ tài khoản cho các vai trò này.
- Chuẩn bị danh mục khả dụng, team/department active và agent hợp lệ. Tạo policy SLA active nếu muốn kiểm thử SLA trên Ticket mới; seed mẫu không tạo policy/snapshot, comment, file hay notification lịch sử.

### Phiên đăng nhập

Backend hiện nhận/trả refresh token bằng JSON, không dùng cookie xác thực. Kế hoạch ban đầu giữ access/refresh token trong **memory**, không persist token vào localStorage, sessionStorage hoặc Zustand persist. **Tải lại trang sẽ yêu cầu đăng nhập lại** khi mất credential trong memory.

Nếu cần duy trì đăng nhập qua reload, phải chốt thay đổi transport sang cookie HttpOnly cùng HTTPS, CSRF, SameSite và CORS trước khi triển khai trải nghiệm đó. Không thêm chức năng “ghi nhớ đăng nhập” chỉ bằng cách lưu refresh token lâu dài trong browser storage.

### Điểm cần kiểm tra trước form phân công

Hiện `GET /api/v1/support-teams` và danh sách membership thuộc policy `team.manage`. `ticket.assign` không tự cấp quyền đọc các API quản trị này. DTO membership cũng chưa thể hiện đầy đủ grant để client tự xác định agent đủ điều kiện xử lý.

Bước 9 đã bổ sung `GET /tickets/{id}/assignment-teams` và `GET /tickets/{id}/assignment-teams/{teamId}/agents`, lọc theo scope nguồn/đích và eligibility trước count/page. `GET /tickets/{id}/workflow` trả capability và các transition hiện được phép cùng version. Không cấp thêm `user.manage`/`team.manage` để mở combobox và không yêu cầu nhập UUID thủ công. Xem [contract và kiểm thử](frontend-ticket-workflow.md).

## 2. Thứ tự triển khai

Các đường dẫn màn hình dưới đây là route SPA đề xuất; API tiếp tục dùng contract hiện tại dưới `/api/v1`.

| Bước | Hạng mục | Phạm vi và tiêu chí hoàn thành |
| --- | --- | --- |
| 1 | Nền tảng frontend | Tổ chức theo feature; cấu hình môi trường, router/provider, QueryClient, Axios typed client, ProblemDetails/validation/traceId, mapping enum và ngày giờ. Có component dùng chung cho form/table/dialog/pagination/loading/empty/error. Thiết lập test cho hành vi quan trọng ngay từ đầu. |
| 2 | Đăng nhập và phiên | `/login`, gọi login và `/users/me`; refresh rotation chỉ có một request đang chạy, logout, trang tài khoản và tự đổi mật khẩu. Xử lý 401/403/429, hết hạn/thu hồi/user inactive; đổi mật khẩu thành công yêu cầu login lại. Kiểm thử refresh đồng thời và logout trong lúc refresh. |
| 3 | Khung ứng dụng và quyền giao diện | Sidebar/header, nested layout, trang 403/404, điều hướng theo permission hiện tại và hỗ trợ nhiều role. Guard không render dữ liệu trước khi xác thực xong. Có responsive, keyboard/focus và các trạng thái lỗi cơ bản. Thử sớm JS SignalR 10 với server 8 để phát hiện vấn đề tích hợp, chưa triển khai toàn bộ realtime. |
| 4 | Danh sách và chi tiết Ticket | `/tickets`, `/tickets/:id`; tìm kiếm, filter/sort/phân trang phía server; lưu filter/page trên URL. Tách dữ liệu chi tiết, access và activity theo contract. Kiểm thử requester/team/all, Ticket ngoài scope và link trực tiếp; không suy quyền đọc chỉ từ assignee hoặc tên role. |
| 5 | Tạo Ticket | `/tickets/new`; chọn category bằng lookup phân trang, mở cây theo `hasChildren`, nhập dữ liệu đúng DTO. Requester/creator từ phiên, TicketNumber từ database. Sau thành công mở detail và invalidate list. Không tự retry POST khi timeout chưa rõ kết quả. |
| 6 | Comment, internal note và activity | Danh sách phân trang, gửi public comment/internal note, author từ phiên, render activity theo loại được phép. Phân biệt rõ nội dung công khai/nội bộ; requester không tải hoặc giữ nội dung nội bộ trong cache. Comment hiện append-only; không thêm sửa/xóa/đổi visibility. Nghiệm thu luồng Requester tạo Ticket và trao đổi bằng hai tài khoản. |
| 7 | Quản trị tài khoản và quyền | Danh sách/chi tiết/tạo/cập nhật/active user, gán phòng ban hợp lệ; quản lý role assignment, grant của role hiện có và audit theo API. Tách giao diện `user.manage` khỏi `role.manage`, tôn trọng phạm vi cấp role và bảo vệ admin cuối cùng. Deactivate/đổi mật khẩu phải xử lý phiên bị thu hồi; không tự dựng endpoint reset mật khẩu hay thu hồi phiên tùy ý. |
| 8 | Phòng ban, team, membership và category | CRUD được hỗ trợ và deactivate; cây category/reparent/duplicate/cycle; team thuộc department, membership/status/leadership đúng scope. Giữ lịch sử inactive. Đọc `version` mới nhất, xử lý 409; membership dùng cả version team/member khi contract yêu cầu. Nghiệm thu dữ liệu quản trị dùng được trong các form Ticket. |
| 9 | Phân công và workflow | Chốt lookup lựa chọn theo mục 1; chọn team/agent, gửi `/assign` và `/transition` với version server. Hiển thị thao tác theo access/status/permission/context, backend kiểm tra lại. Bắt buộc resolution summary khi resolve; có confirm/reopen/cancel đúng phạm vi. Conflict giữ nội dung người dùng, refetch rồi cho quyết định lại. Không có form sửa tự do title/priority hoặc “tự nhận việc” khi backend chưa hỗ trợ. |
| 10 | File đính kèm | Upload multipart sau khi có Ticket/comment phù hợp; list/metadata/download qua API có Bearer. Giới hạn file theo contract, hiển thị tiến trình/lỗi; file gắn comment giữ đúng visibility/ownership. Download Blob và giải phóng object URL. Không truy cập MinIO trực tiếp hoặc hiển thị object key/public bucket URL. |
| 11 | SLA trên Ticket và quản trị policy | Panel snapshot/deadline/first response/resolved/breach, trạng thái không có SLA; cấu hình policy theo priority với `ticket.manage.sla`, active/version. Hiển thị thời gian theo timezone người dùng, rule dựa API. Policy mới không sửa deadline cũ, Waiting không pause SLA 24/7, internal note không tính first public response. |
| 12 | Notification lưu trong database | Chuông/unread count, `/notifications`, phân trang/lọc chưa đọc, chi tiết và đánh dấu đã đọc. Điều hướng về Ticket và xử lý khi mất quyền. REST hoạt động độc lập khi SignalR ngắt; đánh dấu đã đọc đồng bộ list/count. Không giả định có sẵn notification từ seed lịch sử. |
| 13 | Tích hợp SignalR đầy đủ | Một connection theo phiên; JWT qua `accessTokenFactory`, đăng ký handler trước start, reconnect/refetch/rewatch, cleanup khi rời Ticket/logout. Xử lý ba event hiện có bằng invalidate/refetch, chấp nhận lặp/mất sự kiện. Kiểm thử JS client thực với server, refresh rotation, JWT hết hạn và mất quyền group. |
| 14 | Dashboard và báo cáo | `/dashboard`, `/reports/tickets`; dùng API dashboard/team/agent/daily với filter chung, aggregation và phân trang group từ server. Hiển thị nhóm chưa phân công, status/priority rỗng, chỉ số SLA null khi chưa có mẫu. So số liệu với cùng cohort/ACL/filter; không tính tổng toàn hệ thống bằng cách cộng các trang Ticket. |
| 15 | Nghiệm thu toàn luồng và chuẩn bị phát hành | E2E nhiều tài khoản, kiểm tra internal/file/cache/session/concurrency, responsive/a11y, lỗi mạng và reconnect. Chạy lint/build/tests và bổ sung kiểm tra frontend vào CI khi có test runner. Kiểm tra SPA deep link, URL API/hub/CORS/HTTPS trong môi trường deploy; phối hợp bước 18 backend trước production. |

Các bước 7–8 đặt trước phân công để giao diện quản trị có thể chuẩn bị và sửa dữ liệu tổ chức/tài khoản dùng trong workflow. Giai đoạn 4–6 có thể kiểm thử với dữ liệu đã chuẩn bị bằng Swagger, không phải chờ toàn bộ giao diện quản trị.

## 3. Các mốc nghiệm thu

| Mốc | Sau bước | Luồng bắt buộc chạy được |
| --- | --- | --- |
| A — Nền tảng và phiên | 3 | Login → lấy quyền hiện tại → điều hướng → refresh → đổi mật khẩu/logout; không còn dữ liệu phiên cũ. |
| B — Requester | 6 | Tạo Ticket → thấy mã được sinh → tìm/lọc/xem detail → gửi bình luận; không đọc được Ticket người khác hoặc nội dung nội bộ. |
| C — Quản trị và xử lý | 10 | Tạo user/tổ chức/membership → cấp role đúng phạm vi → phân công → Agent xử lý/public response/internal note/file → resolve → Requester confirm hoặc reopen. Có thử xung đột version bằng hai phiên. |
| D — SLA và realtime | 13 | Tạo policy trước Ticket mới → thấy snapshot → public Agent response đúng điều kiện → resolve/overdue; tài khoản khác nhận notification, offline vẫn đọc được inbox, reconnect lấy lại dữ liệu. |
| E — Báo cáo và chất lượng | 15 | Số liệu đúng scope và bộ lọc, hoàn tất luồng bốn vai trò, không lộ dữ liệu khi đổi quyền/tài khoản; lint/build/tests và kiểm tra deploy đạt. |

Không dùng riêng admin trên Ticket mẫu để nghiệm thu ACL: admin mẫu cũng là requester của các Ticket đó, nên vẫn không được xem nội bộ trên chính các Ticket này.

## 4. Quy tắc tích hợp cần giữ xuyên suốt

### Auth, lỗi và cache

- Một refresh đang chạy cho các request 401 đồng thời; thay cả access/refresh token sau rotation. Loại login/refresh/logout khỏi vòng interceptor, giới hạn replay một lần và không làm sống lại phiên đã logout khi refresh về muộn.
- 403 không kích hoạt refresh lặp; 404 dùng thông báo không tiết lộ Ticket ngoài scope; 409 yêu cầu đọc lại dữ liệu và phân biệt conflict với transition không hợp lệ; 429 tôn trọng Retry-After. Lỗi field gắn vào form, lỗi chung có traceId để tra cứu.
- Query keys phân biệt user/session, resource, filter/page và visibility khi cần; không chứa credential. Khi logout/đổi user, hủy request cũ và xóa cache. Khi quyền/scope thay đổi, loại dữ liệu không còn được phép trước khi render lại; guards/permission phía UI chỉ phục vụ UX.
- TanStack Query giữ server state; Zustand giữ UI/client state cần thiết. Không sao chép danh sách Ticket từ Query sang Zustand. `version` là giá trị do server trả, không tự tăng.
- Sau REST mutation, invalidate các query chịu ảnh hưởng trực tiếp. Chuyển team có thể làm actor mất quyền đọc Ticket vừa cập nhật; receipt thành công rồi GET trả 404 phải được xử lý như thay đổi phạm vi.

### Nội bộ, file và quyền tài nguyên

- `GET /tickets/{id}/access` có ba cờ `canRead`, `canUpdateStatus`, `canAssign`; không phải danh sách mọi transition hoặc quyền nội bộ. Panel dùng `GET /tickets/{id}/workflow` cho thao tác hiện được phép, discussion có capability riêng; backend vẫn kiểm tra lại từng command.
- Role TeamLeader không thay thế membership `IsLeader` hợp lệ của từng team. Assignee không tự có quyền đọc; `user.manage` không bao gồm cấp role/grant. Không hardcode “SystemAdmin làm được mọi việc”.
- Requester bị loại khỏi nội dung nội bộ kể cả khi đồng thời có role quản trị. Không tải nội dung nội bộ rồi chỉ ẩn bằng CSS, không trộn vào cache/preview/notification công khai.
- FormData dùng multipart boundary do browser tạo. File không thành công không đồng nghĩa Ticket/comment chưa được tạo; hiển thị kết quả từng bước, tải lại metadata trước khi thử lại nếu kết quả upload chưa rõ.

### SignalR

- Event hiện có: `NotificationsChanged` không payload; `NotificationChanged` mang notification ID; `TicketChanged` mang Ticket ID. Payload là tín hiệu refetch, không phải dữ liệu nghiệp vụ để cộng/trừ KPI hoặc thay ACL.
- Dùng `WatchTicket` khi mở detail, `UnwatchTicket` khi rời; gọi lại watch sau reconnect, tôn trọng giới hạn 20 Ticket/connection. Không tự đặt tên/join group từ frontend.
- SPA khác origin dùng `withCredentials: false` theo Bearer/CORS hiện tại. Refresh rotation làm session/token cũ mất hiệu lực, cần nối lại bằng token mới. Stop/cleanup khi logout, không tạo connection mỗi render.
- Người thực hiện không tự nhận notification cho activity của mình, vì vậy UI vẫn invalidate sau REST thành công. Reconnect/focus phải refetch dữ liệu cần thiết; realtime hiện giới hạn một API instance.

### SLA và số liệu báo cáo

- Hiển thị snapshot từ Ticket SLA, không dùng policy đang active để tính lại Ticket cũ. Countdown chỉ là hỗ trợ hiển thị; refetch để lấy kết luận từ server, không tự ghi trạng thái breach.
- Dashboard là **trạng thái hiện tại của các Ticket được tạo trong khoảng lọc**; daily là số Ticket tạo theo ngày UTC, không phải số resolve trong ngày. Team/agent là assignment hiện tại.
- Mốc lọc dùng `[createdFrom, createdTo)` và dữ liệu theo `asOf` server. Hiển thị rõ timezone; không biến ngày UTC trong daily thành ngày local rồi gắn nhãn sai.
- SLA `measured = met + breached`; tỷ lệ không có mẫu là null, không phải 0%/100%. WithoutSla, pending, stopped, unknown không được coi là đạt. Dùng số liệu và denominator API trả về.
- Báo cáo refetch sau mutation liên quan, khi mở lại tab/reconnect và theo chính sách stale phù hợp. Không dựa hoàn toàn vào SignalR vì không phải mọi thay đổi đều phát đến mọi người đang xem dashboard.

## 5. Tổ chức code dự kiến

Mở rộng dần theo feature trong `fe/src/`, chỉ tạo file/thư mục khi thực sự dùng:

```text
app/                 router, providers, layouts
features/
  auth/              login, session, me, password
  tickets/           list, create, detail, workflow, discussions, attachments
  accounts/          users, role assignment, grants, audit
  departments/
  teams/             team, membership, leadership
  categories/
  sla/               policy và panel Ticket SLA
  notifications/     inbox, count, SignalR lifecycle
  dashboard/         dashboard và reports
shared/              typed transport, error handling, UI, date/enum helpers
```

Trong mỗi feature, tách typed API, Query hooks, types và UI/pages khi cần. Không gọi Axios trong component chỉ render; không đưa logic phân quyền cuối cùng hoặc xử lý nghiệp vụ database vào frontend.

## 6. Kiểm thử và phạm vi MVP

Kiểm thử đi cùng từng bước, không đợi bước 15 mới bắt đầu. Bước 1 đã thêm Vitest, Happy DOM và Testing Library cho unit/component tests. E2E trình duyệt tự động trên CI sẽ được bổ sung theo luồng auth/Ticket thực tế; browser smoke local chưa thay thế E2E suite.

- Auth tests: nhiều 401 chỉ refresh một lần, refresh thất bại, logout/refresh chạy đồng thời, user inactive, mất quyền, xóa cache/request cũ khi đổi user.
- Ticket tests: scope requester/team/all, internal note/file không lộ, cây category phân trang, resolution required, stale version, assignment mất hiệu lực và mutation làm mất read scope.
- Integration/E2E: requester → điều phối → agent → requester; comment/internal/file; đổi role/membership khi đang mở Ticket; SignalR reconnect/rotation bằng JS client; SLA snapshot và báo cáo đúng cohort.
- Mỗi màn hình có loading/empty/error, validation, chặn submit trùng, keyboard/focus và layout mobile. API write không có idempotency key không được retry mù sau lỗi mạng.
- Khi triển khai code, chạy `npm ci` khi cần cài dependency, `npm run lint`, `npm test`, `npm run build` từ `fe/`; CI đã được cấu hình chạy unit/component tests cùng lint/build.

Ngoài phạm vi frontend MVP hiện tại: OIDC, đăng ký công khai/quên mật khẩu, CRUD role tùy ý, sửa/xóa comment/file, sửa nội dung/priority Ticket tùy ý, tạo Ticket hộ, SLA giờ hành chính, báo cáo trạng thái lịch sử, export CSV/Excel và AI. Các mục này cần contract backend riêng trước khi có UI hoạt động.

## 7. Tài liệu contract theo tính năng

- Phiên và quyền: [authentication](backend-authentication.md), [sessions](backend-sessions.md), [authorization](backend-authorization.md), [accounts](backend-accounts.md).
- Tổ chức: [departments](backend-departments.md), [support teams](backend-support-teams.md), [categories](backend-ticket-categories.md), [dữ liệu mẫu](backend-development-data.md).
- Ticket: [access](backend-ticket-access.md), [Ticket cơ bản](backend-tickets.md), [workflow](backend-ticket-workflow.md), [comments](backend-ticket-comments.md), [attachments](backend-attachments.md).
- Tổng hợp: [notifications/SignalR](backend-notifications.md), [SLA](backend-sla.md), [dashboard/reporting](backend-reporting.md), [query review](report-query-review.md).
- Hạ tầng và vận hành: [setup](backend-setup.md), [GitHub Actions](github-actions.md), [backend roadmap — bước 18](backend-roadmap.md).

**Bước triển khai tiếp theo:** bước 12 — notification inbox, số chưa đọc và đánh dấu đã đọc; bước 13 tích hợp SignalR vào phiên/cache. Các bước 1–11 đã có trang, typed API và kiểm thử; xem tài liệu từng tính năng ở đầu lộ trình.
