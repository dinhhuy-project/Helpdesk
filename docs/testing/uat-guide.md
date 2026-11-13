# Hướng dẫn nghiệm thu và dữ liệu mock Helpdesk

Đối chiếu code ngày **04/11/2026**. Đọc [use case và luồng nghiệp vụ](../use-cases-and-business-flows.md) trước, sau đó thực hiện [test cases](test-cases.md). Bộ này mô tả cách chạy; **chưa ghi nhận kết quả Pass/Fail của môi trường đang deploy**.

## 1. Chọn môi trường và ghi nhận lần chạy

Có hai cách nghiệm thu:

| Cách | Dùng khi | Giới hạn |
| --- | --- | --- |
| Database UAT riêng, bootstrap Identity nhưng chưa có Ticket/policy SLA | Muốn đối chiếu số liệu chính xác, thử admin cuối, thu hồi quyền, giới hạn file, policy ngắn | Phải trỏ API/storage đến tài nguyên UAT; không reset database đang sử dụng để đạt tiền điều kiện. |
| Database demo hiện tại | Demo happy path, ACL bằng tài khoản mẫu mới, đọc báo cáo hiện có | Không kỳ vọng tổng hệ thống bằng 4; tránh đổi grant/policy dùng chung, dừng storage hoặc deactivate tài khoản thật. |

Hướng dẫn cài/migrate/khởi chạy đã có tại [installation-and-running.md](../installation-and-running.md). Nếu hệ thống đang chạy, không cần chạy lại bootstrap/deploy/seed. Đối với UAT riêng, operator chuẩn bị một database rỗng riêng, cấu hình connection và bucket riêng, review/apply migration rồi seed Identity/bootstrap admin theo tài liệu cài đặt. Không dùng `EnsureCreated`, SQL tự đặt status/xmin, hoặc `docker compose down -v` để chuẩn bị bài test.

Trước khi nhập dữ liệu:

1. Ghi commit đang chạy, thời điểm, browser/timezone, URL frontend/API/hub, trạng thái migration, worker SLA và MinIO. Kiểm tra môi trường đích với operator.
2. Chọn `runKey`, ví dụ `UAT-20261104-A`; dùng key mới/email mới cho lần khác. File [uat-data.json](uat-data.json) có **8 user mới + admin sẵn có**, 2 department, 2 team, 4 category, 4 Ticket báo cáo và 1 Ticket chính. Đây là blueprint nhập liệu, không phải lệnh seed hoặc DTO gửi nguyên khối.
3. Thay email/ngày/prefix khi đổi runKey. Mật khẩu `Uat-Only!2026-PasswordAlpha` / `Uat-Only!2026-PasswordBravo` trong fixture là **giả, chỉ cho UAT local cô lập**. Với demo Internet dùng password riêng mạnh và lưu ngoài git; không dùng mật khẩu mẫu này.
4. Giữ admin thật trong file credentials riêng. Không dán mật khẩu đang sử dụng vào fixture, test report, ảnh minh chứng hay tài liệu.
5. Mở các **browser profile/context riêng**: A1 (admin), R1, R2, LA, AA, AB. Hai tab cùng profile không phải hai người dùng độc lập. Token hiện nằm trong memory; reload yêu cầu đăng nhập lại là hành vi hiện tại.
6. Trong DevTools bật Network/Preserve log để đối chiếu nhưng không xuất HAR thô có Authorization/token. Chỉ ghi method/path/status/traceId đã lọc; không ghi password/token vào console.

Tạo bản ghi local `.local/uat/run-map.json` (đã nằm dưới thư mục local bỏ qua git), cấu trúc tham khảo:

```json
{
  "runKey": "UAT-20261104-A",
  "frontendUrl": "<URL thực tế>",
  "apiOrigin": "<API origin thực tế>",
  "ids": { "R1": "<GUID user R1>", "TA": "<GUID team A>", "C-VPN": "<GUID danh mục VPN>", "T-MAIN": "<GUID Ticket>" },
  "reportRange": { "createdFrom": "<ISO 8601 có Z/offset>", "createdTo": "<ISO 8601 có Z/offset>" }
}
```

