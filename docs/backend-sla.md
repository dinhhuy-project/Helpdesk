# SLA MVP — phần 16

[Rà soát trước phần 17](sla-completion-review.md) đã xác nhận code/tests/schema đạt MVP. [Dashboard/báo cáo](backend-reporting.md) đã có outcome SLA theo ACL; policy active vẫn là cấu hình nghiệp vụ cần chọn, không tự seed mức cam kết.

SLA chạy trong backend .NET 8, dùng PostgreSQL và [notification/outbox phần 15](backend-notifications.md). Cần [review/apply SlaMvp](sla-mvp-review.md) sau NotificationDelivery trước khi F5. Không tự migrate database hoặc tự tạo mức cam kết cho doanh nghiệp.

## Quy tắc đã chốt

- Thời gian tính theo **phút UTC liên tục 24/7**, kể cả cuối tuần/ngày nghỉ; WaitingForRequester không tạm dừng SLA.
- Mỗi priority có tối đa **một policy active**. Name không bắt buộc duy nhất; inactive policies được giữ để bảo toàn lịch sử. Mỗi target từ 1 đến 525.600 phút và FirstResponseMinutes không lớn hơn ResolutionMinutes.
- Khi tạo Ticket qua API, backend chọn policy active cùng priority, copy tên/priority/targets và tính hai deadline từ Ticket.CreatedAt trong transaction tạo Ticket/activity/notification. Không nhận SLA ID/deadline từ body client.
- **Không có policy active phù hợp thì Ticket vẫn tạo được, nhưng không có SLA**. Tạo/activate policy sau đó không gán hồi tố cho Ticket cũ. Muốn có SLA cho Ticket mới, quản trị cần tạo policy active cho priority đó trước.
- Sửa tên/target/priority hoặc deactivate policy không sửa snapshot/deadline của Ticket đã tạo. Không có API reapply/recalculate SLA hoặc thay priority Ticket trong phần này.

First response chỉ là comment công khai từ user active **khác requester**, đang có `ticket.update.status` và phạm vi xử lý Ticket hiện tại theo TicketScope (assignee/leader trong team hợp lệ, hoặc read.all). SystemAdmin đủ quyền có thể phản hồi; chỉ có role SupportAgent hoặc quyền public comment chưa đủ. Requester có thêm role admin vẫn không tự đáp ứng SLA của mình. Internal note, file, đổi trạng thái và resolution summary không được tính là first response.

Chỉ ghi FirstRespondedAt một lần, khi Ticket đang mở xử lý (New/InProgress/WaitingForRequester/Reopened). Comment sau Resolved vẫn theo API thảo luận hiện có nhưng không ghi first response. FirstRespondedAt và comment/activity/notification cùng transaction; append không đổi Version Ticket.

## Resolve, reopen và overdue

| Trường hợp | Hành vi |
| --- | --- |
| Đúng thời điểm deadline | Chưa vi phạm; chỉ vi phạm khi thời gian vượt deadline |
| Phản hồi công khai trễ | Ghi first response, lưu breach và activity ngay trong command; hoàn thành trễ không xóa vi phạm |
| Resolve | Đồng bộ ResolvedAt, dừng cả hai đồng hồ; nếu resolve trễ thì ghi breach ngay, không chờ worker |
| Resolve trước deadline nhưng chưa có public response | FirstRespondedAt vẫn null; đồng hồ dừng tại ResolvedAt, không tự coi summary là phản hồi |
| Reopen | Xóa ResolvedAt, tiếp tục deadline gốc; giữ FirstRespondedAt và breach đã ghi; có thể vi phạm ngay nếu deadline đã qua |
| Closed/Cancelled | Worker bỏ qua, không tạo vi phạm mới; breach đã ghi vẫn là lịch sử |
| Worker tạm dừng | API tính cờ breach theo thời gian hiện tại; command/worker ghi marker và thông báo khi xử lý lại |

Đối với first response, thời điểm kết thúc tính hạn là mốc sớm hơn giữa phản hồi hợp lệ và resolve, hoặc thời điểm hiện tại nếu chưa có cả hai. Resolution dùng ResolvedAt hoặc hiện tại. FirstResponseBreachedAt/ResolutionBreachedAt là **thời điểm phát hiện/lưu vi phạm đầu tiên**, không phải deadline hay timestamp job chạy gần nhất. Marker không được reset khi reopen, không gửi nhắc lại định kỳ trong MVP.

