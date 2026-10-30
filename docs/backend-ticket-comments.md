# Comment, internal note và activity

Phần 13 bổ sung thảo luận và timeline trên [Ticket cơ bản](backend-tickets.md), dùng chung [scope Ticket](backend-ticket-access.md). Người dùng đã xác nhận cấp ba quyền workflow phần 12 cho bốn role mặc định. Riêng phần 13 **không cần migration, permission, grant, package hoặc secret mới**; catalog vẫn 17 permission. Phần 14–16 bổ sung MinIO, notification và SLA, tổng model hiện tại 20 entity/tám migration. Container (Dockerfile) giữ port 5000.

## API

Tất cả endpoint yêu cầu Bearer JWT, tài khoản active, phiên hợp lệ và read scope của Ticket. Response no-store; chỉ trả DTO.

| Endpoint | Kết quả |
| --- | --- |
| `GET /api/v1/tickets/{ticketId}/discussion-access` | Capability hiện tại canReadInternal/canCommentPublic/canCommentInternal từ TicketScope, phục vụ frontend bước 6 |
| `POST /api/v1/tickets/{ticketId}/comments` | 201 Comment + Location; permission theo public/internal trong transaction |
| `GET /api/v1/tickets/{ticketId}/comments` | 200, danh sách comment đọc được, phân trang |
| `GET /api/v1/tickets/{ticketId}/comments/{commentId}` | 200, chi tiết comment đọc được |
| `GET /api/v1/tickets/{ticketId}/activities` | 200, timeline đọc được, phân trang |
| `GET /api/v1/tickets/{ticketId}/activities/{activityId}` | 200, chi tiết activity đọc được với payload đã lọc |

Tạo public comment:

```json
{
  "content": "Tôi đã thử kết nối lại nhưng vẫn gặp lỗi.",
  "isInternal": false
}
```

Tạo internal note dùng cùng route với isInternal=true. Bỏ isInternal mặc định false. Content bắt buộc, tối đa 20.000 ký tự đầu vào, trim rồi từ chối toàn khoảng trắng. Chỉ nhận application/json, body tối đa 128 KiB. Content lưu văn bản; UI dùng text hoặc sanitizer phù hợp khi triển khai, không render HTML từ nội dung này trực tiếp.

AuthorId luôn lấy từ phiên. Không nhận authorId/ticketId/createdAt/updatedAt/version/attachment hoặc field khác trong body; field lạ trả 400 kể cả admin. DTO trả id/ticketId/authorId/content/isInternal/createdAt/updatedAt, không trả user entity/email/role. CreatedAt UTC theo precision microsecond PostgreSQL; UpdatedAt của comment mới null.

## Quy tắc public/internal

| Thao tác | Điều kiện |
| --- | --- |
| Đọc public comment/activity công khai | Có read scope Ticket; không cần permission ghi comment |
| Ghi public comment | Read scope + ticket.comment.public + Ticket chưa Closed/Cancelled |
| Đọc internal note/activity nội bộ | Read scope + ticket.comment.internal + **không phải RequesterId** + (read.all hoặc membership active trong AssignedTeam active, Department active) |
| Ghi internal note | Toàn bộ điều kiện đọc internal + Ticket chưa Closed/Cancelled |

Permission đọc Ticket kết hợp own/team/all như phần 10, không thay thế điều kiện internal. ticket.read.all một mình không cho đọc note; ticket.comment.internal một mình không cấp read scope Ticket. Public/internal write là hai quyền độc lập: có internal không tự có public và ngược lại. User có read.all + internal có thể hỗ trợ ngoài team, nhưng **requester của chính Ticket vẫn không đọc/ghi internal note**, kể cả có role SupportAgent/SystemAdmin hoặc membership của team. Đây là điều kiện trên tài nguyên, không kiểm tra tên role Requester.

Không cần là assignee để trao đổi nếu đã có đúng read scope và permission. AuthorId, CreatedByUserId, cùng department, claim hoặc header tự khai không cấp thêm quyền. Author cũ mất membership/grant không giữ quyền đọc note do mình viết. User/team/department inactive hoặc grant/session bị thu hồi có hiệu lực ở lần kiểm tra kế tiếp, với cùng JWT. Closed/Cancelled giữ lịch sử theo ACL nhưng không nhận comment mới; Resolved vẫn có thể trao đổi trước confirm/reopen.

Comment trong phần 13 **chỉ thêm mới**, giữ lịch sử: chưa mở PATCH/DELETE, đổi public ↔ internal hoặc sửa hộ nội dung. Cần thiết kế permission, concurrency, audit và visibility lịch sử trước khi mở sửa/xóa; không dùng quyền comment hiện tại như quyền biên tập tùy ý. [API file phần 14](backend-attachments.md) nhận commentId sau khi tạo comment, lấy visibility từ comment và chỉ cho author đính kèm; body tạo comment vẫn không nhận attachment IDs.

## Phân trang và chống lộ dữ liệu

Cả comments và activities nhận page (mặc định 1, 1–1.000.000), pageSize (mặc định 20, 1–100), descending (mặc định false) và isInternal tùy chọn. Mặc định CreatedAt tăng dần, luôn tie-break Id. Response items/page/pageSize/totalCount; trang vượt dữ liệu trả items rỗng và giữ totalCount. Count/items là query đọc riêng, không cam kết snapshot chung khi có comment mới đồng thời.

isInternal không gửi: mọi row đọc được; false: public; true: chỉ nội bộ **trong quyền hiện tại**. Requester gửi true nhận trang rỗng/count=0, không nâng quyền hoặc nhận tổng số note thật. Scope/visibility được áp dụng trong SQL **trước count/page/projection**, không tải note trái phép rồi lọc tại controller. Ticket detail/list hiện có không nhúng comment, internal count, nội dung hoặc ID note.