`<...>` là placeholder phải thay. GUID/TicketNumber/version do API trả; **không gửi key R1/TA hoặc version tự tăng**. Version cần GET lại trước từng mutation, không dùng cố định từ lúc chuẩn bị. Không lưu token vào run-map.

## 2. Chuẩn bị dữ liệu theo đúng thứ tự

### 2.1. Tài khoản và tổ chức

Đăng nhập A1 có đầy đủ grant hiện tại. Dữ liệu chi tiết xem JSON; tên dưới đây dùng key để tránh nhập sai GUID.

| Key | Tài khoản fixture | Role đích | Phòng ban / membership |
| --- | --- | --- | --- |
| A1 | Admin bootstrap sẵn có; không chép credential | SystemAdmin | Không cần membership để đọc toàn cục nếu có read.all |
| A2 | `uat.20261104.a.admin2@example.test` | SystemAdmin | Admin dự phòng; dùng bài bảo vệ admin cuối trên UAT riêng |
| LA / LB | `uat.20261104.a.lead.it@example.test` / `...lead.hr@example.test` | TeamLeader | LA trưởng TA; LB trưởng TB |
| AA / AA2 | `uat.20261104.a.agent.it1@example.test` / `...agent.it2@example.test` | SupportAgent | Thành viên thường TA |
| AB | `uat.20261104.a.agent.hr@example.test` | SupportAgent | Thành viên thường TB |
| R1 / R2 | `uat.20261104.a.requester1@example.test` / `...requester2@example.test` | Requester | R1 phòng IT, R2 phòng HR; không membership |

Các email viết `...` trong bảng là rút gọn trình bày; copy email đầy đủ từ JSON.

1. Vào **Phòng ban** `/admin/departments` → tạo D-IT, D-HR bằng tên/description trong JSON. Ghi ID/version.
2. Vào **Tài khoản người dùng** `/admin/users` → tạo lần lượt 8 user: email, displayName, password. POST create chỉ có 3 field này; role mặc định Requester, active. Mở detail và gán department bằng form cập nhật sau khi tạo.
3. Vào `/admin/roles/assignments` → chọn từng user → gán danh sách role đích ở bảng. Dùng A1 gán SystemAdmin cho A2; A1 không tự sửa role của chính mình. PUT role là thay thế toàn bộ danh sách, phải giữ role cần thiết.
4. Vào **Nhóm hỗ trợ** `/admin/teams` → tạo TA thuộc D-IT, TB thuộc D-HR. Thêm LA/AA/AA2 vào TA và LB/AB vào TB; trước hết membership thường, sau đó A1 bật leadership cho LA/LB. Chưa thêm LA vào TB: cần giữ trường hợp âm chuyển nhóm.
5. Kiểm tra LA đăng nhập, thấy TA trong phạm vi nhưng không được quản lý TB. R1 ở D-IT vẫn không đọc Ticket TA của người khác chỉ nhờ cùng phòng.
6. Vào **Danh mục yêu cầu** `/admin/categories` → tạo root C-NET, C-OFF; tạo C-VPN dưới C-NET, C-PRINT dưới C-OFF. Chưa deactivate các node nền trước happy path.
7. Đăng nhập từng user, mở `/account` và đối chiếu role/permission; A1 kiểm tra membership/leadership. Nếu database đã chỉnh role mặc định, ghi chênh lệch rồi xử lý ở UAT riêng, không mặc định seed sẽ ghi đè grant.

Những bài cần role chỉ có `user.manage`, chỉ có `role.manage`, chỉ `ticket.create`, hoặc assignee không có read grant dùng **fixture automation hiện có**. UI không tạo role mới; không sáng tác màn hình/API tạo role để làm các bài này. Có thể dùng `DemoAccountManager` nếu môi trường đã seed đúng role đó, sau khi kiểm tra grant.

### 2.2. Nhóm 4 Ticket để so báo cáo chính xác

