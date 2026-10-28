# Dashboard và báo cáo — phần 17

Đã bổ sung API tổng hợp trong backend, dùng scope Ticket hiện tại và model sau SlaMvp phần 16. Không thêm permission, package, secret, bảng hoặc migration. Chưa triển khai UI frontend hoặc export CSV/Excel.

## API contract

Các endpoint yêu cầu Bearer JWT/session còn hiệu lực và ít nhất một quyền đọc Ticket (`ticket.read.own`, `ticket.read.team`, `ticket.read.all`). Quyền user.manage hoặc ticket.manage.sla không tự cấp quyền báo cáo. Response thành công có `Cache-Control: no-store`.

| GET route | Kết quả |
| --- | --- |
| `/api/v1/dashboard` | Tổng số Ticket, trạng thái, priority và chỉ số SLA trong tập lọc |
| `/api/v1/reports/tickets/teams` | Nhóm theo AssignedTeamId, phân trang sau aggregation, kèm số liệu Ticket/SLA mỗi nhóm |
| `/api/v1/reports/tickets/agents` | Nhóm theo AssignedAgentId, phân trang sau aggregation, kèm số liệu Ticket/SLA mỗi agent |
| `/api/v1/reports/tickets/daily` | Số Ticket tạo theo ngày UTC; điền ngày trống bằng 0 sau khi SQL đã tổng hợp |

Filter chung: createdFrom/createdTo (ISO 8601 có offset), teamId, agentId, status, priority. Mọi điều kiện kết hợp AND với ACL, không thay hoặc nới ACL. Không nhận userId/role để chọn danh tính. ID filter ngoài phạm vi và ID không tồn tại đều cho tập rỗng, không trả metadata giúp phân biệt hai trường hợp.

Enum query theo API hiện có: status New=1, InProgress=2, WaitingForRequester=3, Resolved=4, Closed=5, Cancelled=6, Reopened=7; priority Low=1, Normal=2, High=3, Urgent=4. Dùng cả createdFrom và createdTo khi chọn giai đoạn tùy chỉnh.

Ví dụ:

```http
GET /api/v1/dashboard?createdFrom=2026-10-01T00:00:00Z&createdTo=2026-11-01T00:00:00Z
Authorization: Bearer <accessToken>

GET /api/v1/reports/tickets/teams?page=1&pageSize=20&sortBy=slaBreached&descending=true
Authorization: Bearer <accessToken>
```

Khoảng thời gian **[createdFrom, createdTo)** dựa trên Ticket.CreatedAt, được đổi về UTC. Mặc định từ 00:00 UTC cách hôm nay 29 ngày đến thời điểm gọi API (30 ngày lịch, ngày hiện tại chưa trọn). Tối đa 366 ngày mỗi request; từ lớn hơn/bằng đến, enum/UUID sai hoặc Guid.Empty trả 400. Nếu chọn createdTo tương lai, dữ liệu vẫn bị cắt tại thời gian server AsOf; series không tạo các ngày tương lai. Mốc AsOf do server cung cấp, không phải tham số replay lịch sử.

Các số liệu là **trạng thái hiện tại của cohort Ticket tạo trong khoảng lọc**, không phải lịch sử số Ticket chuyển trạng thái hoặc được giải quyết trong từng ngày. Team/agent là phân công hiện tại, không phải đội/nhân viên đã từng xử lý. Muốn báo cáo trạng thái tại một ngày quá khứ hoặc attribution lịch sử cần contract và truy vấn activity riêng; MVP không suy lịch sử từ assignment hiện tại.

## Phạm vi và tính nhất quán

`TicketScope.For(actor, Read)` được áp dụng trong SQL **trước count, AVG, group, sort và pagination**. Requester thấy own; agent/leader thấy team hợp lệ và own theo grant; read.all thấy toàn bộ trong filter. Có nhiều role hoặc membership không nhân đôi Ticket. Team/member/department inactive không còn cấp read.team; read.own/read.all vẫn theo rule gốc.

Không join comment/internal note, file, notification hoặc danh mục Identity để tính số liệu; không xuất title/description/summary/email/nội dung nội bộ/object key. Tên hiển thị team/agent chỉ được lấy theo IDs của trang nhóm **đã có Ticket trong phạm vi**. Không xuất toàn bộ roster, nhóm trống hoặc nhóm bị ẩn. AssignedAgent/Team inactive vẫn hiện tên và isActive=false nếu người gọi còn đọc được Ticket lịch sử bằng own/all.

Dashboard dùng một SQL aggregate cho tất cả KPI, byStatus/byPriority; daily dùng một SQL GroupBy. Team/agent dùng RepeatableRead cho count-group/page/labels để các truy vấn cùng thấy một snapshot, không giữ advisory lock chặn các command. Session/grant/membership scope được so lại sau khi đọc, ngoài transaction: phiên mất hiệu lực trả 401, mất mọi read grant trả 403, phạm vi đọc bị đổi trả **409 VersionConflict**, bỏ toàn bộ response và client refetch. Đây là kiểm soát quyền trong request đang xử lý, không phải cache quyền.

