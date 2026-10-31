# Bộ dữ liệu mô phỏng một tuần vận hành

Bộ seed `helpdesk-demo-week-v1` bổ sung dữ liệu vào database đang có để dùng thử dashboard, báo cáo và phân quyền. Đây là dữ liệu **tổng hợp**, được đánh dấu `[Demo tuần]`, tên tài khoản có `(Demo)` và email thuộc `demo.helpdesk.example`. Các mốc quá khứ mô phỏng hành vi người dùng, không phải nhật ký vận hành thật.

## Chạy trên PostgreSQL Docker Compose hiện tại

Database phải đã apply toàn bộ migration đã review và seed Identity. Từ thư mục gốc repository:

```powershell
.\be\scripts\Seed-DemoWeek.ps1
```

Script thực hiện:

1. Đọc cấu hình Compose trong bộ nhớ; kết nối cổng PostgreSQL được publish trên máy hiện tại.
2. Build backend Release. Tạo thư mục `.local/demo-week` chỉ tài khoản hệ điều hành hiện tại được truy cập.
3. Sinh mật khẩu ngẫu nhiên 32 byte và lưu tại `.local/demo-week/credentials.txt`. **Cả 30 tài khoản demo mới dùng chung mật khẩu ban đầu này** để thuận tiện nghiệm thu; mật khẩu không được ghi ra console, Git hay tài liệu. File được giữ nguyên khi chạy lại.
4. Backup database hiện tại dạng `pg_dump -Fc` vào cùng thư mục riêng trước khi seed.
5. Chạy CLI `seed-demo-week`. Toàn bộ dữ liệu mới và marker hoàn thành được commit trong một transaction. Không apply migration, xóa dữ liệu hoặc reset sequence.

Lệnh này được gọi chủ động, không chạy khi F5/khởi động container. Seed Development cũ vẫn giữ nguyên quy tắc chỉ thêm bộ mẫu nhỏ vào database nghiệp vụ trống.

Chạy lại là **no-op**, kể cả khi Ticket demo đã được chỉnh sửa hoặc grant đã thay đổi sau lần seed đầu. Không đổi mật khẩu, không bổ sung lại bản ghi đã xóa và không dịch lịch sử sang tuần mới. Nếu mất file mật khẩu sau khi đã seed, dùng chức năng tự đổi mật khẩu khi còn đăng nhập hoặc quy trình quản trị được hệ thống hỗ trợ; lệnh seed không reset mật khẩu.

CLI cũng có thể được chạy riêng với cấu hình qua biến môi trường/user-secrets:

```text
DemoWeekSeed__Enabled=true
DemoWeekSeed__Password=<random secret 24–128 characters, supply privately>
ConnectionStrings__Helpdesk=<target connection, supply privately>
dotnet run --project be -c Release --no-build --no-launch-profile -- seed-demo-week
```

Chạy CLI trực tiếp cần tự lưu mật khẩu và backup trước. Script tiện ích ở trên chỉ hướng tới database Compose cục bộ.

## Tài khoản và ma trận quyền

Thêm đuôi `@demo.helpdesk.example` vào login trong bảng. Ví dụ `lead.it@demo.helpdesk.example`. Danh sách tên và email đầy đủ nằm trong `DemoWeekCatalog.cs`.

| Login | Số lượng | Role | Phạm vi và quyền chính |
| --- | ---: | --- | --- |
| `admin` | 1 | `SystemAdmin` | Toàn bộ 17 permission tại thời điểm seed tuần; sau khi cấp thêm quyền cộng đồng có 21 permission, gồm soạn/xuất bản tri thức và sự cố. |
| `lead.it`, `lead.hr`, `lead.office` | 3 | `TeamLeader` | `Requester` + quyền Agent + `ticket.assign`, `team.manage`; mỗi người có membership leader active ở đúng một team. Không quản trị user/grant toàn hệ thống. |
| `agent.it.1–2`, `agent.hr.1–2`, `agent.office.1–2` | 6 | `SupportAgent` | Đọc Ticket của mình và team; public/internal comment; xử lý trạng thái trong phạm vi assignment. Không có `ticket.assign` hoặc quyền quản trị tổ chức. |
| `requester.01–18` | 18 | `Requester` | `ticket.create`, `ticket.read.own`, `ticket.comment.public`, `ticket.confirm`, `ticket.reopen`, `ticket.cancel`; chỉ xem Ticket do mình yêu cầu, không xem internal note. |
| `accounts` | 1 | `DemoAccountManager` | **Chỉ `user.manage`**: quản trị thông tin user theo API hiện có, không cấp role/grant, không đọc Ticket/báo cáo. |
| `reports` | 1 | `DemoReportViewer` | **Chỉ `ticket.read.all`**: đọc Ticket/báo cáo toàn bộ, không sửa Ticket hoặc đọc internal note. |