Ticket cũ **đã có TicketSla** tiếp tục dùng deadline đang lưu; các field snapshot bổ sung còn null vì không thể suy ra tên/targets lịch sử từ policy hiện tại. Worker vẫn xử lý những SLA có sẵn đó theo thời hạn; migration không tạo TicketSla mới hoặc phát notification trực tiếp.

## API quản trị policy

Tất cả route dưới yêu cầu Bearer JWT/session còn hiệu lực và **`ticket.manage.sla`**. Đây là cấu hình toàn hệ thống, không giới hạn theo team. Permission đã có trong catalog; SystemAdmin mới có sẵn theo seed. Nếu role cũ đã tùy chỉnh grant, kiểm tra `/api/v1/users/me` và chủ động cấp qua API role/grant; seed không ghi đè grant.

| Method và route | Mục đích |
| --- | --- |
| `GET /api/v1/admin/sla-policies?page=1&pageSize=20` | Danh sách cả active/inactive, tối đa 100/trang; active trước, priority và ID để ổn định thứ tự |
| `GET /api/v1/admin/sla-policies/{id}` | Chi tiết, có Version |
| `POST /api/v1/admin/sla-policies` | Tạo policy active |
| `PATCH /api/v1/admin/sla-policies/{id}` | Cập nhật name/priority/targets, bắt buộc Version hiện tại |
| `PUT /api/v1/admin/sla-policies/{id}/status` | `{ "isActive": false, "version": 123 }` |

Ví dụ POST cho priority Normal (enum API hiện tại: Low=1, Normal=2, High=3, Urgent=4):

```json
{
  "name": "Normal 24/7",
  "priority": 2,
  "firstResponseMinutes": 240,
  "resolutionMinutes": 1440
}
```

Các giá trị trên chỉ minh họa, operator chọn mục tiêu phù hợp. Tạo thêm ba priority tương tự nếu cần SLA cho tất cả Ticket. API không tự seed bốn policy minh họa trong README và dữ liệu mẫu Development cũng không được gán SLA hồi tố.

Response policy: `id`, `name`, `priority`, `firstResponseMinutes`, `resolutionMinutes`, `isActive`, `createdAt`, `updatedAt`, `version`. PATCH nhận bốn field như POST và thêm version. Body strict, field lạ/enum sai/target sai trả 400. Không có grant trả 403, phiên hết hiệu lực 401, ID không có 404, stale Version hoặc trùng active priority trả 409. Chạy lại update/status cùng giá trị và Version hiện tại không ghi audit hoặc bump Version thêm. Không có hard-delete API.

Sửa policy/status dùng xmin, shared advisory lock như Ticket, recheck permission trong transaction và ghi IdentityAuditLog (SlaPolicyCreated/Updated/StatusChanged). Hai create/activate cùng priority không thể cùng thành công; cả DB unique index và application guard bảo vệ rule.

## Đọc SLA của Ticket

`GET /api/v1/tickets/{id}/sla` dùng **read ACL Ticket hiện tại**, không yêu cầu quyền quản trị SLA. Requester được xem SLA của Ticket mình nếu có read.own; user ngoài phạm vi nhận 404. Response có `Cache-Control: no-store`.

```json
{
  "ticketId": "<UUID>",
  "status": 1,
  "asOf": "2026-10-28T12:00:00Z",
  "sla": {
    "policyId": "<UUID>",
    "policyName": "Normal 24/7",
    "priority": 2,
    "firstResponseMinutes": 240,
    "resolutionMinutes": 1440,
    "appliedAt": "2026-10-28T12:00:00Z",
    "firstResponseDueAt": "2026-10-28T16:00:00Z",
    "resolutionDueAt": "2026-10-29T12:00:00Z",
    "firstRespondedAt": null,
    "resolvedAt": null,
    "firstResponseBreached": false,
    "resolutionBreached": false,
    "firstResponseBreachedAt": null,
    "resolutionBreachedAt": null
  }
}
```

