# Ticket cơ bản

Phần 11 bổ sung tạo Ticket và hoàn thiện truy vấn danh sách trên [scope phần 10](backend-ticket-access.md). Riêng phần 11 không cần migration, grant hoặc cấu hình mới; phần 12 bổ sung ba quyền workflow, tổng 17 mã, theo [hướng dẫn nâng cấp](backend-ticket-workflow.md). [Phần 14](backend-attachments.md) bổ sung upload journal, tổng model hiện tại 20 entity/sáu migration; cần review/apply AttachmentStorage trước chạy code mới. Profile Container (Dockerfile) vẫn chạy port 5000.

## API và danh tính

Mọi API yêu cầu Bearer JWT với tài khoản active và phiên còn hiệu lực. Controller trả DTO, không trả EF entity.

| API | Quyền và kết quả |
| --- | --- |
| `POST /api/v1/tickets` | `ticket.create`; 201 với receipt và header Location trỏ tới detail |
| `GET /api/v1/tickets` | Một trong read.own/team/all; scope trước count, lọc và phân trang |
| `GET /api/v1/tickets/{id}` | Chi tiết chỉ trong read scope; ngoài scope hoặc không tồn tại cùng trả 404 |
| `GET /api/v1/tickets/{id}/access` | Cờ quyền hiện tại cho Ticket đọc được; command vẫn phải kiểm tra lại |

Tạo Ticket **cho chính mình**: RequesterId và CreatedByUserId đều lấy từ user của phiên đã xác thực. Chưa mở tạo hộ; không suy quyền tạo hộ từ user.manage, read.all hoặc role SystemAdmin. Body có field ngoài contract, kể cả requesterId/createdByUserId/status/assignment/ticketNumber/version, trả 400 với mọi role. Không có endpoint PATCH nội dung; assignment/transition dùng [command riêng phần 12](backend-ticket-workflow.md).

Body tạo:

```json
{
  "title": "Không kết nối được VPN",
  "description": "VPN báo lỗi sau khi nhập tài khoản.",
  "categoryId": "<ID từ lookup danh mục khả dụng>",
  "priority": 2
}
```

Title bắt buộc, tối đa 300 ký tự; description bắt buộc, tối đa 20.000 ký tự. Kiểm tra độ dài đầu vào rồi trim hai field, từ chối toàn khoảng trắng. CategoryId bắt buộc, khác Guid.Empty. Priority dùng enum JSON dạng số: Low=1, Normal=2, High=3, Urgent=4; bỏ qua field thì mặc định Normal. Giới hạn request body 128 KiB, chỉ nhận application/json. Nội dung được lưu như văn bản; frontend cần render bằng text hoặc sanitizer phù hợp khi bổ sung UI.

Ticket mới luôn New=1, chưa có team/agent/resolution, CreatedAt UTC từ server. Category phải tồn tại, active và tất cả ancestor active, nối được tới root; node trong vòng lặp hoặc dưới ancestor inactive bị từ chối. Lookup và create dùng chung truy vấn availability; kết quả lookup cũ không thay thế validation lúc ghi. Category bị deactivate về sau không làm mất Ticket lịch sử hoặc quyền đọc sẵn có.

Receipt trả `id`, `ticketNumber`, `requesterId`, `createdByUserId`, `categoryId`, `status`, `priority`, `createdAt`, `version`. Đây là xác nhận tạo thành công; ticket.create **không tự cấp quyền đọc**. Requester mặc định có read.own nên có thể theo Location; role tùy chỉnh chỉ có ticket.create vẫn nhận 201 nhưng GET trả 403 nếu không có read grant. Quyền đọc phần 10 giữ nguyên.

## TicketNumber và transaction

PostgreSQL sinh TicketNumber bằng BIGINT GENERATED ALWAYS AS IDENTITY và unique index đã có trong InitialCreate. API không tính MAX+1, không nhận số từ client. TicketNumber là số; giao diện có thể định dạng như HD-000001 mà không sửa giá trị lưu trữ. Số có thể có khoảng trống sau rollback và không cam kết thứ tự commit liên tục.

Create mở transaction, lấy advisory lock chung `734205001`, khóa row người gọi rồi đọc lại session, trạng thái account và ticket.create từ database. Cùng quy tắc khóa với quản trị quyền/tổ chức/danh mục: thay đổi đã commit được kiểm tra trước khi ghi. Kiểm tra category trong transaction, save Ticket để nhận số/version, thêm TicketActivity rồi save và commit. Lỗi khi ghi activity rollback cả Ticket; sequence PostgreSQL vẫn có thể đã tăng. Không gọi dịch vụ ngoài khi đang giữ khóa. SQL writer ngoài ứng dụng cũng phải tuân thủ validation và quy tắc khóa này.