Chỉ áp dụng tổng cố định khi database UAT **chưa có Ticket nào và chưa có policy SLA active**. Ghi `createdFrom` trước khi tạo, `createdTo` sau khi hoàn tất (phải lớn hơn thời điểm tạo cuối); dùng cùng range ở mọi API. Nếu hệ thống có dữ liệu khác, dùng bộ automation reporting hoặc ghi baseline/delta theo cùng cohort, không khẳng định tổng=4. API báo cáo không có filter prefix title/category để cô lập dữ liệu này.

1. R1 tạo RPT1 và RPT2 tại `/tickets/new` theo JSON. R2 tạo RPT3 và RPT4. Chọn category, priority; mọi Ticket ban đầu New, không phân công.
2. A1 mở RPT2, chọn TA/AA → **Lưu phân công**. AA mở RPT2 → chuyển InProgress.
3. A1 phân công RPT3 cho TB/AB. AB chuyển InProgress → Resolved, nhập resolutionSummary trong fixture. Chưa confirm.
4. A1 phân công RPT4 cho TA/AA. AA chuyển InProgress → Resolved kèm summary; R2 mở detail → confirm Closed.
5. Giữ RPT1 New, không team/agent. Mở `/dashboard` và `/reports/tickets`, nhập cùng khoảng tạo, bỏ filter status/priority/team/agent; đối chiếu bảng sau và response API.

| Thước đo | Kỳ vọng chỉ trong nhóm 4 Ticket |
| --- | --- |
| Tổng A1/A2 | 4 |
| R1 / R2 / AA / AA2 / LA / AB đọc được | 2 / 2 / 2 / 2 / 2 / 1 |
| Status | New 1, InProgress 1, Resolved 1, Closed 1; các status khác 0 |
| Priority | Low 0, Normal 2, High 1, Urgent 1 |
| Team | TA 2, TB 1, chưa phân công 1 |
| Agent | AA 2, AB 1, chưa phân công 1 |
| SLA | Không Ticket nào có snapshot; tỷ lệ đo chưa có mẫu phải null/“chưa có dữ liệu”, không 100% |
| Daily | Tổng các bucket = 4; bucket theo ngày tạo UTC, có thể chia hai ngày nếu tạo qua mốc UTC |

AA2 đọc được RPT2/RPT4 vì membership TA nhưng không tự có quyền xử lý RPT2: không phải assignee/leader. Đây là khác biệt read scope và mutation scope. Sau khi ghi evidence, những test thay assignment/status/quyền sẽ làm số liệu thay đổi; không dùng lại tổng cũ.

Trong JSON response dashboard, đối chiếu `tickets.total=4`, `sla.withSla=0`, `sla.withoutSla=4`, `sla.firstResponse.measured=0`, `sla.resolution.measured=0`; hai `metPercent` và `averageMinutes` đều null khi chưa có mẫu. `expected.slaComplianceRate` trong fixture chỉ diễn tả kỳ vọng “chưa có tỷ lệ”, không phải tên field DTO. Group report nằm trong `groups.items`, daily nằm trong `days` với `createdTickets`.

### 2.3. SLA và Ticket happy path

Sau khi nghiệm thu nhóm báo cáo, A1 tạo 4 policy ở `/admin/sla-policies` bằng mảng `slaPoliciesAfterReportFixture` trong JSON. Không tạo policy trùng priority active. Nếu đã có policy, giữ nguyên và tính kỳ vọng theo policy thực tế, hoặc làm tại database UAT riêng; không deactivate policy của người đang dùng.

R1 tạo **T-MAIN**: title/description/category/priority trong JSON. Ghi ID, TicketNumber, version và SLA deadlines. RPT1 vẫn `sla:null` dù vừa có policy. Tạo Ticket riêng tên `<runKey> <case-ID>` cho từng bài làm đổi trạng thái; không dùng lại Closed/Cancelled cho bài yêu cầu Ticket mở.

### 2.4. Tạo file mock

