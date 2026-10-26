# Phạm vi truy cập Ticket

Phần 10 triển khai scope dùng chung cho đọc và xử lý Ticket, dựa trên permission, phiên, requester, assignment và membership hiện tại từ PostgreSQL. Người dùng đã xác nhận apply TicketCategoryAdministration của phần 9. Phần này **không đổi schema, migration, package, permission catalog hoặc secret**; vẫn có 14 permission đã đăng ký.

## Ma trận quyền

Mọi quyết định yêu cầu user active và session thuộc đúng user, chưa hết hạn/thu hồi. Các permission đọc kết hợp bằng **OR**, không phải nhiều policy AND:

| Điều kiện hiện tại | Đọc Ticket | Cập nhật trạng thái | Phân công |
| --- | --- | --- | --- |
| `ticket.read.own` và Ticket.RequesterId là người gọi | Có, kể cả tổ chức/danh mục inactive | Không tự được cấp | Không tự được cấp |
| `ticket.read.team` và là thành viên active của AssignedTeam active, Department active | Có, kể cả đang giao agent khác | Cần `ticket.update.status` và là assignee hoặc IsLeader trong nhóm | Cần `ticket.assign` và IsLeader trong nhóm |
| `ticket.read.all` | Mọi Ticket, gồm chưa có nhóm hoặc nhóm/phòng ban inactive | Cần thêm `ticket.update.status` | Cần thêm `ticket.assign` |
| Chỉ CreatedByUserId, AssignedAgentId, cùng DepartmentId hoặc tên role | Không tự được cấp | Không tự được cấp | Không tự được cấp |

Write scope luôn yêu cầu **đọc được Ticket đó**, không chỉ có một permission thao tác. `ticket.read.all` là phạm vi toàn hệ thống, không phải quyền ghi tự động. SystemAdmin cũng đi qua cùng predicate; không bypass theo tên role. Người được ủy quyền read.all + permission thao tác có thể làm nhiệm vụ toàn hệ thống mà không cần mang tên SystemAdmin.

Assignee cần membership còn active trong nhóm/department active để có phạm vi xử lý theo nhóm; ID assignee lịch sử không đủ. Nếu cũng là requester thì read.own vẫn giữ quyền xem lịch sử, nhưng không tự cho quyền xử lý. IsLeader được đọc từ SupportTeamMember của đúng AssignedTeam; role TeamLeader toàn user không tự cấp phạm vi. `team.manage` quản trị tổ chức là permission độc lập: không thay thế read.team, ticket.assign hoặc ticket.update.status.

`Closed` và `Cancelled` chỉ được đọc, scope UpdateStatus/Assign đều false, kể cả read.all. Với các trạng thái khác, scope xử lý chỉ là điều kiện đầu vào; **chưa đồng nghĩa được chuyển đến mọi trạng thái**. Quy tắc transition, summary khi resolve, confirm/reopen/cancel và sửa nội dung sẽ được chốt trong các use case phần 11–12. Không dùng ticket.update.status như quyền sửa tùy ý title/description/requester/category.

## API đọc đã nối scope

| API | Kết quả |
| --- | --- |
| `GET /api/v1/tickets` | 200, danh sách chỉ trong scope; scope được lọc trước count/page/projection |
| `GET /api/v1/tickets/{id}` | 200, chi tiết được phép đọc |
| `GET /api/v1/tickets/{id}/access` | 200, `{canRead,canUpdateStatus,canAssign}` cho Ticket đọc được |

Mọi endpoint yêu cầu Bearer. Không có read grant nào trả 403; có read grant nhưng Ticket không nằm trong phạm vi hoặc không tồn tại trả cùng 404 `NotFound`, không echo nội dung Ticket. Thiếu/sai/hết hạn/thu hồi phiên hoặc user inactive trả 401. `/access` cũng yêu cầu đọc được Ticket, không dùng để dò ID ngoài phạm vi. Những cờ này chỉ hỗ trợ UI tại thời điểm kiểm tra, không phải token cấp quyền dùng cho request ghi sau đó.

Danh sách nhận `page=1`, `pageSize=20` (1–100), `search` theo title tối đa 200 ký tự, `status`, `priority`, `teamId`, `categoryId`. Sort cố định createdAt giảm dần, tie-break ID; không cần field do client khai để chọn scope. Các filter chỉ thu hẹp kết quả đã được authorize. Query requesterId/userId/role/permission lạ hoặc header X-User-Id/X-Role không thay thế danh tính server. Response trang là `{items,page,pageSize,totalCount}`; count/items là các query đọc thông thường, không có snapshot chung khi cập nhật đồng thời.

Status/priority nhận tên enum hoặc giá trị hợp lệ trong query; response JSON hiện dùng enum số theo serializer mặc định: status New=1, InProgress=2, WaitingForRequester=3, Resolved=4, Closed=5, Cancelled=6, Reopened=7; priority Low=1, Normal=2, High=3, Urgent=4. Giá trị enum không đăng ký, page sai hoặc Guid.Empty trong filter trả 400.

DTO summary gồm ID/number/title/status/priority, requester/category/assigned team/agent ID, timestamps và Version. Detail bổ sung Description, ResolutionSummary và resolved/closed timestamps. Không trả EF entity, email, token/hash, comment/activity/internal note hoặc dữ liệu file. Comment/internal attachment phải thêm scope và permission riêng ở phần 13–14; đọc được Ticket không tự cho đọc internal notes. Response dùng no-store; Swagger mô tả Bearer và 401/403/404 tương ứng.

