# Notification và SignalR

Phần 15 bổ sung API notification và hub `/hubs/notifications`, giữ .NET/EF Core/Npgsql 8.x, port Container 5000 và các permission hiện có. Cần [review/apply NotificationDelivery](notification-delivery-review.md) sau AttachmentStorage trước khi F5. Không tự migrate khi startup, không thêm secret hoặc grant.

## Người nhận và dữ liệu được phép xem

Mỗi activity từ API tạo Ticket, phân công, chuyển trạng thái, thêm comment hoặc hoàn tất attachment tạo notification cho các user **active và đang có quyền đọc Ticket**, trừ người thực hiện. `TicketScope.Readers()` dùng chung điều kiện với API Ticket: read.all, requester + read.own, hoặc read.team + membership/team/department active. Agent không tự có quyền chỉ nhờ AssignedAgentId.

Đối với internal note/file: người nhận còn cần `ticket.comment.internal`, read.all hoặc membership hợp lệ trong team được phân công, và **không phải requester của Ticket**, kể cả requester có thêm role SystemAdmin. Nội dung comment, summary, tên file, mô tả Ticket, email và object key không được nhúng vào notification/sự kiện.

Danh sách, chi tiết, unread count và mark-read kiểm tra owner từ phiên, phiên/user hiện tại, quyền Ticket và visibility activity **trước count/phân trang**. ID không thuộc mình hoặc đã mất phạm vi trả 404. Nếu mất quyền, notification được giữ trong DB nhưng bị ẩn; quyền được khôi phục có thể làm nó xuất hiện lại. Không tạo bù notification lịch sử cho người mới được cấp quyền.

Notification phát sinh theo người đọc hiện tại, nên SystemAdmin có read.all cũng nhận các thay đổi do người khác thực hiện. Membership/phòng ban của user không thay thế membership của support team. Activity lấy trạng thái **sau thay đổi** làm phạm vi; team cũ đã mất quyền không nhận ID Ticket qua realtime.

## REST contract

Tất cả endpoint yêu cầu `Authorization: Bearer <accessToken>` và trả `Cache-Control: no-store`.

| Method và route | Kết quả |
| --- | --- |
| `GET /api/v1/notifications?page=1&pageSize=20&unread=true` | `{ items, page, pageSize, totalCount }`; bỏ unread để lấy cả đã đọc/chưa đọc, false chỉ lấy đã đọc |
| `GET /api/v1/notifications/unread-count` | `{ "count": 2 }`, chỉ đếm notification còn được xem |
| `GET /api/v1/notifications/{id}` | Một notification thuộc phiên và còn trong ACL |
| `PUT /api/v1/notifications/{id}/read` | Không có body; trả DTO với readAt, chạy lặp giữ timestamp lần đầu |

DTO:

```json
{
  "id": "<notification UUID>",
  "ticketId": "<Ticket UUID>",
  "activityId": "<activity UUID>",
  "kind": "PublicCommentAdded",
  "isInternal": false,
  "title": "Ticket có bình luận mới",
  "createdAt": "2026-10-28T12:00:00Z",
  "readAt": null
}
```

Title là nhãn cố định theo loại activity; không đọc text tùy ý từ Title/Content legacy. Kind gồm TicketCreated, TicketAssigned, TicketStatusChanged, PublicCommentAdded, InternalNoteAdded, PublicAttachmentAdded, InternalAttachmentAdded và hai action SLA phần 16: SlaFirstResponseBreached/SlaResolutionBreached. Thứ tự CreatedAt giảm dần, tie-break Id tăng dần; page 1–1.000.000, pageSize 1–100. Query không hợp lệ/body mark-read trả 400; token/phiên hết hiệu lực trả 401; notification ẩn/không tồn tại trả 404 ProblemDetails. Không có endpoint gửi tùy ý, chỉ định owner hoặc join group tùy ý.

## SignalR contract và xác thực

Hub ở `http://localhost:5000/hubs/notifications` khi Debug Container. Client dùng access token từ login/refresh. Bearer được kiểm tra signature/type/issuer/audience/lifetime và phiên/user trong DB như REST. Query `access_token` chỉ được chấp nhận dưới đường dẫn hub cho browser transport; query token không xác thực được REST. Khi có Authorization header, header được ưu tiên.