CommentId/activityId phải thuộc đúng ticketId trong route. Ticket ngoài scope hoặc không tồn tại trả 404 như nhau. Comment/note/activity không đọc được, ID không tồn tại, hoặc đặt ID dưới Ticket khác cũng trả 404, không tiết lộ body/author/metadata nội bộ. Thiếu read grant trả 403; thiếu/sai/hết hạn/thu hồi phiên hoặc user inactive trả 401. Thiếu write grant hoặc ghi vào Ticket terminal trả 403. Input/phân trang sai trả 400; lỗi có ProblemDetails/traceId, use case có code, không echo content/SQL/exception.

## Transaction và tác động lên Ticket

Create mở transaction, dùng TicketWriteGuard.LockCommentAsync với thứ tự advisory lock 734205001 → row user → row Ticket. Recheck session/read/operation sau khi khóa; cùng quy tắc với quản trị grant/tổ chức và workflow. Sau đó insert TicketComment và TicketActivity trong cùng SaveChanges/transaction rồi commit. Lỗi sau SQL save nhưng trước commit rollback cả hai; writer SQL ngoài ứng dụng phải tuân thủ cùng khóa/validation.

Append không sửa row Ticket, không đổi Version/xmin, UpdatedAt, status hoặc assignment. Nhờ vậy note nội bộ không làm lộ thêm sự kiện qua UpdatedAt/Version của Ticket. [Phần 16](backend-sla.md) chỉ cập nhật FirstRespondedAt/breach SLA khi public comment đủ điều kiện; internal note không thay SLA. Không nhận Version từ client vì comment là một bản ghi mới độc lập; nhiều append đồng thời đều được giữ. Row lock sắp thứ tự với close/cancel/reassign và recheck trạng thái/scope; workflow vẫn bắt buộc Version.

Comment requester trên WaitingForRequester không tự resume Ticket. [Phần 15](backend-notifications.md) thêm notification cùng transaction và phát ID sau commit, theo public/internal ACL. [Phần 16](backend-sla.md) tính first response một lần cho public comment của người khác requester có quyền/phạm vi xử lý hiện tại; giữ deadline, không tạo SLA hồi tố. Mỗi POST hợp lệ là comment mới; chưa có idempotency key, không tự retry khi chưa biết kết quả sau mất kết nối.

## Visibility và payload activity

New activity có ActorId từ phiên, CreatedAt bằng comment.CreatedAt; OldValue null, NewValue chỉ chứa CommentId/IsInternal. Không sao chép content vào audit. Phân loại theo action cố định do server chọn, không theo action/visibility từ body:

| Action đã đăng ký | Visibility và dữ liệu trả |
| --- | --- |
| TicketCreated | Public; TicketNumber/requester/creator/category/status/priority |
| TicketAssigned | Public; AssignedTeamId/AssignedAgentId cũ/mới |
| TicketStatusChanged | Public; status, resolution summary, resolved/closed time cũ/mới |
| PublicCommentAdded | Public; CommentId và IsInternal=false |
| InternalNoteAdded | Cùng internal ACL với note; CommentId và IsInternal=true |
| PublicAttachmentAdded | Public; AttachmentId, CommentId và IsInternal=false |
| InternalAttachmentAdded | Cùng internal ACL; AttachmentId, CommentId và IsInternal=true |
| SlaFirstResponseBreached / SlaResolutionBreached | Public; DueAt và DetectedAt, không chứa nội dung comment hoặc file |
| Action chưa đăng ký/legacy không rõ visibility | Ẩn khỏi list/count/detail với mọi role cho đến khi có quy tắc rõ ràng |

Activity DTO trả id/ticketId/actorId/action/createdAt/oldValue/newValue, trong đó old/new là TicketActivityData có các field whitelist tùy action, field khác null. **Không trả raw JSON** từ database. Các key Content/internalNote/user/email hoặc dữ liệu tùy ý trong audit cũ không xuất hiện. JSON không đúng dạng payload nhận null; action mới cần thêm cả visibility lẫn projection/tests trước khi xuất API. Filter isInternal=true trên activities chọn InternalNoteAdded và InternalAttachmentAdded trong quyền hiện tại, false chọn các action public đã đăng ký. Attachment activity chỉ có ID/visibility, không có tên file hoặc storage key.

ResolutionSummary trong history là kết quả **công khai** do workflow ghi, đã có ở Ticket detail; không dùng làm nơi lưu internal note. Lịch sử resolution trước reopen tiếp tục đọc được theo read scope. SQL writer không được đổi visibility của comment hoặc gắn action public cho dữ liệu nội bộ; ứng dụng hiện không có thao tác đổi visibility. Khi thêm các loại audit khác cần đăng ký ACL riêng, không mặc định đưa mọi action ra requester.

## Kiểm chứng và bước tiếp theo

```powershell
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

PostgreSQL tests bao phủ author từ phiên, strict DTO, Location, trim/length, public/internal độc lập, requester nhiều role vẫn không đọc note, list/detail/activity không lộ note hoặc count, nested ID sai Ticket, author mất scope, thu hồi grant/member/team/department/user/session với cùng JWT, terminal/history, append đồng thời, rollback sau SQL save và projection audit cũ/unknown action. Toàn bộ test workflow/ACL cũ tiếp tục chạy; script dùng container tạm và không thay đổi database helpdesk thật. CI hiện có tự chạy test mới, không cần workflow/secret mới.

[Phần 14](backend-attachments.md) có file theo ACL và cleanup journal; [phần 15](backend-notifications.md) có notification/SignalR. [Phần 16](backend-sla.md) thêm first response và overdue với [migration SlaMvp](sla-mvp-review.md); phần tiếp theo là Dashboard/báo cáo.
