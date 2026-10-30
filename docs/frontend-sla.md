# Frontend bước 11 — SLA trên Ticket và quản trị policy

Đã bổ sung panel SLA vào Ticket detail và các trang quản trị policy. Giữ nguyên React/thư viện hiện có và .NET/EF/Npgsql 8.x. Không có migration, package, permission hoặc biến môi trường mới.

## Panel SLA của Ticket

Panel đọc `GET /api/v1/tickets/{id}/sla` sau khi phiên, quyền đọc và detail đã được xác minh. Quyền quản trị SLA không bắt buộc để đọc snapshot Ticket trong phạm vi; có tên role/assignee không thay thế read ACL. 403/404 yêu cầu boundary kiểm tra lại tài nguyên, ẩn dữ liệu cũ trong lúc kiểm tra; lỗi đọc tạm thời có nút tải lại.

Hiển thị tên policy, priority và hai mục tiêu **khi áp dụng**, ngày áp dụng, deadline, first public response, resolved và các marker vi phạm. Snapshot cũ thiếu tên/priority/target được ghi rõ thiếu dữ liệu lịch sử; không gọi policy hiện tại để điền vào. Link tham chiếu policy hiện tại chỉ có khi user có `ticket.manage.sla`.

- `200` với `sla: null` là **Yêu cầu chưa có SLA**, khác với lỗi API. Seed Ticket mẫu không tự có snapshot; policy tạo/bật sau đó không backfill Ticket cũ.
- Cờ `firstResponseBreached` / `resolutionBreached` lấy nguyên từ API. UI không so đồng hồ trình duyệt để kết luận đạt/vi phạm. Cờ false hiển thị **Chưa ghi nhận vi phạm SLA**, không coi pending/stopped là đã đạt.
- Marker là thời điểm command/worker ghi nhận vi phạm đầu tiên. Cờ true nhưng marker null vẫn hiển thị vi phạm và giải thích chưa có marker; không thay marker bằng deadline.
- Snapshot/deadline giữ nguyên khi policy đổi. WaitingForRequester không pause SLA 24/7. Internal note, file và resolution summary không tính first public response. Resolve/reopen/terminal theo quy tắc [backend](backend-sla.md).
- Timestamp UTC được format theo timezone trình duyệt của người dùng, có nhãn IANA timezone và mốc đánh giá server `asOf`. Hiện profile chưa có tùy chọn timezone riêng. Không gửi local deadline về backend.

Response đọc SLA được bổ sung **`status` và `asOf`**; backend đọc status/snapshot/ACL trong một transaction RepeatableRead để tránh trộn dữ liệu khi resolve/reopen đồng thời. Đây là thay đổi DTO bổ sung field, không đổi schema hay quy tắc nghiệp vụ.

Query key chứa session/scope và Ticket; `gcTime=0`, không giữ snapshot qua đổi user/logout. Panel ẩn dữ liệu trong lúc refetch, cập nhật khi mở/focus và mỗi 60 giây khi tab active và Ticket có snapshot. Public/internal comment thành công invalidate resource nên panel đọc lại first response; workflow đọc lại sau commit. Chưa kết nối SignalR ở bước này; **Tải lại SLA** vẫn dùng được khi worker mới ghi marker.

## Quản trị policy

Menu **Chính sách SLA** và các route chỉ mở với `ticket.manage.sla` từ profile hiện tại; không suy từ SystemAdmin và không yêu cầu thêm quyền đọc Ticket:

| Route frontend | Chức năng / API |
| --- | --- |
| `/admin/sla-policies` | GET danh sách cả active/inactive, phân trang server; page/pageSize trên URL |
| `/admin/sla-policies/new` | POST tên, priority, firstResponseMinutes, resolutionMinutes; policy mới active ngay |
| `/admin/sla-policies/:id` | GET chi tiết; PATCH bốn field cùng version; PUT `/{id}/status` với isActive/version |

Tên bắt buộc tối đa 200 ký tự, không yêu cầu duy nhất. Target là số phút nguyên từ 1 đến 525.600, phản hồi không lớn hơn giải quyết. Mỗi priority chỉ có một policy active; server kiểm tra cả khi tạo, sửa priority và activate. Không lấy một trang danh sách để suy ra priority nào đang trống. Không thêm tìm kiếm/sort/filter hay xóa/reapply khi API chưa hỗ trợ.

Form có xác nhận trước khi ghi. Version lấy từ server; UI không tự tăng. Bật/tắt không lưu kèm bản nhập tên/target chưa gửi. Khi đã có draft hoặc mở xác nhận, một lần refetch không tự thay baseline bằng version mới.

