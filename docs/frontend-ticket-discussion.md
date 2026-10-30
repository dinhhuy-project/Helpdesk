# Comment, internal note và activity — frontend bước 6

Đã kiểm tra bước 5 bằng 211 frontend tests trước khi triển khai. Trang `/tickets/:id` có danh sách public/internal phân trang, form gửi append-only và activity theo quyền. Comment giữ append-only; [bước 10](frontend-attachments.md) đã bổ sung nút xem/đính kèm file vào comment sau khi lưu, theo author và visibility hiện tại. Subscription SignalR triển khai ở bước sau.

## Capability theo tài nguyên

Bổ sung `GET /api/v1/tickets/{id}/discussion-access`, trả `canReadInternal`, `canCommentPublic`, `canCommentInternal`. API `/access` vẫn giữ ba cờ cũ. Endpoint mới dùng chính `TicketScope.For(ReadInternal/CommentPublic/CommentInternal)` trong một projection SQL; yêu cầu JWT/session/user active và read scope. Ngoài scope/không tồn tại trả 404, thiếu read grant trả 403.

Requester luôn bị loại khỏi internal trên Ticket của mình, kể cả có thêm role SystemAdmin và permission internal. Public write cần read scope, ticket.comment.public và Ticket chưa Closed/Cancelled. Internal cần không phải requester, permission internal và read.all hoặc membership/team/department hợp lệ; write bị khóa khi Closed/Cancelled. Resolved vẫn được trao đổi. Public/internal là hai quyền độc lập; không suy capability từ role, assignee hoặc department ở frontend. POST kiểm tra lại toàn bộ trong transaction.

## Danh sách và gửi nội dung

- Sau access/detail, xác nhận discussion capability rồi mới tải comments/activity. Comments mặc định `page=1&pageSize=20&descending=true&isInternal=false`. Chỉ chủ động đọc `isInternal=true` khi có capability internal và chọn **Nội bộ**.
- Người không đọc internal luôn gửi `isInternal=false` cả với activity. Người được phép lấy activity do backend lọc theo ACL, có nhãn cho action nội bộ. Không lấy body note qua activity hoặc dump raw JSON.
- URL dùng `commentPage` và `comments=internal` (mặc định public), độc lập `activityPage`. Đổi loại hiển thị về trang comment 1, giữ activityPage và navigation state chứa URL danh sách. Invalid filter có thông báo/sửa; requester mở internal deep link không phát GET internal.
- Nội dung render bằng text, có author “Bạn”/“Người dùng”, thời gian local và nhãn Công khai/Nội bộ; note có nền riêng. DTO chỉ có AuthorId, không có tên người khác; không gọi API quản trị user để lấy tên. Có loading/empty/error/traceId, retry GET và **Tải lại thảo luận**. Count/page lấy từ server sau ACL.
- POST `/api/v1/tickets/{id}/comments` chỉ gửi `{ content, isInternal }`. Author từ phiên backend; không gửi authorId/ticketId/version/time/attachment. Content bắt buộc, không toàn whitespace, tối đa 20.000 ký tự trước trim; validation map Content/`$.content` và ProblemDetails.
- Loại gửi độc lập bộ lọc đọc và luôn có mô tả người xem. Đổi loại gửi xóa bản nháp kèm hướng dẫn để tránh chuyển draft nội bộ sang public. Người chỉ có internal cần chọn loại internal trước khi soạn. Closed/Cancelled chỉ đọc lịch sử, không có composer ghi.
- Submit có khóa đồng bộ, disabled controls; chỉ xóa draft sau receipt 201 hợp lệ. Invalidate resource/list trong scope hiện tại, kiểm tra lại access khi tải lại, về trang đầu loại vừa gửi và activity trang 1. Không optimistic chèn note vào public cache, không tự tăng Version Ticket.

Timeout/mất kết nối/5xx/receipt không xác nhận được: giữ draft, báo **Chưa xác định được kết quả gửi**, khóa submit. Tải lại thảo luận để kiểm tra; nút cho phép gửi lại chỉ mở khóa, không POST. Khóa/request đang gửi tồn tại qua resource revalidation. 429 tôn trọng Retry-After; 400 giữ draft; 403/404 revalidate quyền. Auth chỉ replay một lần với 401 xác định, không replay timeout. Backend chưa có idempotency key nên kiểm tra thủ công không bảo đảm tuyệt đối chống trùng sau response mất.

