# Phân công và workflow Ticket

Phần 12 nối command vào TicketWriteGuard phần 10 và [Ticket cơ bản phần 11](backend-tickets.md). Riêng phần 12 không đổi schema/migration/package/secret. [Phần 14](backend-attachments.md) bổ sung MinIO/upload journal và migration riêng. Catalog tăng từ 14 lên **17 permission** với ticket.confirm, ticket.reopen, ticket.cancel. Profile Container (Dockerfile) giữ port 5000; startup seed catalog nhưng bảo toàn grant của role cũ.

## API phân công

`POST /api/v1/tickets/{id}/assign`, Bearer + ticket.assign và resource scope:

```json
{
  "teamId": "<ID nhóm đích active>",
  "agentId": "<ID agent hợp lệ hoặc null>",
  "version": 123
}
```

Thay 123 bằng Version hiện tại từ GET hoặc receipt gần nhất. TeamId bắt buộc, khác Guid.Empty; AgentId null/bỏ qua nghĩa giao nhóm, chưa chọn agent, cũng là cách gỡ agent hiện tại. Muốn đổi team phải gửi lại agent hợp lệ của team mới hoặc null, không tự giữ agent cũ. Không mở clear toàn bộ team, tự nhận việc hoặc PATCH nội dung trong phần này.

Kiểm tra **source trước destination** theo [ma trận phần 10](backend-ticket-access.md):

- Người gọi đọc được Ticket và có ticket.assign; read.all cho phạm vi toàn hệ thống, còn scope nhóm yêu cầu membership leader active ở cả nhóm nguồn và đích, với team/department active.
- Ticket chưa có nhóm hoặc source inactive cần người có read.all + ticket.assign điều phối; TeamLeader không tự có quyền lấy Ticket ngoài scope.
- Team đích tồn tại/active, department active. Agent nếu có phải là user active, membership active trong đúng team, có ticket.update.status và read.team hoặc read.all. User.DepartmentId không thay thế membership.
- Closed/Cancelled không được phân công. Các trạng thái khác giữ nguyên khi phân công; không tự bắt đầu xử lý, kể cả khi chọn agent. Gỡ agent đang xử lý giữ status, nhưng chặn xử lý tiếp cho đến khi phân công hợp lệ lại.

Assignment thay đổi ghi TicketAssigned activity: ActorId từ phiên, old/new chỉ có AssignedTeamId/AssignedAgentId; UpdatedAt UTC và Version được cập nhật. Gửi lại cùng assignment với **version hiện tại** trả 200, giữ Version/UpdatedAt, không ghi activity trùng; version cũ vẫn trả 409. Không suy đây là idempotency key cho mọi lần retry.

## API chuyển trạng thái

`POST /api/v1/tickets/{id}/transition`, Bearer; permission được server chọn theo status đích trong transaction:

```json
{
  "status": 4,
  "resolutionSummary": "Đã cập nhật cấu hình VPN và kiểm tra kết nối thành công.",
  "version": 124
}
```

Status bắt buộc, JSON enum số như API hiện tại. ResolutionSummary chỉ nhận khi đích Resolved; bắt buộc không toàn khoảng trắng, tối đa 4.000 ký tự đầu vào, lưu sau trim. Khi chuyển trạng thái khác, bỏ field hoặc gửi null; gửi nội dung summary cho transition khác trả 400. Không nhận actor/requester/assignment/timestamp/title/priority hoặc field ngoài DTO. Giới hạn body assign 16 KiB, transition 32 KiB; chỉ application/json.

| Từ | Đến hợp lệ | Quyền và actor |
| --- | --- | --- |
| New (1) | InProgress (2) | ticket.update.status + scope xử lý |
| InProgress (2) | WaitingForRequester (3), Resolved (4) | ticket.update.status + scope xử lý |
| WaitingForRequester (3) | InProgress (2) | ticket.update.status + scope xử lý; resume trước khi resolve |
| Reopened (7) | InProgress (2) | ticket.update.status + scope xử lý |
| Resolved (4) | Closed (5) | ticket.confirm + requester hoặc read.all |
| Resolved (4) | Reopened (7) | ticket.reopen + requester hoặc read.all |
| New / InProgress / WaitingForRequester / Reopened | Cancelled (6) | ticket.cancel + requester hoặc read.all |
| Closed / Cancelled | Không có | Trạng thái kết thúc, không mở lại hoặc sửa/phân công |