Phần này có API đọc để kiểm chứng predicate thực tế. Chưa có POST tạo Ticket hoặc endpoint production cho đổi status/phân công. Phần 11 tiếp tục use case tạo Ticket và mở rộng truy vấn; phần 12 nối các command vào guard đã có.

## Phạm vi đích phân công

`ITicketAccessChecker.CanAssignToAsync` và `TicketWriteGuard.LockAssignmentAsync` kiểm tra cả source Ticket và destination:

1. Người gọi phải được Assign **Ticket hiện tại đang lưu**, trước khi đổi AssignedTeamId. Gửi ID nhóm của mình trong body không kéo được Ticket ngoài scope về nhóm mình.
2. Nhóm đích phải tồn tại, active, thuộc phòng ban active.
3. Người có read.all + ticket.assign được chọn mọi nhóm đích hợp lệ. Người theo scope leader phải là leader active của **cả nhóm nguồn và nhóm đích**; có thể chuyển giữa nhiều nhóm mình phụ trách, không được chuyển vào nhóm khác chỉ vì nhìn thấy ID.
4. Agent đích tùy chọn; nếu có thì user phải active, membership active trong nhóm đích, có ticket.update.status và có ticket.read.team hoặc ticket.read.all để đọc/xử lý công việc được giao. Role Requester đơn thuần hoặc chỉ có ID membership không đủ.

TeamId bắt buộc, không nhận Guid.Empty; AgentId=null nghĩa giao nhóm chưa chọn agent. Clear toàn bộ nhóm là use case riêng chưa mở. Source nhóm inactive/chưa gán chỉ được phân công bởi phạm vi read.all; destination vẫn phải active. Nếu source không đọc được thì 404, source đọc được nhưng thiếu Assign thì 403, destination không hợp lệ trong scope thì 400 `InvalidAssignment`.

## Ranh giới code và transaction

- [TicketScope](../be/Infrastructure/Tickets/TicketScope.cs) chứa một predicate EF dùng chung cho Read, UpdateStatus và Assign. Session/user/grant/membership/team/department đều là subquery PostgreSQL; không đưa IQueryable qua interface Application hoặc API và không cache quyết định theo JWT.
- [ITicketAccessChecker](../be/Application/Features/Tickets/ITicketAccessChecker.cs) là abstraction Application cho boolean/capability checks; không trả entity hoặc query. `GetAsync` có thể thực hiện nhiều lần kiểm tra, nên cờ chỉ là gợi ý UI, không bảo đảm snapshot chung.
- [TicketReadStore](../be/Infrastructure/Tickets/TicketReadStore.cs) dùng Read predicate cho cả list/count và detail trước khi project DTO. Query không Include/load nội dung Ticket ngoài scope; mỗi trang không kiểm tra quyền bằng một query riêng cho từng row.
- [TicketWriteGuard](../be/Infrastructure/Tickets/TicketWriteGuard.cs) dành cho Infrastructure mutation stores. Caller phải mở transaction và lấy guard **trước khi sửa entity**. Guard không tự save/commit hoặc thay thế workflow/audit.

Thứ tự dùng guard trong command:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
var ticket = await guard.LockAsync(actor, id, TicketOperation.UpdateStatus, version, cancellationToken);
// Validate transition/payload, apply changes, and append TicketActivity here.
await database.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

Với phân công, dùng LockAssignmentAsync cùng target trước khi sửa assignment. `actor` phải từ principal đã được xác thực, không từ body; operation cố định do use case chọn. Guard trả Domain Ticket nội bộ cho store; không được trả entity này ra HTTP.

Guard yêu cầu transaction, lấy advisory lock `734205001`, khóa row user người gọi rồi Ticket, nạp lại row sau khóa và kiểm tra lại session/read/operation/version. Cùng lock/order với quản trị Identity, tài khoản, tổ chức và danh mục, nên thu hồi grant/membership hoặc deactivate đã commit sẽ được thấy trước khi ghi. Các thao tác ghi hiện được tuần tự qua khóa chung; không giữ khóa qua dịch vụ ngoài. Caller xử lý Dispose/rollback khi lỗi. SQL writer ngoài ứng dụng cũng cần tuân thủ quy tắc khóa và validation.

Không có transaction hoặc Ticket đã bị sửa trong ChangeTracker trước guard là lỗi lập trình, fail closed. Một row đã đọc nhưng chưa sửa được reload sau khi khóa để không tin assignment/version cũ. Version thiếu/0 trả 400, stale sau khi đã kiểm tra scope trả 409 `VersionConflict`. Request ngoài scope không nhận được version hoặc nội dung chỉ vì gửi version đúng. Mỗi command phải tạo TicketActivity trong cùng transaction; scope checks thuần đọc không ghi audit cho từng lần xem.

## Kiểm thử và sử dụng

```powershell
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Negative tests kiểm tra list/detail/checker cùng kết quả; scope trước count/page; creator, assignee mất membership, cùng department, role/claim/header không cấp quyền; thu hồi quyền/phiên, user/team/department inactive, demote/reassign với cùng JWT; destination ngoài scope/agent sai quyền; Closed/Cancelled; transaction/version và hai write đồng thời. Các harness route `__tests/ticket-scope` chỉ được TestServer đăng ký từ assembly test, dùng production guard với save/activity/rollback thật để chứng minh request bị từ chối không sửa Ticket hoặc ghi activity. Không có những route này trong API production.

Phần 10 không cần migration hoặc lệnh grant mới. Sau khi phần 9 đã áp dụng và quyền cũ đã cấp như mong muốn, F5 profile Container (Dockerfile) vẫn port 5000. Dùng Ticket có sẵn để thử GET; API tạo dữ liệu Ticket sẽ triển khai ở phần 11, không seed Ticket giả để bỏ qua ACL.
