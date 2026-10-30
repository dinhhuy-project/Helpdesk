# Frontend bước 13 — SignalR theo phiên và cache

Đã nối SignalR JS 10 hiện có với hub .NET 8 `/hubs/notifications`. Giữ phiên Bearer trong memory, API REST và PostgreSQL làm nguồn dữ liệu chuẩn. Không thêm package, migration, permission hoặc secret; không đổi nghiệp vụ backend.

## Vòng đời kết nối

`createAppServices` tạo controller realtime nhưng chưa mở mạng. `RealtimeProvider` ở root ứng dụng attach controller một lần; component feature chỉ đăng ký Ticket đang cần theo dõi. Phiên anonymous/authenticating, profile chưa xác nhận hoặc lỗi không mở kết nối. Cleanup root/logout/đổi user/scope hủy timer, bỏ handler và stop kết nối; event/response cũ không được ghi vào cache phiên mới. StrictMode detach/attach và stop trong lúc start đang chạy được serialize.

JWT đi qua `accessTokenFactory`, lấy từ `AuthSession.realtimeCredential(expectedScope)`. Phương thức này dùng cùng đường `authorize` và refresh single-flight với REST; refresh token vẫn chỉ nằm trong closure auth. Credential revision là số dùng nội bộ để nhận biết rotation, không phải token/claims và không persist vào Zustand, Query hay browser storage.

- Trước JWT expiry, controller lấy lại credential qua auth để proactive refresh, kể cả lúc không có REST request từ trang. Refresh rotation làm access token cũ mất hiệu lực; controller stop instance cũ trước khi start instance mới bằng JWT mới. Mỗi phiên/tab có tối đa một kết nối hoạt động; hai tab/phiên độc lập có thể kết nối riêng.
- Đăng ký đủ ba handler trước `start`. SDK automatic reconnect dùng delay 0/2/10/30 giây; hết lượt thì controller retry start với backoff 2/5/10/30 giây, tối đa 30 giây/lượt. Initial start cũng có backoff vì SDK không tự retry bước này.
- Reconnect/connect mới kiểm tra profile qua REST, refetch dữ liệu persist và đăng ký lại các Ticket còn được quan sát. JWT hết hạn/thu hồi/user inactive được xử lý bằng auth/REST hiện có, không suy trạng thái phiên chỉ từ chuỗi lỗi SDK hoặc tạo vòng refresh riêng.
- Inbox hiển thị trạng thái kết nối và nút kết nối lại khi offline. REST polling mỗi phút, focus/reconnect và tải lại thủ công của phần 12 vẫn hoạt động. Focus/online/visibility cũng yêu cầu catch-up và kiểm tra lại watch.

SDK logging tắt (`LogLevel.None`); không lưu exception SDK nguyên bản vì có thể chứa URL `access_token`. Không đưa token vào query keys, props hoặc nội dung hiển thị.

## Sự kiện và dữ liệu

| Event | Xử lý frontend |
| --- | --- |
| `NotificationsChanged` | Refetch inbox/count/detail đang quan sát, danh sách Ticket và các Ticket đang watch; dùng để khôi phục sau connect/reconnect |
| `NotificationChanged {id}` | Validate UUID, invalidate/refetch notification/inbox/count và danh sách Ticket; không tự giảm count hoặc đánh dấu readAt |
| `TicketChanged {id}` | Chỉ xử lý UUID thuộc Ticket đang watch trong scope hiện tại; kiểm tra lại access trước nội dung |

Các event gần nhau được gộp trong cửa sổ 150 ms. Chấp nhận trùng; không deduplicate vĩnh viễn theo notification ID vì mark-read có thể phát lại cùng ID. Event đến trong lúc refetch sẽ được xử lý tiếp sau đó. Payload không thành nội dung comment/file, DTO hoặc KPI và không được dùng để cấp quyền. Không fetch notification/Ticket ngoài màn hình chỉ vì nhận được một ID.

Mất event không làm mất dữ liệu đã commit: connect/reconnect/focus đọc lại REST, inbox còn polling và các REST command vẫn invalidate trực tiếp. Người thực hiện không tự nhận notification activity của mình. Không coi SignalR là nguồn cập nhật duy nhất hoặc cam kết exactly-once.

## Watch Ticket và ACL

`useTicketWatch` nằm tại access boundary của Ticket, chỉ đăng ký sau khi API cho phép đọc. Desired watches được reference-count; tối đa 20 Ticket khác nhau, không nhận tên group/user từ UI. Rời trang hoặc mất access gỡ watch; release trong lúc WatchTicket còn pending sẽ Unwatch ngay khi invocation hoàn tất. Reconnect/rotation đăng ký lại các Ticket còn mở.

Khi có TicketChanged/catch-up, hủy GET cũ và đánh dấu resource stale, refetch access trước. Boundary ẩn/unmount nội dung riêng trong lúc xác minh. Nếu không còn đọc được thì bỏ cache resource ngoài access; nếu vẫn được đọc thì refetch detail/workflow/comment/activity/file/SLA hiện hoạt. Query không còn observer không được tải để điền cache. Command/draft của workflow, comment và file ở boundary được giữ qua revalidation; không replay POST hoặc tự gửi lại bản nhập.