Tại root repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\docs\testing\Generate-UatFiles.ps1
```

Sinh 8 file và `manifest.json` trong `.local/uat/mock-files`; không gọi database/MinIO. Script từ chối ghi đè file đã có; chạy lần khác dùng `-OutputDirectory .local/uat/run-b/mock-files`, hoặc `-Force` khi chủ động thay fixture. Mặc định oversize = 10 MiB + 1 byte. Đọc `/api/v1/tickets/{id}/attachments/options` trước; nếu `policy.maxFileSizeBytes` môi trường nhỏ hơn, truyền `-MaxFileBytes <số-byte-thực-tế>` (tối đa 10 MiB). Đây là tên tham số script, không phải tên field API.

| File | Chọn MIME khi gọi API | Mục đích |
| --- | --- | --- |
| `uat-public.txt` | text/plain | Public upload/download; so SHA-256 với manifest |
| `uat-internal.txt` | text/plain | Internal upload; marker không được xuất hiện ở R1 |
| `uat-image.png` | image/png | PNG 1×1 hợp lệ |
| `uat-empty.txt` | text/plain | 0 byte, bị chặn |
| `uat-invalid-utf8.txt` | text/plain | Byte không phải UTF-8 hợp lệ |
| `uat-fake.pdf` | application/pdf | Không có PDF signature, bị chặn |
| `uat-blocked.exe` | application/octet-stream | Nội dung chỉ là text vô hại; extension không được phép |
| `uat-oversize.txt` | text/plain | Vượt giới hạn chính xác 1 byte, không phải test quota tổng |

Tên kiểu `../evil.txt` không tạo trên disk; gửi filename tùy chỉnh bằng multipart API/automation. Không dùng malware thật. Giới hạn file/MIME là validation hiện có, không phải bằng chứng đã quét antivirus.

## 3. Kịch bản demo xuyên suốt bằng giao diện

Các Ticket ghi trong bảng là T-MAIN. Dùng các profile đã chuẩn bị, giữ một profile Guest không đăng nhập.

| Bước | Người và thao tác cụ thể | Kết quả phải nhìn thấy/đối chiếu |
| --- | --- | --- |
| 1 | Guest vào `/knowledge`, tìm “VPN”; vào `/incidents` | Chỉ bài đã publish đúng dataset; không đòi login để đọc. Nếu chưa có bài, empty state là đúng, chưa chứng minh retrieval có nguồn. |
| 2 | R1 `/tickets/new` → nhập T-MAIN → chọn root Mạng, mở cây, chọn VPN → gửi một lần | Server 201, detail mới; requester/creator=R1, số do DB sinh, New; activity tạo và SLA snapshot nếu policy active. |
| 3 | R2 dán link T-MAIN; AB thử cùng link | 404/không khả dụng, không thấy title/description từ cache. A1 vẫn đọc được bằng read.all. |
| 4 | A1 detail → chọn TA/AA → **Lưu phân công** | Assignment đúng; status vẫn New. Sau commit AA/LA đọc được, AB không đọc được. |
| 5 | AA chuyển InProgress; LA mở kiểm tra workflow | Người xử lý/leader hợp lệ thấy thao tác; AA2 đọc được nhưng không xử lý nếu không được giao. |
| 6 | AA gửi public comment `publicCommentAgent` | R1 thấy public, notification đúng người; first public response được chốt nếu đủ điều kiện. |
| 7 | AA gửi `internalNote`, upload `uat-internal.txt` vào chính comment của AA | AA/LA đủ quyền thấy nội bộ; R1 không tải comment/file/activity nội bộ. Note/file không tính first response. |
| 8 | R1 gửi `publicCommentRequester`, mở **File của bình luận**, upload `uat-public.txt` | Author R1, file kế thừa public. AA tải qua API Bearer; SHA-256 khớp. R1 không upload vào comment của AA. |
| 9 | AA chuyển WaitingForRequester; R1 bổ sung public comment | Status vẫn Waiting; SLA tiếp tục 24/7. AA phải chuyển lại InProgress trước khi resolve. |
| 10 | AA về InProgress → Resolved; nhập `resolutionSummary` → xác nhận thao tác | Summary bắt buộc, version mới, activity cùng commit, resolvedAt/SLA phản ánh thời điểm resolve. |
| 11 | R1 mở lại (Reopened), AA vào InProgress rồi resolve lại | Giữ lịch sử và deadlines ban đầu; không snapshot policy mới. |
| 12 | R1 confirm Closed; thử comment/upload/chuyển trạng thái mới | Đọc lịch sử được; không ghi mới bằng các command đã đóng. |
| 13 | R1 mở `/notifications`, lọc chưa đọc → mở detail → đánh dấu đọc | Count/list cập nhật, không thấy thông báo của người khác hoặc nội dung nội bộ. |
| 14 | A1 mở dashboard/report cùng filter, xem nhóm chưa phân công và SLA | Dữ liệu server theo scope; T-MAIN làm cohort thay đổi, không còn kỳ vọng tổng cố định ở mục 2.2. |

Để kiểm cache: AA từng đọc internal → logout → R1 login trong cùng profile → mở link Ticket. Kiểm Network responses, DOM và dữ liệu Query cache bằng tooling có sẵn: không còn marker `UAT-INTERNAL-ONLY-20261104-A`, không xuất hiện muộn khi response AA cũ hoàn tất. HTTP request không tải nội dung nội bộ quan trọng hơn việc chỉ ẩn một nút UI.

## 4. Knowledge, sự cố, truy xuất và AI

### 4.1. Soạn và publish

1. A1 mở `/admin/knowledge/new`, nhập K-DEMO theo JSON; chọn dataset **Demo**. Nhập content bằng các dòng `1.`, `2.`, `3.` liên tiếp; điền phạm vi, điều kiện, kết quả, escalation, nguồn và ngày cập nhật. Không dán nguyên JSON vào form.
2. Lưu draft. Guest mặc định Operational không thấy; ngay cả Demo cũng chưa thấy bản nháp. Người duyệt chọn đã rà soát nội dung public và publish version mới nhất. Guest chuyển bộ lọc Demo mới thấy.
3. Chỉ tại database UAT riêng, tạo/publish K-OPS-UAT với dataset Operational để test retrieval/AI có nguồn. Trên môi trường đang phục vụ thật, dùng bài **thực tế đã được người phụ trách duyệt**; không publish fixture giả thành Operational.
4. Sửa draft chứa marker revision2 nhưng chưa publish: Guest/retrieval vẫn thấy revision1. Publish lại: cùng ID, revision tăng, bản public mới. Withdraw: Guest/retrieval không đọc được nữa. Không xóa revision bằng SQL.
5. R1 mở bài public, đánh giá hữu ích và tự giải quyết; kiểm một user chỉ có một trạng thái đánh giá. “Tự giải quyết” không tự đóng Ticket đang mở.
6. A1 mở `/admin/incidents/new`, tạo I-LAB Demo. Publish đầu Investigating; publish các thông điệp Identified → Monitoring → Resolved theo JSON. Guest lọc Demo xem timeline theo thứ tự; R1 xác nhận ảnh hưởng khi sự cố còn mở, không tạo ảnh hưởng mới sau Resolved. Sửa draft không tự đổi timeline public.

### 4.2. API hỗ trợ bằng Postman/HTTP client

Đặt biến private trong HTTP client; không export môi trường có secrets. `apiOrigin` là origin, ví dụ local `http://localhost:5000` hoặc URL HTTPS demo hiện tại; **không thêm `/api/v1` hai lần**. FE local thường `http://localhost:5173`. Link tunnel có thể đổi, lấy từ lần deploy mới, không dùng URL cũ trong ảnh.