Các endpoint/request riêng có thể nhận dữ liệu ở thời điểm khác nhau khi Ticket thay đổi; Period.AsOf dùng chung mốc tính SLA trong từng response, không bảo đảm nhiều request là một snapshot DB. Assignment/status thay đổi sau snapshot được phản ánh trong lần refetch. Không dùng cache chia sẻ hoặc Redis cho báo cáo có ACL.

## Dashboard response

Response gồm:

- `period`: createdFrom, createdTo, asOf, đều là UTC.
- `tickets`: total, open, resolved, closed, cancelled, unassignedTeam, unassignedAgent (kiểu đếm long).
- `byStatus`: đủ 7 `{ key, count }`, kể cả 0, theo thứ tự enum nghiệp vụ.
- `byPriority`: đủ 4 `{ key, count }`, kể cả 0, thứ tự Low → Urgent.
- `sla`: tập và outcome SLA mô tả ở dưới.

Open = New + InProgress + WaitingForRequester + Reopened. Resolved và Closed là hai counter riêng; nếu UI cần tổng hoàn thành thì cộng hai giá trị này. Cancelled không phải hoàn thành. UnassignedTeam/Agent đếm trong toàn cohort/filter, gồm cả Ticket đã kết thúc, không tự có nghĩa là backlog đang mở. Open trong dashboard cũng là open của cohort đang chọn, không phải backlog mọi thời điểm.

Tập rỗng vẫn trả 200 với counters 0, đủ status/priority, tỷ lệ và trung bình null. Không lấy 0% hoặc 100% thay cho trường hợp chưa có mẫu.

## Định nghĩa SLA cho báo cáo

Dùng deadline/timestamps/marker **đã lưu trên TicketSla**, không join target hoặc trạng thái policy hiện tại. Policy đổi/deactivate không viết lại báo cáo Ticket cũ. Công thức breach SQL được kiểm tra parity với `SlaRules` phần 16 cho cả 7 status, đúng deadline/trễ, resolve/response null và marker lịch sử.

| Field | Định nghĩa |
| --- | --- |
| withSla | Ticket có snapshot, gồm cả cancelled |
| withoutSla | Ticket không có snapshot; không được tính là đạt SLA |
| excludedCancelled | Ticket có SLA nhưng status Cancelled; loại khỏi mẫu đo SLA |
| eligible | withSla − excludedCancelled |
| breachedTickets | Ticket eligible vi phạm ít nhất một metric; vi phạm cả hai vẫn đếm một Ticket |

Mỗi metric `firstResponse`/`resolution` trả met, breached, pending, stopped, unknown, measured, metPercent, durationSamples, averageMinutes:

| Outcome | Ý nghĩa |
| --- | --- |
| met | Có timestamp hoàn thành từ AppliedAt đến deadline, chưa có breach theo quy tắc SLA |
| breached | Có marker lịch sử hoặc vượt hạn theo SlaRules hiện tại; có thể còn chưa hoàn thành |
| pending | Chưa hoàn thành, Ticket còn mở xử lý, chưa quá hạn |
| stopped | First response chưa ghi nhưng đã resolve đúng hạn, đồng hồ dừng; không giả lập một public response. Resolution stopped luôn 0 |
| unknown | Legacy/timestamps không đủ kết luận theo rule hiện tại, ví dụ Closed có phản hồi trễ nhưng chưa có marker lịch sử; không tự kết luận là đạt |

Mỗi metric có `met + breached + pending + stopped + unknown = eligible`. **Measured = met + breached**; metPercent = 100 × met/measured, làm tròn 2 chữ số, null nếu measured=0. Đây là tỷ lệ outcome đã kết luận của cohort, bao gồm vi phạm chưa giải quyết; không chia cho mọi Ticket hoặc coi pending/stopped/unknown là đạt.

DurationSamples đếm timestamp hoàn thành đã ghi hợp lệ (không trước AppliedAt), loại cancelled; averageMinutes là trung bình SQL của các duration đó, làm tròn 2 chữ số. Không lấy thời gian đang chờ của Ticket chưa hoàn thành để tính trung bình. Denominator của duration khác measured, vì một vi phạm chưa phản hồi đã có kết luận breach nhưng chưa có duration hoàn thành. Unknown có timestamp duration hợp lệ vẫn có thể góp vào duration; độ trễ ghi nhận và kết luận SLA là hai chỉ số khác nhau.

Resolution dùng ResolvedAt hiện tại; reopen xóa mốc đó nên không còn duration resolution hiện tại, trong khi breach lịch sử vẫn giữ. Worker bỏ qua Closed/Cancelled và báo cáo không tự tạo marker hoặc sửa dữ liệu legacy. Xem [quy tắc SLA phần 16](backend-sla.md).

## Báo cáo team/agent và daily

Team/agent response:

```json
{
  "period": { "createdFrom": "...Z", "createdTo": "...Z", "asOf": "...Z" },
  "groups": {
    "items": [
      { "id": "<UUID hoặc null>", "name": "Tên hiển thị", "isActive": true, "tickets": {}, "sla": {} }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 1
  }
}
```