- `409 VersionConflict`: giữ draft, khóa ghi; **Tải lại để xem xét** hiển thị tên/trạng thái/priority/targets/version mới; **Dùng phiên bản mới để tiếp tục** là quyết định chủ động trước lần gửi kế tiếp.
- `409 ActivePolicyExists`: giữ draft, cho chỉnh priority hoặc ngừng policy active cũ, không tự tắt policy khác.
- `400` gắn validation vào field; `429` giữ draft và chờ Retry-After; traceId hiển thị để tra cứu.
- Network/timeout/5xx hoặc receipt không hợp lệ: kết quả có thể chưa rõ, khóa command trong workspace, không retry POST/PATCH/PUT tự động. Đối chiếu danh sách/chi tiết trước khi chủ động mở lại workspace.
- Logout/đổi scope hủy command và bỏ response muộn; draft/credential không vào Query mutation cache, URL hay browser storage. Mất grant hoặc detail trả 403/404 sẽ bỏ nội dung và cache detail.

## Chạy và nghiệm thu

1. Restart API Visual Studio **Container (Dockerfile)** để nạp DTO đọc SLA mới; database đã migrate SlaMvp thì không cần migrate/seed lại hoặc sửa `.env`.
2. Chạy `npm run dev` trong `fe/`. Login tài khoản có `ticket.manage.sla`, mở **Chính sách SLA**, tạo policy phù hợp với cam kết thực tế cho priority muốn dùng. Nếu menu không có, kiểm tra grant hiện tại `/users/me`; seed không ghi đè role đã tùy chỉnh.
3. Requester tạo Ticket mới cùng priority. Mở detail thấy snapshot/deadline; Ticket tạo trước policy vẫn không có SLA. Sửa/ngừng policy rồi kiểm tra deadline Ticket cũ không đổi.
4. Phân công cho Agent hợp lệ, chuyển InProgress/Waiting, gửi internal note rồi public comment: chỉ public response hợp lệ ghi firstRespondedAt. Resolve/reopen giữ deadline gốc. Hai phiên sửa cùng policy phải giữ draft và yêu cầu review khi conflict.

Kiểm tra frontend từ `fe/`:

```powershell
npm run lint
npm test
npm run build
```

Kiểm tra tích hợp từ root, dùng PostgreSQL/MinIO GUID tạm của runner, không đụng database Helpdesk:

```powershell
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~SlaTests|FullyQualifiedName~SlaRulesTests|FullyQualifiedName~SlaReadConsistencyTests|FullyQualifiedName~FrontendSlaJourneyTests'
```

`FrontendSlaJourneyTests` chạy auth/typed client TypeScript thật qua API/PostgreSQL: grant riêng, own/team/outside scope, policy version/duplicate active, snapshot/no backfill, status, public/internal response và resolve/reopen. `SlaReadConsistencyTests` cố tình đổi status giữa hai query để kiểm tra response nhất quán. Factory tắt worker tự động; các tests backend SLA hiện có điều khiển clock/worker để kiểm tra breach/idempotence/rollback.

Browser mode local tùy chọn theo cách ở [nghiệm thu workflow](frontend-ticket-workflow.md): đặt `HELPDESK_PLAYWRIGHT_MODULE`, `HELPDESK_FRONTEND_URL=http://localhost:5173`, `HELPDESK_BROWSER_CHANNEL=msedge` và tùy chọn `HELPDESK_BROWSER_ARTIFACTS`, chạy filter `FrontendSlaJourneyTests`. Script dùng tài khoản test qua stdin và giữ thông tin xác thực ngoài log.

Kết quả kiểm tra khi hoàn thành bước 11: **395 tests frontend / 29 files**, lint/build frontend và build backend Release đạt (0 warning/error); **85 tests backend tập trung** cho SLA/workflow/file và hai journey frontend đạt, cùng smoke seed/grant trên runner tạm. Đã nghiệm thu Edge với API/PostgreSQL tạm: tạo/đọc/sửa/status policy, phiên sửa cạnh tranh, public/internal response, timezone America/New_York và Ticket panel 390px không tràn ngang/không lỗi console. CI chạy nhánh TypeScript/API; browser mode vẫn tùy chọn local. Không chạy lại toàn bộ backend suite trong bước này.

Tiếp theo: bước 12 — danh sách thông báo, số chưa đọc và đánh dấu đã đọc; sau đó bước 13 tích hợp SignalR vào vòng đời phiên/cache.