```http
POST {{apiOrigin}}/api/v1/auth/login
Content-Type: application/json

{"email":"{{emailR1}}","password":"{{privatePasswordR1}}"}
```

Nhận `accessToken`, `refreshToken`, `expiresAt`, `user`; giữ token ở biến private trong phiên test. `/users/me` dùng AccessToken, không dùng RefreshToken:

```http
GET {{apiOrigin}}/api/v1/users/me
Authorization: Bearer {{tokenR1}}

### Tạo Ticket: thay categoryId bằng ID đã lấy, không gửi requester/creator/status
POST {{apiOrigin}}/api/v1/tickets
Authorization: Bearer {{tokenR1}}
Content-Type: application/json

{"title":"UAT-20261104-A API VPN","description":"Dữ liệu lab kiểm thử API.","categoryId":"{{categoryVpnId}}","priority":2}

### Ghi id và version từ response, GET lại nếu có thay đổi trước khi mutation
POST {{apiOrigin}}/api/v1/tickets/{{ticketId}}/assign
Authorization: Bearer {{tokenA1}}
Content-Type: application/json

{"teamId":"{{teamAId}}","agentId":"{{agentAId}}","version":{{currentVersion}}}

### Agent A có quyền xử lý sau assignment; InProgress = 2
POST {{apiOrigin}}/api/v1/tickets/{{ticketId}}/transition
Authorization: Bearer {{tokenAA}}
Content-Type: application/json

{"status":2,"version":{{currentVersion}}}

POST {{apiOrigin}}/api/v1/tickets/{{ticketId}}/comments
Authorization: Bearer {{tokenAA}}
Content-Type: application/json

{"content":"UAT-INTERNAL-ONLY-20261104-A: ghi chú lab.","isInternal":true}

### Truy xuất không cần Bearer; URL encode khi nhập query có dấu
GET {{apiOrigin}}/api/v1/public/knowledge/retrieve?query=khong%20ket%20noi%20vpn&limit=5

### API trợ lý hiện có, chưa có màn hình chat hoàn chỉnh
POST {{apiOrigin}}/api/v1/public/assistant
Content-Type: application/json

{"question":"Tôi không kết nối được VPN lab, cần làm gì?"}
```