Mọi thao tác vẫn yêu cầu **read scope hiện tại**, user active và session hợp lệ. Scope xử lý giữ quy tắc ticket.update.status + assignee/leader active trong nhóm, hoặc read.all; đồng thời Ticket phải đang có **team và agent hợp lệ** theo tiêu chí đích phân công. Cả operator toàn cục cũng phải phân công hợp lệ trước khi start/wait/resolve. Membership/grant/team/department mất hiệu lực được kiểm tra lại trong transaction. Requester không tự resume khi gửi thông tin; thao tác đó do processor thực hiện ở giai đoạn hiện tại.

ticket.update.status không cho phép confirm/reopen/cancel. Ba quyền mới chỉ cho requester của Ticket hoặc người có read.all; leader/member của nhóm không tự được dùng trên Ticket người khác dù đã có quyền riêng. Scope không bypass theo tên role SystemAdmin. CreatorId không thay thế RequesterId. Confirm/cancel/reopen cho requester không phụ thuộc assignment/organization còn active, để vẫn xử lý được Ticket lịch sử đọc được; bảng transition vẫn áp dụng.

Domain TicketWorkflow giữ bảng transition và quy tắc resolution. Không cho cùng status hoặc chuyển tắt New → Resolved, Waiting → Resolved, Resolved → InProgress/Cancelled. Khi resolve: ResolvedAt UTC server, ClosedAt null. Confirm giữ summary/ResolvedAt, ghi ClosedAt. Reopen xóa summary/ResolvedAt/ClosedAt hiện tại; bản cũ giữ trong activity. Không sửa nội dung Ticket bằng transition.

`GET .../access` giữ contract ba cờ canRead/canUpdateStatus/canAssign. Hai cờ ghi là capability rộng tại thời điểm đọc, không phải danh sách transition hoặc bảo đảm destination hợp lệ; không đại diện ba quyền requester mới. UI kết hợp status/requester/permissions hiện tại khi bổ sung, backend luôn quyết định lại.

## Response, concurrency và audit

Hai command trả **200 TicketMutationResult**: id/status/assignedTeamId/assignedAgentId/updatedAt/resolvedAt/closedAt/version. Receipt không gọi lại read store sau commit vì chuyển nhóm có thể làm người gọi mất read scope. Version trả về dùng cho command kế tiếp; không tự tăng xmin. Timestamp UTC theo độ chính xác microsecond PostgreSQL.

| Lỗi | Kết quả |
| --- | --- |
| Body/Guid/enum/version thiếu, 0 hoặc không hợp lệ | 400; model validation hoặc InvalidInput |
| Resolve thiếu summary | 400 ResolutionRequired |
| Destination hoặc assignment hiện tại mất hiệu lực | 400 InvalidAssignment |
| Token/phiên/user không hợp lệ | 401 |
| Thiếu grant hoặc operation ngoài scope; Ticket terminal | 403 Forbidden |
| Ticket không tồn tại hoặc ngoài read scope | 404 NotFound như nhau |
| Version cũ sau khi đã xác nhận scope | 409 VersionConflict |
| Đọc/ghi được, version đúng nhưng cạnh transition không hợp lệ | 409 InvalidTransition |

Lỗi dùng ProblemDetails/ValidationProblemDetails với traceId, lỗi use case có code; không trả exception/SQL/nội dung summary. Không đảm bảo lỗi version được trả trước scope: nếu command khác đóng/hủy hoặc đổi scope trước thì request còn lại có thể nhận 403/404. Client tải lại dữ liệu và quyền sau 409, không retry bằng version cũ.

Mỗi command mở transaction, lấy advisory lock chung 734205001 → khóa user → khóa Ticket, reload rồi recheck session/read/operation/version. Phân công kiểm tra lại destination; transition kiểm tra workflow và assignment. Save Ticket + activity (+ SLA hiện có khi cần) và commit trong cùng transaction. Hai command cạnh tranh cùng version chỉ một thay đổi được commit. Writer SQL ngoài ứng dụng phải theo cùng quy tắc; không giữ khóa khi gọi dịch vụ ngoài.

TicketStatusChanged activity lưu old/new: Status (tên enum), ResolutionSummary, ResolvedAt, ClosedAt; ActorId từ phiên, CreatedAt cùng UpdatedAt. Summary là kết quả công khai đã xuất hiện trong detail, **không phải internal note**. Giữ giá trị cũ để không mất lịch sử sau reopen; không đưa title/description hoặc token vào audit/log. [API activity phần 13](backend-ticket-comments.md) đã xuất lịch sử với scope/visibility và payload whitelist, không trả raw JSON audit.