Ticket có quyền đọc nhưng chưa có snapshot trả **200 `{ "ticketId": "...", "sla": null, "status": 1, "asOf": "...Z" }`**. `status` và snapshot được đọc trong cùng transaction RepeatableRead; `asOf` là UTC server dùng đánh giá cả hai cờ breach. Các field này được bổ sung khi tích hợp frontend bước 11, không đổi schema. Cờ breach gồm lịch sử marker và tính toán thời gian để UI không phải chờ poll; marker có thể còn null trước lượt worker. Không trả EF entities hoặc lấy target hiện tại của policy để giả lập snapshot. Xem [panel và quản trị policy trên frontend](frontend-sla.md).

## Worker, activity và SignalR

SlaOverdueProcessor chọn batch các snapshot đủ điều kiện, dùng cùng advisory lock 734205001 và khóa Ticket như các command. Trong một transaction, ghi marker lần đầu + TicketActivity + notification. Lỗi SQL rollback toàn bộ batch; lần sau thử lại. Không có network I/O trong transaction. Nhiều worker gọi đồng thời không tạo lại activity/notification cho cùng metric/snapshot; không cần scheduler hoặc container riêng.

Activity mới: **SlaFirstResponseBreached**, **SlaResolutionBreached**, payload whitelist chỉ DueAt/DetectedAt. ActorId null khi do worker, hoặc actor của command khi phát hiện trong phản hồi/chuyển trạng thái. Không có nội dung comment, summary hoặc file.

Worker tạo notification cho user hiện có quyền đọc Ticket, theo outbox phần 15, rồi dispatcher mới phát sau commit. Trong command, người thực hiện vẫn được loại khỏi recipient như quy tắc phần 15. Đây là sự kiện public về SLA, không thông báo nội dung internal. SignalR vẫn phát NotificationChanged/TicketChanged chỉ có ID; client refetch SLA/timeline và kiểm tra lại quyền. Realtime vẫn giới hạn một API instance dù SLA worker có lock DB chống xử lý trùng.

Defaults `Sla` được bind và validate, **không cần thêm .env để chạy**:

| Key | Mặc định | Giới hạn |
| --- | --- | --- |
| `Sla__WorkerEnabled` | true | false chỉ dừng worker, command vẫn snapshot/first-response/resolve và phát hiện vi phạm |
| `Sla__PollSeconds` | 30 | 1–3600 giây |
| `Sla__BatchSize` | 20 | 1–100 Ticket/lượt |

Không có business-hour calendar, pause, SLA reassignment, gần quá hạn/escalation, recurring reminders hoặc backfill trong MVP. Chưa có UI SLA; frontend còn cần tích hợp endpoint và invalidation theo sự kiện. Dashboard/report theo ACL là phần 17.

## Kiểm tra sau migrate

1. Apply SQL đã review rồi Stop Debugging/F5 Container; worker tự chạy trong API, port 5000 như cũ.
2. Login admin có ticket.manage.sla, tạo policy active cho priority muốn thử.
3. User A tạo Ticket mới cùng priority; GET `/tickets/{id}/sla` phải có snapshot đúng.
4. Agent/leader đủ phạm vi gửi public comment; FirstRespondedAt được ghi, internal/requester không được tính.
5. Thử SLA ngắn trong môi trường test: chờ quá deadline, kiểm tra cờ breach, activity và notification. Sửa policy rồi đọc Ticket cũ để xác nhận deadline giữ nguyên.

```powershell
dotnet build be/be.sln --no-restore -c Release -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Tests dùng PostgreSQL/MinIO riêng: UTC/leap-day/boundary, policy permission/version/unique race, snapshot bất biến/no backfill, response ACL/public/once, resolve/reopen, worker batch/idempotence/terminal skip, rollback sau SQL và SQL migration forward/replay/rollback. Không dùng DB Helpdesk thật để thay timestamp hoặc thử drop schema.

Lượt triển khai phần 16: toàn bộ **479/479 tests đạt, 0 skipped**, cùng smoke test CLI Identity seed/grant; build Release `-warnaserror` đạt 0 warning/error. Người dùng sau đó đã apply SlaMvp và [lượt rà soát phần 17](sla-completion-review.md) chạy lại SLA/rules/model 21/21 đạt. Chưa chạy workflow trên GitHub hoặc tích hợp UI SLA.