Các block `###` chia request theo HTTP client; nếu client không hỗ trợ, tạo từng request riêng. Chỗ `{{currentVersion}}` là **JSON number** từ response mới, không có dấu ngoặc kép. Sau assignment/transition/comment có thể đổi version: refetch trước mutation tiếp theo. Việc đăng nhập/tokenA1/tokenAA phải làm riêng cho từng user.

Refresh test bằng `POST /api/v1/auth/refresh` với `{"refreshToken":"{{privateRefreshToken}}"}`. Lưu token mới, token cũ chỉ dùng lại trong test replay riêng vì có thể thu hồi family. Logout dùng `POST /api/v1/auth/logout` cùng DTO refreshToken. Không làm test replay trên phiên đang dùng làm happy path.

Multipart: `POST /api/v1/tickets/{id}/attachments`, Authorization Bearer; body form-data gồm `file` kiểu File + `isInternal=false` cho Ticket public. Khi gắn comment, dùng `file` + `commentId`, **bỏ isInternal** để server lấy visibility từ comment. Không tự đặt Content-Type boundary thủ công; HTTP client tạo boundary. Download `GET .../attachments/{attachmentId}/download`, lưu file rồi `Get-FileHash -Algorithm SHA256 -LiteralPath <file>` và so manifest.

Fixture JSON dùng `dataset` để diễn tả mục đích dữ liệu. API biên tập tri thức/sự cố dùng field **`classification`**, không phải `dataset`; metadata hướng dẫn nằm trong object **`guide`**. Ví dụ tạo tri thức Demo qua API:

```http
POST {{apiOrigin}}/api/v1/community/knowledge
Authorization: Bearer {{tokenA1}}
Content-Type: application/json

{
  "title": "[UAT fixture] Hướng dẫn VPN demo",
  "summary": "Bản mẫu, không dùng vận hành.",
  "content": "1. Mở ứng dụng VPN lab.\n2. Chọn hồ sơ UAT-VPN trong mạng thử nghiệm.\n3. Ghi mã lỗi UAT và liên hệ hỗ trợ lab nếu chưa kết nối được.",
  "topic": "VPN",
  "classification": "Demo",
  "guide": {
    "applicability": "Chỉ môi trường lab UAT",
    "prerequisites": "Có ứng dụng và hồ sơ VPN lab",
    "expectedOutcome": "Kết nối lab hoặc thu được mã lỗi thử nghiệm",
    "escalation": "Tạo Ticket UAT; không gửi mật khẩu",
    "sourceName": "UAT fixture, không dùng cho vận hành",
    "sourceUrl": null,
    "sourceUpdatedOn": "2026-11-04"
  }
}

### Dùng id/version mới từ response, chỉ publish sau khi thực sự review
POST {{apiOrigin}}/api/v1/community/knowledge/{{knowledgeId}}/publish
Authorization: Bearer {{tokenA1}}
Content-Type: application/json

{"version":{{currentVersion}},"reviewedForPublic":true}

### Demo phải có dataset query cả khi mở trực tiếp detail
GET {{apiOrigin}}/api/v1/public/knowledge/{{knowledgeId}}?dataset=demo
```

Đổi `sourceUpdatedOn` thành ngày nguồn thực sự được rà soát, không sao chép một ngày tương lai. Khi update draft dùng PUT `.../knowledge/{id}` với các field biên tập và `version` mới. Với incident dùng kind `incidents`, field `status` là tên như `Investigating`, message xuất bản đặt trong body publish. Knowledge dùng kind `knowledge` (số ít), incident dùng `incidents` (số nhiều); không tự đổi thành `knowledges` hoặc `incident`.

### 4.3. Kỳ vọng retrieval/AI và bảo trì

- Với K-OPS-UAT public hợp lệ và model enabled/khỏe, thử có dấu/không dấu “không kết nối VPN” / “khong ket noi vpn”, cụm VPN và câu diễn đạt khác ở JSON. So **đúng đoạn + nguồn**, không bắt điểm ranking/latency giống nhau. Các từ viết tắt chỉ được normalize theo catalog hiện có, không phải hiểu mọi từ viết tắt tự nghĩ ra.
- Câu không có nguồn: `InsufficientInformation`, hướng dẫn liên hệ hỗ trợ, không tự nghĩ thao tác. Khi model tắt/lỗi: `Unavailable`; không đánh fail vì khác `InsufficientInformation` nếu cấu hình chưa bật.
- Khi có nguồn đúng nhưng model chọn abstain, kiểm lại evidence/model; không ghi Pass Answered chỉ vì HTTP 200. Body có status nghiệp vụ `Answered/InsufficientInformation/Unavailable/Busy/SourcesChanged`; 429 là rate limit HTTP riêng.
- Không cài/tải model bằng bài test này. Operator bật AI local theo [public-assistant.md](../public-assistant.md). Chạy query không cần AI theo [knowledge-retrieval.md](../knowledge-retrieval.md).
- Check/rebuild dùng lệnh ở [knowledge-index-synchronization.md](../knowledge-index-synchronization.md), manifest riêng mỗi môi trường. `check` có thể chạy read-only DB; `rebuild` là REINDEX có khóa, chỉ thực hiện bài test lỗi/rollback trên DB test hoặc khoảng bảo trì đã thống nhất. Không drop index đang phục vụ để “thử lỗi”.
- BookStack/Wikipedia là tùy chọn; xem [knowledge-integrations.md](../knowledge-integrations.md). Wikipedia chỉ nguồn tham khảo, không tự trở thành corpus trợ lý. BookStack import tạo draft Unclassified, không tự publish hoặc tự áp revision mới.

## 5. Chạy kiểm tra tự động hiện có

Các lệnh sau là hướng dẫn chạy, **không phải kết quả đã chạy trong lần viết tài liệu**. Tại root repo, có Node đúng `fe/package.json`, .NET 8 và Docker Desktop:

```powershell
npm --prefix fe ci
npm --prefix fe run lint
npm --prefix fe test
npm --prefix fe run build
dotnet restore be/be.sln

# Runner dùng PostgreSQL/MinIO tạm riêng; không trỏ HELPDESK_TEST_* vào dữ liệu thật.
powershell -NoProfile -ExecutionPolicy Bypass -File be/tests/Run-PersistenceTests.ps1 -Configuration Release

# Browser thật; nếu chưa có Chromium, cài tại fe/ bằng npx playwright install chromium.
powershell -NoProfile -ExecutionPolicy Bypass -File be/tests/Run-FrontendAcceptanceTests.ps1
```

