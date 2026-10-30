# Frontend bước 12 — Notification lưu trong database

Đã triển khai chuông/số chưa đọc trên header, inbox `/notifications` và chi tiết `/notifications/:id` bằng REST hiện có. Không thêm migration, permission, API nghiệp vụ, package hoặc biến môi trường. Giữ React/thư viện hiện tại và .NET/EF/Npgsql 8.x.

## Trải nghiệm và contract

| Giao diện | API |
| --- | --- |
| Chuông trên header | `GET /api/v1/notifications/unread-count` |
| Inbox, bộ lọc và phân trang | `GET /api/v1/notifications?page=1&pageSize=20&unread=true` |
| Chi tiết | `GET /api/v1/notifications/{id}` |
| Đánh dấu một thông báo đã đọc | `PUT /api/v1/notifications/{id}/read`, không có body |

Chỉ cần phiên đã xác nhận; không yêu cầu tên role hoặc permission quản trị để mở inbox. Backend lấy owner từ phiên, lọc quyền Ticket/visibility trước count và phân trang. User không có quyền đọc Ticket nào vẫn có thể mở inbox rỗng. Không gửi userId/owner, không truy vấn mọi Ticket để tự dựng notification.

- Chuông có accessible name chứa số chưa đọc đầy đủ; hiển thị `99+` khi số lớn. Khi đang tải/lỗi, dùng dấu `—` và nhãn chưa xác nhận, không hiển thị 0 giả. Bấm chuông hoặc menu **Thông báo** mở inbox.
- Inbox có tất cả/chưa đọc/đã đọc, page/pageSize trên URL và browser Back. Omit unread nghĩa là tất cả; true/false là hai bộ lọc riêng. Dùng count/page và thứ tự server trả về, không sort hoặc đếm cục bộ.
- Mỗi hàng hiển thị title cố định do API trả, public/internal và trạng thái đọc, ngày tạo/đọc theo timezone trình duyệt. Không lấy nội dung comment/file/description để làm preview; không render HTML từ server.
- Chi tiết giữ đường dẫn quay lại bộ lọc inbox. Xem chi tiết không tự đánh dấu đã đọc; người dùng chủ động chọn nút. Link **Mở yêu cầu liên quan** đi qua route/access boundary của Ticket và vẫn có thể nhận 403/404 nếu quyền vừa thay đổi.
- `200` với danh sách rỗng khác lỗi mạng. Seed Development không tạo notification lịch sử. Thông báo mới được persist khi API phát sinh activity cho người nhận phù hợp, trừ actor; không cần SignalR hoặc có mặt online lúc activity xảy ra.

Không thêm mark-all, delete, đánh dấu chưa đọc, gửi tùy ý hay endpoint nội dung activity khi backend chưa hỗ trợ.

## Đồng bộ trạng thái đọc và lỗi

PUT nhận notification ID hiện tại, không body/owner/version/readAt. Backend giữ timestamp đọc đầu tiên khi gọi lặp. API client kiểm tra receipt đúng ID, ticket/activity hợp lệ và readAt có giá trị trước khi xác nhận thành công; chỉ các field DTO đi vào cache.

Sau receipt thành công, hủy GET list/count/detail đang chạy, cập nhật detail nếu đang quan sát và invalidate/refetch các query notification hiện hoạt. Không tự giảm count hoặc trừ hàng khỏi danh sách: thông báo có thể đồng thời bị ẩn, có thêm activity hoặc trang đang lọc đã đọc. Trong lúc refetch ẩn nội dung cũ; sau đó danh sách và chuông dùng kết quả server. Page ngoài phạm vi có thể rỗng sau mutation; giữ URL và phân trang để người dùng về trang trước.

Chặn command trùng khi pending; command không vào Query mutation cache. Không tự retry PUT ở tầng mutation. `401` dùng refresh rotation một request chung của auth; `403/404` hiển thị thông báo không còn khả dụng và kiểm tra lại list/count. Detail bị từ chối được gỡ cache và tắt đọc nền trong workspace đó; title/link cũ không còn render. `429` chờ Retry-After; lỗi chung có traceId.

Network/timeout/5xx hoặc receipt không hợp lệ có thể chưa xác định kết quả. Giao diện giữ thông báo lỗi, đọc lại trạng thái và khóa ghi trong workspace hiện tại; nếu vẫn chưa đọc, người dùng có thể mở lại trang rồi chủ động thử lại. API mark-read idempotent giữ timestamp lần đầu. Lỗi GET sau khi PUT đã commit không làm UI báo PUT thất bại hoặc gửi lại mutation.