Nếu đã có TicketSla: resolve đồng bộ ResolvedAt, dừng đồng hồ; reopen xóa ResolvedAt và tiếp tục deadline gốc. Giữ snapshot/first response và breach lịch sử. [Phần 16](backend-sla.md) phát hiện vi phạm trong cùng transaction chuyển trạng thái và có worker cho overdue; bỏ qua Closed/Cancelled. Ticket cũ chưa có SLA tiếp tục không có, chỉ tạo mới qua API với policy active phù hợp mới snapshot. [Phần 15](backend-notifications.md) dispatch SignalR sau commit.

## Projection phục vụ frontend workflow

Frontend bước 9 bổ sung ba API đọc, không đổi schema hoặc các lệnh ghi:

- `GET /api/v1/tickets/{id}/workflow`: version/status, assignment hiện tại với tên hiển thị, `canAssign`, `assignmentIsValid` và `transitions` số. Dùng cùng bảng Domain transition, mapping operation và TicketScope với command; requester/global actions không đòi assignment active.
- `GET /api/v1/tickets/{id}/assignment-teams`: lookup team/department active trong phạm vi phân công.
- `GET /api/v1/tickets/{id}/assignment-teams/{teamId}/agents`: lookup active membership/user có grant xử lý và đọc team/all. Kiểm tra Ticket nguồn trước team đích, không cho đoán team để mở rộng scope.

Lookup dùng `ticket.assign` cùng scope tài nguyên, không cần `team.manage`/`user.manage`; trả ID/tên tối thiểu, có search/page/pageSize và scope trước count/page. Mỗi read dùng transaction RepeatableRead để version/capabilities hoặc count/page cùng snapshot; command vẫn recheck dưới write lock. Chi tiết contract và giao diện: [frontend workflow](frontend-ticket-workflow.md).

## Nâng cấp quyền trên database hiện có

Không có migration mới. Startup Container seed ba permission còn thiếu nhưng **không tự cấp vào role cũ**. Với cấu hình development/user-secrets đang dùng, chạy từ root trên host:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project be/be.csproj --no-launch-profile -- seed-identity
dotnet run --project be/be.csproj --no-launch-profile -- grant-ticket-workflow
```

Lệnh không tự đọc .env; dùng ConnectionStrings:Helpdesk của môi trường/user-secrets/appsettings, host localhost hoặc địa chỉ database thực tế. Không dùng override host.docker.internal của profile Container khi chạy CLI trên host.

grant-ticket-workflow thêm đúng ba quyền mới cho **bốn role mặc định** Requester/SupportAgent/TeamLeader/SystemAdmin, trong một transaction; chỉ bump Version role và ghi TicketWorkflowEnabled audit khi thực sự thêm. Chạy lặp an toàn, không sửa quyền khác hoặc role tùy chỉnh. Đây là lệnh operator chủ động, không được gọi khi F5. Role mới tạo từ seed nhận tập mặc định mới ngay; role tùy chỉnh hoặc nhu cầu cấp từng quyền dùng API grant có role.manage theo phần 6. Không cần login lại, checker đọc quyền database trên cùng JWT; phiên vẫn phải còn hiệu lực.

## Kiểm chứng và bước tiếp theo

```powershell
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Unit tests bao phủ bảng transition, terminal/no-op/unknown status, summary và timestamps. PostgreSQL tests đi qua route production cho phân công hợp lệ/không hợp lệ, requester/global/leader scope, quyền bị thu hồi với cùng JWT, current assignment inactive, resolve/confirm/reopen, activity giữ lịch sử, SLA không đổi deadline, cạnh tranh cùng version và rollback sau SQL save trước commit. Seed/grant tests chứng minh nâng cấp không cấp quyền ngầm, explicit grant idempotent/audited; script smoke test CLI mới trên container riêng, không sửa database helpdesk đang dùng. GitHub Actions hiện có tự chạy test mới.

[Phần 13](backend-ticket-comments.md) có comment/internal note theo ACL; append không đổi status hoặc Version Ticket. Người dùng xác nhận đã cấp ba quyền workflow. [Phần 14](backend-attachments.md) có file và cleanup journal; [phần 15](backend-notifications.md) có notification/SignalR; [phần 16](backend-sla.md) có SLA snapshot/first response/overdue. Bước tiếp theo là Dashboard/báo cáo.