`agent.it.1` đồng thời mang `SupportAgent` và `Requester` để nghiệm thu nhiều role. Quyền hiệu lực không tăng vì grant Requester đã có trong SupportAgent.

Không có quyền riêng tên `report.read`: báo cáo sử dụng chính ACL đọc Ticket. Backend vẫn kiểm tra active user, phiên, grant hiện tại và membership; tên role đơn thuần không mở quyền vào team.

Bốn role mặc định phải có đúng ma trận của `IdentitySeedCatalog`. Nếu đã tùy chỉnh grant trước khi seed, lệnh dừng và giữ nguyên cấu hình đó. Hai role `DemoAccountManager`/`DemoReportViewer` chỉ được tạo mới cho demo; seed không tự mở quyền cho role/tài khoản hiện có.

## Dữ liệu nghiệp vụ

| Thành phần mới | Quy mô |
| --- | ---: |
| Phòng ban | 6: CNTT, Nhân sự, Hành chính, Tài chính kế toán, Kinh doanh, Vận hành |
| Team / membership | 3 team / 9 thành viên, gồm 3 leader và 6 agent |
| Danh mục | 3 nhóm gốc + 12 danh mục con |
| Ticket | **144**, số lượng theo ngày: 12, 16, 24, 28, 26, 22, 16 |
| SLA snapshot | 128 Ticket; 16 Ticket minh họa trạng thái không có SLA |
| Policy lịch sử | 4 policy theo Low/Normal/High/Urgent, đều đã deactivate |
| Comment / activity / notification | Sinh theo timeline của từng Ticket và thời điểm seed; có public/internal, đã đọc/chưa đọc |

Khoảng thời gian là **7 ngày lịch UTC+07:00 gần nhất, tính cả ngày hiện tại**. Giờ tạo chủ yếu 08:05–17:40; hôm nay chỉ tạo đến thời điểm seed, không dựng sự kiện tương lai. Nếu chạy trước giờ làm việc, dữ liệu ngày hiện tại nằm từ đầu ngày đến thời điểm chạy. Những luồng chưa đủ thời gian hoàn tất sẽ dừng ở trạng thái phù hợp, vì vậy số lượng mỗi status có thể khác giữa các lần tạo trên database mới.

Tình huống gồm VPN, email, laptop, ERP, chấm công, nghỉ phép, hồ sơ, bảo hiểm, máy in, phòng họp, thẻ ra vào và điều hòa ở nhiều địa điểm. Tất cả requester/creator lấy từ cùng tài khoản mô phỏng; không tự tạo hộ. TicketNumber do PostgreSQL sinh nên tiếp nối số hiện có.

Phân công đầu tiên do demo SystemAdmin thực hiện vì leader chưa có scope với Ticket chưa thuộc team. Chuyển trạng thái dùng `TicketWorkflow`; phản hồi công khai đầu tiên đến từ agent đang được phân công. Internal note của leader và comment requester không tính first response. Có Waiting, mở lại, hủy, xác nhận Closed, xử lý đúng hạn và trễ hạn. Activity, comment và SLA được dựng theo thứ tự thời gian, cùng transaction; mốc breach mô phỏng worker phát hiện sau deadline.

Policy demo được dùng để chụp snapshot lịch sử rồi lưu **inactive**, vì vậy không thay live policy hoặc deadline Ticket cũ và không áp SLA demo cho Ticket mới. Snapshot là policy/duration tại thời điểm tạo, vẫn hiển thị khi policy đã deactivate. SLA tính 24/7, Waiting không pause; reopened giữ deadline gốc và breach cũ.

Notification mô phỏng một số cập nhật gần nhất trên mỗi Ticket, được lọc bằng `TicketScope.Readers` và internal visibility, chỉ gửi cho **tài khoản demo mới**, loại trừ actor. Đánh dấu `DispatchedAt` như lịch sử đã phát để worker không phát hàng loạt sự kiện cũ đến client. Không tạo phiên đăng nhập sẵn và không nạp notification cho tài khoản đang có.

