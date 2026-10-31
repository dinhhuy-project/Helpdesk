# Kho tri thức và sự cố chung công khai

Cổng tra cứu hoạt động **trước khi đăng nhập** tại `/knowledge` và `/incidents`. Trang chủ, header công khai và trang đăng nhập đều có đường dẫn truy cập. Người đã đăng nhập dùng cùng nội dung công khai, có thêm đánh giá hữu ích/tự giải quyết và xác nhận ảnh hưởng.

## Cài đặt và dữ liệu ban đầu

1. Backup database, đọc [review migration PublicCommunity](public-community-review.md), rồi áp dụng SQL forward hoặc `dotnet ef database update --project be` với connection/environment đã cấu hình đúng.
2. Chạy seed catalog và cấp quyền cho SystemAdmin hiện có:

```powershell
dotnet run --project be --no-launch-profile -- seed-identity
dotnet run --project be --no-launch-profile -- grant-community-administration
```

`seed-identity` thêm bốn permission còn thiếu, giữ nguyên grant của role đã tồn tại. SystemAdmin được tạo mới có cả bốn quyền ngay từ đầu; role cũ cần lệnh grant riêng ở trên. Không mở rộng quyền của Requester/Agent/TeamLeader cũ.

3. Tùy chọn nạp nội dung mẫu đã biên soạn riêng cho cổng public:

```powershell
$env:CommunityDemoSeed__Enabled = 'true'
try { dotnet run --project be --no-launch-profile -- seed-community-demo }
finally { Remove-Item Env:CommunityDemoSeed__Enabled -ErrorAction SilentlyContinue }
```

Lệnh này xuất bản **6 bài hướng dẫn an toàn và 2 sự cố gắn rõ `[Demo]`**, có timeline mô phỏng. Không chuyển Ticket thật sang public. Chạy lặp không nhân đôi hay ghi đè nội dung đã sửa. API startup không tự chạy lệnh này. Các lệnh CLI dùng connection/environment/user-secrets đang cấu hình cho backend; với user-secrets Development cần đặt `ASPNETCORE_ENVIRONMENT=Development` và `DOTNET_ENVIRONMENT=Development` như hướng dẫn setup hiện có.

4. Restart backend và frontend nếu đang dùng bản build cũ. Mở cổng public, sau đó đăng nhập SystemAdmin để thấy menu biên soạn và quản trị sự cố.

## Phân quyền

| Permission mới | Quyền thực hiện |
| --- | --- |
| `knowledge.manage` | Đọc danh sách/detail bản soạn tri thức, tạo/sửa draft, tạo draft từ Ticket được phép đọc. |
| `knowledge.publish` | Đọc bản soạn để duyệt, xuất bản/thu hồi tri thức. Không tự có quyền sửa draft. |
| `incident.manage` | Đọc, tạo và sửa bản soạn sự cố chung. |
| `incident.publish` | Đọc để duyệt, công khai/thu hồi mô tả và cập nhật tiến trình sự cố. |

Đây là quyền biên tập **cấp tổ chức**, không tự kế thừa từ `team.manage`, `category.manage` hoặc tên role. Dùng giao diện role/grant hiện có để cấp thêm cho biên tập viên hay người duyệt phù hợp. Bản soạn có thể chứa nội dung chưa được duyệt; chỉ cấp quyền cho nhóm biên tập được tin cậy.

Đánh giá bài và xác nhận bị ảnh hưởng yêu cầu phiên đang active, không yêu cầu quyền biên tập. Backend đọc lại phiên, trạng thái user và permission hiện tại; thu hồi quyền có hiệu lực với JWT đang dùng. Các thao tác ghi serialize theo advisory lock chung, kiểm tra version/xmin và audit trong transaction.

## Nội dung công khai

API chính (`{kind}` là `knowledge` hoặc `incidents`):