Tickets/Sla của mỗi group cùng shape với dashboard. Id null là bucket chưa phân công, name “Chưa phân công”, isActive null. TotalCount là **số group có dữ liệu sau ACL/filter**, gồm bucket null; không phải số Ticket hoặc tổng team/user trong hệ thống.

IsActive phản ánh cờ của user/team, không suy ra department đang active hoặc khả năng phân công. Đọc báo cáo không cấp quyền xử lý/gán Ticket; frontend vẫn dùng API lookup/assignment và permission tương ứng cho các thao tác đó.

Page mặc định 1, pageSize 20, tối đa 100, page tối đa 1.000.000. SortBy chỉ total/open/slaBreached, descending mặc định true; tie-break ID, bucket null được sắp như Guid.Empty. Group/AVG/sort/page chạy trong PostgreSQL trước khi trả DTO. Label lookup là một query với các ID trên trang, không N+1 theo từng Ticket/group.

Daily trả `{ period, days: [{ date, createdTickets }] }`, date là đầu ngày UTC, thứ tự tăng dần. Grouping trước trong PostgreSQL; backend chỉ điền tối đa 367 bucket ngày đã aggregate, không tải Ticket về RAM để đếm. Ngày đầu/cuối có thể là ngày chưa trọn nếu filter có giờ; cận createdTo luôn exclusive. Series không phải biểu đồ số Ticket resolve trong ngày. Chart nên giữ nhãn ngày UTC; timestamp sự kiện khác có thể đổi múi giờ khi hiển thị.

## SQL, index và kiểm thử

Query có tags Reports:dashboard/groups-count/groups-page/daily để nhận diện trong test/diagnostics; không chứa credentials hoặc nội dung Ticket trong tag. [Kết quả EXPLAIN thực tế](report-query-review.md) dùng database con tạm với 8.000 Ticket/8.000 snapshot, 100 Ticket thuộc cohort gần đây; đo read.all/own/team và thử index CreatedAt riêng.

Giữ các index hiện có, không thêm migration sau khi đo. Các câu SQL thực tế có COUNT/AVG/GROUP BY; group page có LIMIT/OFFSET. Xem thêm [Npgsql translations](https://www.npgsql.org/efcore/mapping/translations.html); test chạy provider Npgsql 8 đang pin trong repo, không dựa vào tính năng mới của provider khác major.

```powershell
dotnet build be/be.sln --no-restore -c Release -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Tests cover own/team/all, role/membership không nhân đôi, filter âm, user/session/grant/team/department mất hiệu lực, đổi scope ngay giữa aggregate, pagination/unassigned/inactive history, UTC range/day/midnight, empty/invalid/no-store, SLA denominator/average và parity với Domain. Query-plan test dùng database GUID riêng, EXPLAIN ANALYZE + BUFFERS trước/sau candidate index; không tạo/drop index hoặc thay dữ liệu trên DB Helpdesk thật. Kết quả đo nằm trong `be/tests/Helpdesk.Persistence.Tests/bin/<Configuration>/net8.0/report-query-plan.json`, là artifact bị git ignore.

Kết quả lượt triển khai phần 17: **497/497 tests đạt, 0 skipped**, cùng CLI seed/grant smoke tests; riêng reporting có 18 tests gồm query-plan A/B. Build Release `-warnaserror` đạt 0 warning/error. Chưa chạy workflow GitHub hoặc UI frontend; không có migration/DDL mới cần apply.

## Trước khi frontend tích hợp

1. Database đã apply SlaMvp; phần 17 chỉ cần Stop Debugging/F5 code mới, giữ port Container 5000. Không cần thêm .env, role/grant, migration hoặc service.
2. Nếu cần SLA, admin tạo policy active qua API theo [backend-sla.md](backend-sla.md). Lượt kiểm tra DB local thấy chưa có policy active; không tự seed cam kết thời gian. Ticket cũ không có snapshot vẫn được đếm withoutSla.
3. Login bằng các role để thử Swagger, so số liệu với danh sách Ticket cùng ACL và **cùng CreatedFrom/To/filter**; kiểm tra đủ status/priority và quyền theo `/users/me`.
4. Frontend dùng typed DTO/OpenAPI, query keys chứa user/session và filter/page; xóa cache khi logout, đổi user hoặc mất quyền, refetch khi 409. 401 refresh một lần theo luồng auth, 403 không retry vô hạn.
5. REST command thành công cần invalidate Ticket/dashboard/report. SignalR chỉ là hint, không cập nhật KPI bằng cách cộng/trừ payload ID; reconnect hoặc NotificationsChanged thì refetch theo quyền hiện tại. Không phải mọi client đều nhận event cho mọi Ticket có thể thống kê, nên vẫn cần refresh khi mở lại tab hoặc cơ chế stale/refetch phù hợp.
6. Client SignalR JS major 10 trong FE cần integration test với backend 8 trước khi coi realtime UI đã hoàn thiện. Chưa có UI/CSV export, dashboard cache chung hoặc report lịch sử trạng thái.