Kiểm nhanh nhóm cần fixture khó tạo bằng UI:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File be/tests/Run-PersistenceTests.ps1 -Configuration Release -Filter 'FullyQualifiedName~AccountAdministrationTests|FullyQualifiedName~SessionManagementTests|FullyQualifiedName~TicketAccessTests|FullyQualifiedName~SlaTests|FullyQualifiedName~KnowledgeIndexTests'
```

Model thật có runner opt-in `be/tests/Run-LocalAssistantTests.ps1`; đọc hướng dẫn model trước. Testcase ghi **AUTO** là mục tiêu kiểm thử cần fixture có kiểm soát, không khẳng định mỗi ID tương ứng sẵn một test method hoặc toàn bộ suite sẽ chạy UI. Browser journeys hiện có bao phủ nhiều luồng nhưng không thay thế nghiệm thu thủ công trên URL deploy.

## 6. Ghi kết quả, ưu tiên và kết thúc

Copy [test-cases.csv](test-cases.csv) ra `.local/uat/<runKey>/results.csv`, cập nhật `Status` = Not run / Pass / Fail / Blocked / N/A, `Actual`, `Evidence`. Bản Markdown là đặc tả; CSV là bản theo dõi tạo từ cùng nội dung. Tất cả bắt đầu **Not run**. Blocked phải ghi thiếu môi trường/quyền/nguồn; N/A chỉ khi module tùy chọn nằm ngoài phạm vi release và có người nghiệm thu xác nhận.

| Ưu tiên | Ý nghĩa / tiêu chí |
| --- | --- |
| P0 | Không rò internal/file/cache, không nâng quyền, phiên thu hồi không còn truy cập; happy path Ticket và version/transaction đúng. Không phát hành nếu Fail. |
| P1 | Chức năng nghiệp vụ chính, dữ liệu báo cáo/SLA, xử lý mạng/reconnect. Phải đạt phần thuộc phạm vi trước nghiệm thu. |
| P2 | Trường hợp bổ sung và tích hợp tùy chọn; ghi rõ module không bật và phạm vi thực tế. |

Một case gồm: key case/use case, tiền điều kiện, dữ liệu, thao tác, kết quả kỳ vọng và phương thức UI/API/AUTO. Ghi actual HTTP/body status, ID tài nguyên, thời điểm và traceId đã lọc; không chỉ chụp toast. Với kiểm internal, ghi cả list/detail/comment/activity/file/notification và trạng thái cache sau đổi user.

Thứ tự: môi trường → tổ chức/user → **report fixture trước SLA** → ghi evidence báo cáo → SLA/T-MAIN → happy path → negatives/thu hồi/concurrency → community/AI → mạng/deploy. Case làm đổi trạng thái hay deactivate dùng bản sao `<runKey> <case-ID>` hoặc khôi phục có chủ đích bằng UI, refetch version. Bài admin cuối thực hiện cuối trên UAT riêng, có A2/credential dự phòng; không đụng admin thật trên demo public. Bài chính xác thời gian/transaction/network receipt dùng runner, không sửa đồng hồ máy hoặc SQL thẳng.

Chốt nghiệm thu khi P0/P1 trong phạm vi có evidence, case còn Blocked/Fail được ghi rõ, có kiểm tra deep link/HTTPS/CORS/hub trên URL đích. Hoàn thành UAT không đồng nghĩa hoàn thành các cổng production: backup/restore, readiness, quan sát worker/log, secret rotation và rollback xem [release acceptance](../frontend-release-acceptance.md).

Sau test không xóa lịch sử bằng SQL hoặc tự xóa volumes. Withdraw bài fixture đã publish, deactivate tài khoản/tổ chức mock theo kế hoạch giữ evidence; database UAT dùng một lần chỉ được dọn đúng môi trường bởi operator. Trên database thật không publish nội dung giả thành tri thức Operational.