| Client gọi | Quy tắc |
| --- | --- |
| `WatchTicket(ticketId)` | Kiểm tra JWT/session và read ACL hiện tại; join group do server tạo; tối đa 20 Ticket/connection, gọi lại cùng Ticket an toàn |
| `UnwatchTicket(ticketId)` | Kiểm tra phiên và gỡ subscription của chính connection |
| `Ping()` | Kiểm tra phiên còn hợp lệ |

User group `user:<UUID không dấu gạch>` do server lấy từ principal. Ticket group `ticket:<UUID không dấu gạch>` chỉ được join sau ACL. HubException trả mã chung Unauthenticated/NotFound/WatchLimitExceeded, không lộ nội dung Ticket hoặc DB. Group là subscription, **không phải ranh giới bảo mật**: dispatcher gửi từng connection sau khi kiểm tra lại ACL, không broadcast nội dung vào group có thể còn membership cũ.

| Server event | Payload | Client cần làm |
| --- | --- | --- |
| `NotificationsChanged` | Không có payload, khi connect/reconnect | Tải lại list/unread-count từ REST |
| `NotificationChanged` | `{ "id": "<notification UUID>" }` | Refetch notification/list/count; bỏ qua nếu REST trả 404 |
| `TicketChanged` | `{ "id": "<Ticket UUID>" }` | Chỉ cho connection đang watch và đủ ACL của notification; refetch Ticket/comment/activity/file qua REST |

Mark-read requeue NotificationChanged trên cùng bản ghi để các connection khác cập nhật unread count. TicketChanged cũng có thể được phát lại khi notification thay đổi; client phải chấp nhận lặp và chỉ invalidate/refetch. Người thực hiện không tự nhận notification activity, nên UI cần invalidate trực tiếp sau REST command thành công.

Kết nối đóng khi JWT hết hạn (`CloseOnAuthenticationExpiration` và kiểm tra exp trong registry). Worker định kỳ kiểm tra phiên/user và membership; mất quyền Ticket sẽ gỡ group, phiên thu hồi/user inactive sẽ abort connection. Trước mỗi lần gửi còn kiểm tra lại, nên không chờ lượt poll tiếp theo mới chặn dispatch. Thay đổi grant giữa kiểm tra và network có thể làm một ID đã được phép trước đó tới client; payload không có nội dung và REST luôn kiểm tra lại.

Frontend đã có auth/session và khung ứng dụng; chưa tích hợp inbox hoặc SignalR lifecycle trong SPA. Khi tích hợp: đăng ký handler trước Start, dùng accessTokenFactory lấy token mới trong memory, điều phối refresh một lần, reconnect rồi refetch và gọi lại WatchTicket. Với SPA khác origin, đặt `withCredentials: false` trong options `withUrl` vì API hiện dùng Bearer và CORS không bật cookie credentials. Không lưu nội dung internal trong cache công khai. Ngoài .NET client 8.0.32, [test JS client 10.0.11 với hub .NET 8 qua LongPolling](frontend-shell.md) đã đạt trên DB test riêng; browser WebSocket/SSE và reverse proxy production chưa được xác minh.

## Transaction, retry và quyết định outbox

Thay đổi nghiệp vụ + TicketActivity + notification được lưu trong **cùng transaction** của command. `TicketNotificationWriter` chỉ chạy trong transaction, unique `(user_id,ticket_activity_id)` ngăn trùng một activity/người nhận. Lỗi khi lưu notification rollback cả command. Seed dữ liệu Development ghi trực tiếp bộ mẫu, không phát notification lịch sử.

Chọn dùng **bản ghi notification làm transactional outbox** cho MVP, vì cần khôi phục sau lỗi/crash giữa commit và gửi. Không dùng fire-and-forget hoặc chỉ callback trong process. Worker:

1. Sau commit, claim một batch bằng transaction/advisory lock chung; ghi claim UUID và lease một phút rồi commit.
2. Ra ngoài transaction, kiểm tra token/session/ACL từng connection và gửi ID. Tổng deadline một batch 40 giây, ngắn hơn lease.
3. Ghi DispatchedAt khi đã xử lý; lỗi gửi giữ pending và retry sau khoảng 5 giây. Crash giữ lease và lượt sau nhận lại khi hết hạn.