Activity Action=`TicketCreated`, ActorId là người gọi, CreatedAt cùng thời điểm Ticket; OldValue null. NewValue JSON chỉ gồm TicketNumber, RequesterId, CreatedByUserId, CategoryId, Status và Priority (enum trong audit lưu tên). Không sao chép title/description vào audit. [API timeline phần 13](backend-ticket-comments.md) áp dụng ACL/visibility trước count/page và trả payload whitelist.

Mỗi POST hợp lệ là một lần tạo mới, kể cả body giống nhau; hiện chưa có idempotency key. Client không tự retry POST sau lỗi mất kết nối khi chưa biết kết quả.

## Lọc, phân trang và sắp xếp

Các filter kết hợp AND và chỉ thu hẹp read scope hiện tại. Truy vấn ID, mã hoặc requester khác không mở rộng quyền. Response gồm items/page/pageSize/totalCount; totalCount chỉ tính Ticket đọc được và khớp filter. Danh sách không trả description, resolution, activity hoặc thông tin nội bộ của user.

| Query | Quy tắc |
| --- | --- |
| `page`, `pageSize` | Mặc định 1/20; page 1–1.000.000, pageSize 1–100; trang vượt dữ liệu trả items rỗng, giữ totalCount |
| `search` | Trim, tối đa 200 ký tự, tìm title không phân biệt hoa/thường; `%`/`_` là ký tự tìm kiếm |
| `ticketNumber` | Số dương, khớp chính xác |
| `status`, `priority` | Giá trị enum hợp lệ (tên hoặc số trong query) |
| `requesterId`, `categoryId`, `teamId`, `agentId` | Guid khác rỗng; agentId khớp AssignedAgentId, teamId khớp AssignedTeamId |
| `unassigned` | true: AssignedAgentId null, gồm cả Ticket chưa có team; false: đã có agent |
| `createdFrom`, `createdTo` | ISO 8601 kèm Z/offset; quy đổi UTC; from bao gồm, to không bao gồm; nếu có cả hai thì from < to |
| `sortBy` | Allowlist createdAt (mặc định), updatedAt, ticketNumber, priority |
| `descending` | Mặc định true; false để tăng dần |

Sort luôn có tie-break Id. updatedAt dùng CreatedAt nếu chưa cập nhật. Priority theo thứ hạng Low < Normal < High < Urgent, không theo tên enum lưu trong PostgreSQL. Offset có dấu cộng cần URL encode thành `%2B`. Không dùng description hoặc tên cột tùy ý làm sort.

Ví dụ GET:

```text
/api/v1/tickets?page=1&pageSize=20&status=New&unassigned=true&sortBy=ticketNumber&descending=false
/api/v1/tickets?createdFrom=2026-10-01T00:00:00Z&createdTo=2026-11-01T00:00:00Z
```

400 cho input/enum/sort/khoảng ngày không hợp lệ hoặc `InvalidCategory`; 401 cho token/phiên/user không hợp lệ; 403 khi thiếu permission. Lỗi use case dùng ProblemDetails với code/traceId, không trả exception/SQL/nội dung request. Không có version từ client trong create; Version/xmin trả về để dùng với command cập nhật sau này.

## SLA và bước tiếp theo

Ticket tạo trong phần 11 được phép **chưa có TicketSla**, kể cả database đã có SlaPolicy. Chưa tính deadline hoặc phát notification/SignalR. [File phần 14](backend-attachments.md) có API riêng sau khi tạo Ticket. Khi triển khai phần 16 cần snapshot SLA trong transaction theo quy tắc đã chốt; không tự gán deadline hồi tố cho Ticket hiện có. [Phần 12](backend-ticket-workflow.md) đã bổ sung phân công/transition, dùng TicketWriteGuard cùng activity/version trong transaction; nếu có SLA thì đồng bộ mốc resolved và giữ nguyên deadline.

## Kiểm chứng

```powershell
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Suite PostgreSQL kiểm tra danh tính lấy từ phiên, overposting cả admin, độ dài/enum, permission/session bị thu hồi, category/ancestor inactive và cycle, giữ lịch sử, TicketNumber khi tạo đồng thời, receipt/Location, activity whitelist và rollback sau khi Ticket đã insert. Query tests kiểm tra các filter, UTC/offset, priority rank, sắp xếp/phân trang và không lộ Ticket ngoài scope. Suite ACL phần 10 tiếp tục chạy. Script dùng PostgreSQL tạm, không apply migration hoặc ghi dữ liệu vào database helpdesk đang dùng; GitHub Actions hiện có tự chạy các test mới.