| Method / route | Quyền / hành vi |
| --- | --- |
| `GET /api/v1/public/{kind}` | Anonymous; `search`, `topic`, `page`, `pageSize`, `active` cho incident. |
| `GET /api/v1/public/{kind}/{id}` | Anonymous; detail snapshot public và metadata nguồn. |
| `GET /api/v1/public/incidents/{id}/updates` | Anonymous; timeline public phân trang. |
| `GET /api/v1/community/{kind}` và `/{id}` | Có quyền manage hoặc publish tương ứng; trả bản soạn. |
| `POST /api/v1/community/{kind}` | Quyền manage; tạo draft. |
| `PUT /api/v1/community/{kind}/{id}` | Quyền manage; lưu draft với `version`. |
| `POST /api/v1/community/{kind}/{id}/publish` | Quyền publish; `{version, reviewedForPublic:true, message?}`. |
| `POST /api/v1/community/{kind}/{id}/withdraw` | Quyền publish; `{version}`. |
| `POST /api/v1/community/knowledge/from-ticket/{ticketId}` | `knowledge.manage` và Ticket read ACL; chỉ nguồn Resolved/Closed. |
| `GET /api/v1/community/{kind}/{id}/participation` | Phiên active; phản hồi/xác nhận của chính tài khoản. |
| `PUT /api/v1/community/{kind}/{id}/participation` | Phiên active; `{value,solved}`: helpful/self-resolved cho knowledge, affected cho incident. |

Body lưu draft: `{title, summary, content, topic, status?, version}`. `status` là tên trạng thái incident; knowledge phải để null. Danh tính actor, source Ticket khi tạo từ nguồn và snapshot public do server quản lý.

Knowledge và Incident dùng chung quy trình xuất bản ở `CommunityEntry`, có `Kind` và trường phù hợp từng loại. Bốn bảng mới không thay đổi Ticket/workflow/SLA hiện có.

- Soạn nội dung bằng văn bản thuần: tiêu đề, chủ đề, tóm tắt và hướng dẫn/mô tả. UI render bằng React text, không thực thi HTML từ nội dung.
- `Lưu bản soạn` giữ nguyên snapshot công khai trước đó. Người đọc và kết quả tìm kiếm tiếp tục thấy phiên bản đã duyệt.
- `Xuất bản công khai` cần đúng server version, checkbox xác nhận được phép công khai và quyền publish tương ứng. Mỗi lần xuất bản tăng `revision` và cập nhật `publishedAt`.
- `Thu hồi công khai` loại nội dung khỏi danh sách/search/detail/timeline public, giữ lại bản soạn và lịch sử trong database.
- Conflict 409 giữ nội dung đang nhập. Người dùng chủ động tải version mới, kiểm tra trước khi lưu/xuất bản lại. Khi timeout chưa rõ kết quả, UI không tự gửi lại POST.

API public trả projection riêng: ID, kind, title, summary, content (detail), topic, status nếu có, revision, publishedAt, sourceUrl và các số đếm tổng hợp. Không trả source Ticket, author/user ID, email, quyền, nội dung internal hoặc file.

Danh sách/search/filter/page chạy trên server; pageSize 1–50, từ khóa tối đa 200 ký tự, chủ đề tối đa 100. FTS theo token `simple`, không phân biệt hoa thường; hiện vẫn phân biệt dấu tiếng Việt và không phải tìm kiếm ngữ nghĩa. Bộ lọc chủ đề là tên đầy đủ. Trên form tạo Ticket, người dùng có thể chủ động tìm hướng dẫn theo tiêu đề và kiểm tra sự cố chung trước khi gửi yêu cầu.

## Tạo tri thức từ Ticket

Trong detail Ticket đã Resolved/Closed, người có `knowledge.manage` thấy nút `Soạn bài tri thức từ Ticket`. Backend vẫn áp dụng Ticket read ACL cho ID nguồn.

Chỉ tiêu đề và `resolutionSummary` được dùng để tạo **bản nháp**, không copy description, comment, internal note hoặc file. Người biên soạn cần sửa lại nội dung, loại thông tin riêng tư và xin duyệt công khai. SourceTicketId chỉ có trong phần quản trị; việc đọc bài công khai không cấp quyền đọc Ticket gốc.

## Sự cố chung

Mô tả/ảnh hưởng/status được soạn nháp rồi xuất bản. Mỗi lần publish có một thông điệp timeline công khai; để trống sẽ dùng tóm tắt đã duyệt. Thay đổi status trong draft chưa đổi trạng thái mà người đọc thấy.

Trạng thái:

```text
Lần xuất bản đầu: Investigating
Investigating → Identified / Monitoring / Resolved
Identified    → Monitoring / Resolved
Monitoring    → Investigating / Resolved
Resolved      → Investigating (mở lại)
```

Có thể xuất bản cập nhật cùng status. Incident được khắc phục vẫn tra cứu được qua bộ lọc `Đã khắc phục` hoặc `Tất cả`.

Timeline là lịch sử append-only. Thu hồi incident sẽ ẩn toàn bộ timeline; xuất bản lại cũng mở lại các cập nhật cũ. Nếu cần loại bỏ một cập nhật đã công khai nhầm, giữ incident cũ ở trạng thái thu hồi và biên soạn incident mới; giai đoạn này chưa có thao tác biên tập/xóa riêng từng cập nhật.

`Tôi cũng gặp vấn đề này` dùng PK `(entryId,userId)`, mỗi tài khoản tính một lần dù gửi lại PUT. Người dùng có thể gỡ xác nhận; incident đã Resolved không nhận xác nhận mới. Số đếm là các xác nhận hiện còn lưu, không công khai danh sách người tham gia. Sự cố chung có workflow riêng; không tự merge, đóng hoặc thay đổi SLA của Ticket cá nhân.

## Đánh giá tri thức

Mỗi tài khoản có một feedback trên một bài, có thể cập nhật hữu ích/chưa giúp giải quyết và đã tự giải quyết. `solved=true` yêu cầu `helpful=true`. Public chỉ có số lượng hữu ích/tự giải quyết, không có danh tính người đánh giá. Khách chưa đăng nhập chỉ tra cứu và được mời đăng nhập khi muốn tham gia.

## Realtime, cache và deploy

Hub `/hubs/community` cho phép anonymous và chỉ phát `CommunityChanged {kind,id}`. Không nhận tên group từ client, không có WatchTicket/Subscribe tùy ý và không dùng JWT của phiên. Handler đăng ký trước start; frontend reset/refetch dữ liệu public sau event, reconnect hoặc focus, đồng thời poll mỗi 30 giây. Connection được stop khi rời cổng tra cứu.

Broadcast thực hiện sau database commit, best effort; lỗi phát không biến một publish đã commit thành lỗi ghi. Giai đoạn này không có outbox/delivery bảo đảm, không gửi email hoặc notification Ticket cho người xác nhận sự cố. Người theo dõi đang mở cổng nhận cập nhật realtime; lần truy cập sau luôn đọc trạng thái hiện tại từ API.

API dùng `Cache-Control: no-store`; query public staleTime/gcTime bằng 0 và dùng HTTP client không gắn Bearer. Endpoint đọc public/participation và negotiate hub giới hạn 120 request/phút/IP/process. Nginx template hiện có đã proxy `/api` và `/hubs`, hỗ trợ SPA deep link; script `verify:deployment` bổ sung kiểm tra hai cổng và hub anonymous. CORS/HTTPS vẫn phải đúng origin frontend/backend theo tài liệu deploy.

SignalR hiện phù hợp một API instance, cùng giới hạn với notification cũ. Trước khi triển khai nhiều instance cần backplane/transport và chiến lược phát phù hợp.

## Nền tảng tích hợp AI

Giai đoạn này chuẩn bị **nguồn dữ liệu đã duyệt**, chưa gọi LLM, chưa cài vector database và không yêu cầu API key.

Đã bổ sung [nhập bản nháp từ BookStack và tra cứu Wikipedia tiếng Việt](knowledge-integrations.md). Wikipedia hoạt động trước đăng nhập; BookStack yêu cầu `knowledge.manage` và token đọc ở backend. Bài nhập phải được duyệt như bài Helpdesk khác; chưa có đồng bộ tự động nguồn ngoài.