## Cache và cập nhật khi không có SignalR

Query keys gồm session/permission scope, loại dữ liệu, ID hoặc bộ lọc/page. `gcTime=0`, không giữ trang trước khi đổi filter; response đã bị hủy không vào cache dù adapter trả muộn. Logout/đổi user/quyền hủy request và xóa cache cũ. Sau khi một phiên còn hợp lệ đổi permission scope, header chỉ được tải count của scope mới; các cache quản trị/Ticket trước đó vẫn bị xóa.

Header chỉ mount chuông khi auth/profile đã xác nhận, ẩn nó trong lúc kiểm tra profile hoặc khi profile lỗi. Không fetch nội dung riêng trước guard. Metadata nội bộ không được client suy từ role/assignee; backend loại requester khỏi notification internal kể cả có grant internal. Khi membership đổi mà permission strings không đổi, REST vẫn đánh giá ACL và ẩn các bản ghi không còn đọc được.

List/detail/count refetch khi mount, focus/reconnect và mỗi 60 giây tại tab đang hoạt động; inbox có nút tải lại thủ công. Đây là lớp REST polling dự phòng. [Bước 13](frontend-realtime.md) đã nối event ID vào cùng query keys/lifecycle và một connection theo phiên; REST vẫn là nguồn dữ liệu chuẩn khi hub mất kết nối.

## Chạy và nghiệm thu

Database đã có NotificationDelivery/SlaMvp thì **không cần migrate/seed lại**. Chạy backend hiện tại và `npm run dev` trong `fe/`; đăng nhập rồi mở chuông hoặc `/notifications`.

Nghiệm thu hai tài khoản:

1. Requester tạo Ticket, coordinator phân công cho Agent hợp lệ. Người thực hiện không nhận notification từ chính activity của mình.
2. Coordinator/Agent gửi public comment: requester có read.own nhận thông báo. Gửi internal note: chỉ người có scope/grant internal và khác requester nhận; không có text note/file trong DTO.
3. Requester lọc chưa đọc, mở detail, đánh dấu đã đọc; count/list thay đổi theo GET. Mở Ticket vẫn kiểm tra ACL.
4. Deactivate membership của Agent khi đang xem thông báo: detail/mark-read bị từ chối, title/link cũ bị bỏ và count/list không còn tính Ticket đó. Restore quyền có thể làm notification đã persist xuất hiện lại, không tạo bù lịch sử.

Kiểm tra frontend từ `fe/`:

```powershell
npm run lint
npm test
npm run build
```

Kiểm tra backend/client thật từ root, trên container PostgreSQL/MinIO GUID tạm của runner:

```powershell
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~NotificationTests|FullyQualifiedName~FrontendNotificationJourneyTests|FullyQualifiedName~SlaReadConsistencyTests'
```

Journey chạy auth và typed clients TypeScript qua API thật: persist khi offline, paging/unread/count, PUT không body/idempotence, owner/visibility, mất/khôi phục membership. Credential test truyền qua stdin, không qua command arguments/log. Không đụng database Helpdesk đang dùng. CI hiện có Node/PostgreSQL nên tự chạy journey trong backend job; không thêm secret/job.

Browser mode local tùy chọn: khởi động `npm run dev`, đặt `HELPDESK_PLAYWRIGHT_MODULE` tới Playwright đã cài ngoài repo, `HELPDESK_FRONTEND_URL=http://localhost:5173`, `HELPDESK_BROWSER_CHANNEL=msedge` và tùy chọn `HELPDESK_BROWSER_ARTIFACTS`; chạy filter `FrontendNotificationJourneyTests`. Browser test chặn hub, kiểm tra chuông bằng keyboard, filter/detail/read/count/Ticket link, mất quyền và viewport 390px. CI hiện chạy TypeScript/API, browser mode vẫn local.

Kết quả khi hoàn thành: **422 tests frontend / 30 files** (27 tests mới cho notification), lint/build frontend đạt; **21 tests backend tập trung** và smoke seed/grant trên container tạm đạt. Edge đã kiểm tra các luồng trên, không tràn ngang ở 390px và không có lỗi trang/console ngoài HTTP 404 chủ động trong bài mất quyền. Phần 11 được kiểm tra lại qua toàn bộ tests frontend và nhóm SLA read consistency. Không chạy lại toàn bộ backend suite trong bước này.

[Bước 13 — SignalR theo phiên](frontend-realtime.md) và [bước 14 — dashboard/báo cáo](frontend-reporting.md) đã triển khai; tiếp theo là bước 15 nghiệm thu toàn luồng và chuẩn bị phát hành.