Mark-read xóa claim cũ và requeue; ack của dispatcher chỉ cập nhật đúng claim UUID, tránh nuốt thay đổi vừa đọc. Retry/crash có thể gửi trùng; Notification ID ổn định để deduplicate/refetch. DispatchedAt chỉ có nghĩa dispatcher xử lý xong, **không phải client đã nhận/đã đọc**. Không có connection cũng đánh dấu xử lý; người offline đọc notification đã persist khi quay lại. Không bảo đảm exactly-once hoặc delivery tới thiết bị, cũng không coi SignalR là nguồn dữ liệu chính.

MVP chỉ hỗ trợ **một API instance** cho realtime: registry ở memory và chưa dùng Redis backplane. Không scale nhiều instance rồi kỳ vọng tất cả connection đều được báo. Trước khi cần delivery đa node, email/webhook hoặc acknowledgment: tách delivery outbox theo kênh/recipient, thiết kế retry/dead-letter/idempotency và backplane phù hợp; PostgreSQL notification vẫn là inbox của user. Xem [quy tắc group SignalR](https://learn.microsoft.com/aspnet/core/signalr/groups) và [xác thực SignalR .NET 8](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz?view=aspnetcore-8.0).

## Cấu hình và vận hành

Defaults được bind/validate từ `Notifications` (không cần sửa .env để chạy):

| Key | Mặc định | Giới hạn |
| --- | --- | --- |
| `Notifications__DispatchEnabled` | true | false tạm dừng gửi nhưng vẫn persist, API vẫn đọc được và validator vẫn kiểm tra connection |
| `Notifications__PollSeconds` | 2 | 1–60 giây |
| `Notifications__BatchSize` | 20 | 1–100 notification/lượt |

Poll không phải cam kết độ trễ: batch dispatch/network backlog có thể kéo dài chu kỳ. Trên môi trường có nhiều người đọc toàn cục, số notification tăng theo số người nhận; theo dõi pending count, lease hết hạn và log retry trước khi mở rộng. Chưa có retention tự xóa lịch sử.

Request-start logging Hosting.Diagnostics đã đặt tối thiểu Warning vì browser gửi access_token trong URL. Reverse proxy/access log/APM cũng phải bỏ hoặc che query token; dùng HTTPS khi deploy. CORS dùng allowlist hiện có; không mở wildcard để phục vụ hub. DB/session lookup lỗi làm worker đóng connection theo hướng từ chối truy cập; notification pending vẫn còn để retry.

## Kiểm thử

```powershell
dotnet restore be/be.sln
dotnet build be/be.sln --no-restore -c Release -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Script dùng PostgreSQL và MinIO tạm, không dùng DB/bucket Helpdesk thật. Suite có HTTP pagination/mark-read/owner/internal ACL; revoke grant/membership/session/user/expiry; create/assign/transition/comment/attachment tạo notification atomic; rollback sau notification SQL; retry/claim đồng thời, mark-read race và không giữ transaction qua I/O. SignalR client thật kiểm tra JWT negotiate, token query bị từ chối ở REST, group không hợp lệ, event sau commit, payload ID-only và thu hồi group/session. Migration được apply/replay/rollback trên DB test; rollback có managed notification bị chặn.

Kết quả lượt triển khai: toàn bộ **461/461 tests đạt, 0 skipped** trên PostgreSQL/MinIO tạm; smoke test CLI seed/grant thành công; build Release `-warnaserror` đạt 0 warning/error. Không chạy CI trên GitHub hoặc apply NotificationDelivery lên DB Helpdesk thật.

[Phần 16 — SLA MVP](backend-sla.md) đã thêm snapshot/first response và hai action notification public SlaFirstResponseBreached/SlaResolutionBreached. Worker ghi breach/activity/notification cùng transaction, dispatcher phần 15 phát sau commit như các sự kiện Ticket khác. Phần tiếp theo: **17 — Dashboard và báo cáo**.