Mỗi 30 giây khi kết nối còn sống, Ping kiểm tra phiên và WatchTicket kiểm tra lại phạm vi các Ticket đang mở. Watch bị từ chối yêu cầu REST xác minh ACL, không dựa vào tên role hoặc AssigneeId. Điều này phát hiện membership/team/department thay đổi dù các permission strings trong profile không đổi. Worker backend cũng loại group/connection không còn hợp lệ và kiểm tra lại trước dispatch; việc không còn nhận event không tự cấp hoặc giữ quyền đọc.

Requester không tải nội dung nội bộ do event: các query visibility/capability hiện có vẫn quyết định việc gọi API. Backend tiếp tục kiểm tra owner/session/read/internal ACL tại từng endpoint.

## Cấu hình và chạy

- Hub dùng cùng origin API đã chốt bằng `VITE_API_BASE_URL`; không cần biến hub URL/credential riêng. Client đặt `withCredentials: false`, dùng Bearer và CORS allowlist hiện tại.
- Vite đã thêm proxy `/hubs` với `ws: true`, cùng `API_PROXY_TARGET` đang dùng cho `/api`. **Restart Vite** nếu nó đang chạy trước thay đổi cấu hình. Mở inbox để xem trạng thái kết nối.
- Database đã migrate phần 15/16 thì không migrate/seed lại. API vẫn dùng profile Container port 5000 và worker notification hiện có. `DispatchEnabled=false` vẫn persist nhưng không phát live event.
- Production cần reverse proxy cho `/hubs`, WebSocket upgrade và timeout cho SSE/LongPolling, HTTPS, CORS phù hợp và che query token trong access log/APM. Dev proxy không thay thế cấu hình deploy.

MVP realtime backend vẫn giới hạn **một API instance**; chưa có Redis backplane. Browser production tự thương lượng transport. Các bài tích hợp dưới dùng LongPolling để forward qua TestServer; chưa nghiệm thu WebSocket/SSE qua reverse proxy production.

## Kiểm thử và nghiệm thu

Frontend từ `fe/`:

```powershell
npm run lint
npm test
npm run build
```

Tests lifecycle kiểm tra guard, StrictMode/start-stop, một kết nối, single-flight REST/hub, renew JWT, logout khi refresh đang chạy, initial retry/SDK retry, callback cũ, reference-count/giới hạn watch và rewatch. Cache tests kiểm tra event trùng/lặp lại cùng ID, payload sai, access trước nội dung và loại dữ liệu ngoài scope.

Từ root, runner chỉ dùng PostgreSQL/MinIO GUID tạm và credential fixture qua stdin:

```powershell
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~FrontendRealtimeJourneyTests|FullyQualifiedName~SignalRNotificationTests|FullyQualifiedName~SignalRJavaScriptCompatibilityTests|FullyQualifiedName~NotificationTests'
```

`FrontendRealtimeJourneyTests` chạy chính controller/auth/typed clients TypeScript với JS SignalR 10 và hub .NET 8 thật. Kiểm tra public/internal event, mark-read, refresh rotation và JWT cũ bị từ chối, server disconnect/reconnect/rewatch, activity dispatch khi offline rồi catch-up, mất membership/group và xóa private cache. Một JWT test hợp lệ có lifetime ngắn kiểm tra server đóng kết nối tại expiry; không thay token issuer/config production. Các control endpoint của bridge chỉ tồn tại trong fixture loopback, không vào API ứng dụng.

Browser mode local tùy chọn: chạy `npm run dev`, đặt `HELPDESK_PLAYWRIGHT_MODULE`, `HELPDESK_FRONTEND_URL=http://localhost:5173`, `HELPDESK_BROWSER_CHANNEL=msedge` và tùy chọn `HELPDESK_BROWSER_ARTIFACTS`, chạy filter `FrontendRealtimeJourneyTests`. Bridge chỉ forward HTTP nên browser test giới hạn transport được quảng bá thành LongPolling; business DTO/auth/ACL/event vẫn là server thật.

Đã nghiệm thu Edge: public comment xuất hiện ở tài khoản khác và giữ draft requester; internal note không bị requester tải/giữ; mark-read cập nhật count giữa hai phiên; rời Ticket Unwatch, server disconnect nối lại, membership mất quyền ẩn nội dung và logout đóng đúng connection. Inbox 390px không tràn ngang. CI có Node/PostgreSQL và chạy nhánh JS/API thật; browser mode vẫn local.

Kết quả hoàn thành: **440 tests frontend / 31 files** (18 tests controller/cache mới), lint/build frontend đạt; **20 tests backend tập trung** cùng smoke seed/grant trên container tạm đạt; build solution Release **0 warning/error**. Không chạy lại toàn bộ backend suite. Trình duyệt/JS integration đã xác minh LongPolling; WebSocket/SSE qua reverse proxy production còn thuộc nghiệm thu deployment.

Tham chiếu chính thức: [JavaScript client/reconnect](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client?view=aspnetcore-8.0), [cấu hình xác thực và accessTokenFactory](https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration?tabs=javascript&view=aspnetcore-8.0). Source/version và kiểm thử của repository là căn cứ cho khả năng tích hợp JS 10/server 8 hiện tại.

[Bước 14 — dashboard/báo cáo](frontend-reporting.md) đã triển khai và nhận invalidation qua realtime reconciliation. Tiếp theo: bước 15 — nghiệm thu toàn luồng và chuẩn bị phát hành.