## Cache và quyền thay đổi

Query key phân biệt scope phiên, Ticket, comments/activity, page, sort, visibility; không chứa token/POST body. Server state ở Query, draft chỉ ở React memory. Khi revalidate capability, unmount observer comments/activity để gcTime=0 loại dữ liệu cũ. Mất quyền internal không remount query internal disabled, bỏ internal draft và không giữ note trong query data.

Public draft/khóa submit ở boundary Ticket/scope, giữ qua read revalidation. Scope/ID đổi hủy request/reset draft; late response sau logout không hiện receipt hoặc tái tạo cache. Response comment sai Ticket/visibility bị từ chối trước khi đưa vào Query; backend vẫn là lớp phải chặn nội dung trái phép qua mạng. Comments/activity/capability 403/404 che nội dung Ticket và unmount thảo luận.

## Kiểm chứng

Từ `fe/`: `npm run lint`, `npm test`, `npm run build` đạt; **237 frontend tests**. Ca mới: capability gating, requester nhiều role, internal deep link, URL/pagination riêng, public/internal độc lập, cache/draft khi thu hồi quyền, wrong-visibility response, text an toàn, exact DTO, refresh sau commit, double submit, revalidation khi đang gửi, timeout/network/503, Retry-After, validation và late response sau logout.

Backend build với warning là lỗi đạt. **70 integration tests** create/discussion/activity/capability và client frontend thật đạt trên PostgreSQL test riêng:

```powershell
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~TicketDiscussion|FullyQualifiedName~TicketActivityVisibilityTests|FullyQualifiedName~TicketCreationTests|FullyQualifiedName~FrontendDiscussionJourneyTests'
```

`FrontendDiscussionJourneyTests` chạy application thật qua loopback HTTP bridge, Vite SSR chạy đúng typed API/auth/QueryClient từ source frontend với hai tài khoản. Requester tạo Ticket, fixture admin gán agent hợp lệ; agent ghi internal/public, requester trả lời. So danh tính, ID/count/activity/query data: requester có thêm admin vẫn không nhận body/ID internal trong public cache. Test nằm trong backend CI, cần Node và `npm ci` ở fe như phép thử SignalR đã có.

Đã chạy thêm **Edge headless hai browser context trên API/PostgreSQL test thật**: requester login → tạo bằng UI → agent mở qua danh sách → gửi note/public reply → requester tải lại/trả lời → agent đọc được. Network requester chỉ có GET comments/activity isInternal=false, response không chứa note. Desktop 1440px/mobile 390px không tràn ngang, không lỗi JavaScript. Không dùng database/tài khoản Helpdesk thật.

Browser mode tùy chọn dùng Playwright đã cài trên máy test, không thêm dependency app. Chạy `npm run dev`, rồi tại root trong shell test riêng:

```powershell
$env:HELPDESK_PLAYWRIGHT_MODULE = '<đường dẫn tuyệt đối tới playwright/index.mjs đã cài>'
$env:HELPDESK_FRONTEND_URL = 'http://localhost:5173'
$env:HELPDESK_BROWSER_CHANNEL = 'msedge'
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~FrontendDiscussionJourneyTests'
```

Các biến chỉ dành cho test, không thêm vào .env ứng dụng. Không đặt biến thì test vẫn chạy client frontend thật/API/DB, không mở browser; CI hiện chạy nhánh đó. Browser HTTP forward tới backend test, không giả lập response hoặc gọi API port 5000. Credential truyền qua stdin, không ghi log/command line.

## Sử dụng

Khởi động lại backend Container/F5 để có endpoint discussion-access, chạy frontend và mở detail. Dùng requester và agent có membership/grant hợp lệ; quyền lấy từ backend theo Ticket. Chưa có realtime toàn bộ: bên kia gửi thì dùng **Tải lại thảo luận** hoặc refetch khi quay lại tab.

Không cần migration, grant, package hoặc cấu hình ứng dụng mới. Tiếp theo: **7 — Quản trị tài khoản và quyền** theo [lộ trình](frontend-roadmap.md).