Không tạo metadata file giả: bộ này không upload object MinIO. Có thể dùng UI để thêm file thật sau khi đăng nhập tài khoản phù hợp.

## Kiểm tra dashboard và báo cáo

1. Đăng nhập `admin` hoặc `reports`; mở dashboard và báo cáo Ticket, chọn 7 ngày tương ứng với ngày seed, không lọc team/agent ban đầu.
2. Xem status/priority, nhóm chưa phân công, agent chưa phân công, SLA met/breached/pending và Ticket không SLA. Báo cáo có thể bao gồm cả dữ liệu cũ phù hợp bộ lọc; tổng toàn hệ thống không mặc định bằng 144.
3. Dùng filter team/agent để so sánh. Đăng nhập `lead.it` hoặc `agent.it.1` sẽ thấy cohort nhỏ hơn; requester chỉ thấy Ticket của mình. `accounts` không được vào dashboard dù có quyền quản lý user.
4. Mở cùng Ticket bằng requester và agent để đối chiếu public/internal; `reports` đọc toàn bộ Ticket nhưng vẫn không có quyền nội bộ.

Báo cáo daily hiện bucket theo ngày **UTC**; UI hiển thị thời gian theo timezone người dùng. Khi đối soát SQL, dùng cùng UTC period/cohort/ACL và `asOf` của API. SLA pending có thể trở thành overdue khi thời gian thực trôi qua; đây là hành vi bình thường của dữ liệu vận hành.

Marker `identity.identity_audit_logs` có `action='DemoWeekSeeded'`, `data.Batch='helpdesk-demo-week-v1'`, mốc `AsOf`/`CreatedFrom` và số lượng. Không xóa marker để chạy lại; việc đó sẽ gặp kiểm tra trùng tên/email và không tạo batch mới.

## Kiểm thử

```powershell
.\be\tests\Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~DemoWeekSeedTests'
```

Tests dùng PostgreSQL riêng: kiểm tra timeline/status, SLA–public response, login và ACL own/team/all, tách user.manage/role.manage, internal visibility, giữ account/grant/live policy cũ, chạy lặp/đồng thời, collision và rollback sau khi SQL lịch sử đã chạy. Không dùng database Helpdesk thật để chạy tests.

Database dùng để trình diễn cần được kiểm soát như môi trường có tài khoản thử nghiệm, đặc biệt tài khoản demo SystemAdmin. Không giữ bộ tài khoản này trong môi trường phục vụ người dùng thật sau nghiệm thu; deactivate bằng API quản trị theo quy trình dự án và giữ lịch sử nghiệp vụ.

## Kết quả nạp và đối soát ngày 30/10/2026

Đã chạy vào PostgreSQL Helpdesk Compose hiện có, anchor **22:17:22 UTC+07:00 ngày 30/10/2026**, khoảng tạo Ticket **24–30/10/2026**. Đã lưu backup trước khi ghi. Chạy lại script trả `already exists; no changes`; marker và tuần gốc được giữ nguyên.

Đã thêm 30 tài khoản, 144 Ticket, 438 comment, 1.118 activity, 128 SLA snapshot và 2.085 notification lịch sử. Đối chiếu digest xác nhận tài khoản cũ, 5 Ticket cũ và grant role cũ không thay đổi.

| Status của 144 Ticket mới | Số lượng |
| --- | ---: |
| New | 21 |
| InProgress | 25 |
| WaitingForRequester | 22 |
| Resolved | 26 |
| Closed | 27 |
| Reopened | 12 |
| Cancelled | 11 |

Priority: Low 28, Normal 73, High 28, Urgent 15. Số Ticket theo ngày khớp 12/16/24/28/26/22/16.

Kiểm tra trực tiếp API đang chạy tại port 5000 bằng tài khoản mới:

- `admin`: dashboard và daily cùng **149 Ticket**, gồm 144 mới + 5 cũ; 128 có SLA, 21 không SLA. First response: 64 met / 53 breached; resolution: 25 met / 89 breached / 3 pending (đã loại Cancelled theo contract).
- `requester.01`: dashboard và daily cùng **11 Ticket của chính mình**.
- Hai phiên kiểm tra đã logout sau khi đối soát. Các chỉ số pending/overdue có thể thay đổi sau thời điểm kiểm tra.

Release build không warning/error; **7 kiểm thử seed** trên PostgreSQL tạm đều pass, không skip. File `.local/demo-week/live-report-verification.json` lưu kết quả API tại thời điểm đối soát, không chứa token hoặc mật khẩu.