- Corpus: chỉ dùng `/api/v1/public/knowledge` và detail; có thể dùng incident public hiện tại cho câu hỏi về tình trạng dịch vụ.
- Mỗi nguồn có ID ổn định, `revision`, `publishedAt` và `sourceUrl` để lưu chunk, phát hiện thay đổi và trích dẫn về `/knowledge/:id` hoặc `/incidents/:id`.
- Bản nháp không nằm trong FTS public; không lấy dữ liệu từ endpoint quản trị hoặc quét trực tiếp Ticket/comment/file cho chatbot công khai.
- Khi làm RAG, đồng bộ việc sửa/thu hồi và kiểm tra lại nguồn còn published trước khi đưa câu trả lời/trích dẫn. Vector index không thay thế quyết định public hiện tại của database.
- Nội dung truy xuất là dữ liệu, không phải chỉ dẫn hệ thống cho mô hình. Đánh giá sau này cần bao gồm câu hỏi không có nguồn, nguồn bị thu hồi, dữ liệu đã thay đổi và khả năng lẫn nội dung nội bộ.

Revision là phiên bản của snapshot công khai hiện tại, chưa phải API lưu và phục vụ toàn bộ phiên bản bài cũ. Có thể thêm ingestion/outbox/chunk history khi tích hợp AI thực sự.

## Kiểm thử

```powershell
.\be\tests\Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~CommunityTests|FullyQualifiedName~FrontendCommunityJourneyTests|FullyQualifiedName~ModelTests'
cd fe
npm test
npm run lint
npm run build
```

Browser production bundle riêng:

```powershell
$env:HELPDESK_ACCEPTANCE_JOURNEYS = 'Community'
try { .\be\tests\Run-FrontendAcceptanceTests.ps1 }
finally { Remove-Item Env:HELPDESK_ACCEPTANCE_JOURNEYS -ErrorAction SilentlyContinue }
```

Tests chạy trên PostgreSQL riêng: draft/published/search, source Ticket ACL, grant bị thu hồi, inactive user, feedback idempotent, status/affected membership, optimistic concurrency, model/SQL và anonymous hub. JavaScript 10 client chạy với server 8 thật; browser kiểm tra tra cứu trước login, responsive/a11y, biên soạn/duyệt và public HTTP không có Authorization.

## Kích hoạt trên workspace cục bộ ngày 31/10/2026

Đã backup PostgreSQL Compose vào `.local/community/`, review và áp dụng `20261031095438_PublicCommunity`, seed bốn permission mới, cấp cho SystemAdmin và nạp 6 bài hướng dẫn cùng 2 sự cố `[Demo]`. Không chuyển nội dung Ticket đang có ra công khai.

Đối chiếu trước/sau xác nhận **31 tài khoản, 149 Ticket và toàn bộ grant cũ giữ nguyên**. File `.local/community/preflight.json`, `postflight.json` và `live-verification.json` lưu bằng chứng cục bộ không chứa mật khẩu hoặc token. Backup và log nằm trong thư mục riêng chỉ tài khoản hệ điều hành hiện tại truy cập.

Kiểm tra bản API mới với database này: anonymous đọc 6 bài; tìm `VPN` trả 1 bài; lọc incident đang diễn ra/đã khắc phục trả 1/1; hub public negotiate trả 200; management anonymous trả 401; đăng nhập demo SystemAdmin có 21 permission và đọc được phần quản trị. Phiên kiểm tra đã logout, process API tạm đã dừng.

**Với phiên Visual Studio đang chạy bản backend cũ, restart profile Container (Dockerfile)** để nạp code/route mới trên port 5000. Frontend đang dùng Vite sẽ nhận thay đổi source; nếu dùng bundle/container cũ thì build lại frontend. Sau đó mở `/knowledge`, `/incidents`, `/admin/knowledge` hoặc `/admin/incidents` theo quyền.

Các sự cố `[Demo]` dùng cho nghiệm thu, cần loại khỏi corpus AI phục vụ người dùng thực tế. Khi triển khai môi trường khác, thực hiện lại bước review/backup/migration/grant và chọn nội dung công khai phù hợp thay vì mặc định sao chép dữ liệu cục bộ.

Kết quả nghiệm thu: **562 backend tests Release pass, 0 skip; 484 frontend tests pass; lint/build frontend pass; Release build warn-as-error pass**. Browser journey mới trên production bundle đã pass, bao gồm anonymous lookup, responsive 390px, axe accessibility, soạn nháp và duyệt công khai. JavaScript SignalR 10 đã kiểm tra trực tiếp với hub server 8. Test tạo draft từ Ticket còn bao gồm tiêu đề nguồn dài 300 ký tự, được giới hạn phù hợp contract tri thức 200 ký tự.
