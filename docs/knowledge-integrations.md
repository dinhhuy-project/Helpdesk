# BookStack và tài liệu tham khảo Wikipedia

Đã tích hợp hai nguồn bổ sung cho [kho tri thức công khai](public-community.md): **BookStack** để nhóm biên soạn tài liệu riêng rồi nhập thành bản nháp, và **Wikipedia tiếng Việt** để mọi người tra cứu trước đăng nhập tại `/knowledge`.

Không cần migration mới, tài khoản Wikipedia hoặc package trả phí. BookStack là phần mềm MIT miễn phí tự host; vẫn cần tài nguyên máy chủ và backup. Xem [giới thiệu BookStack](https://www.bookstackapp.com/), [REST API](https://demo.bookstackapp.com/api/docs) và [Wikimedia API](https://developer.wikimedia.org/use-content/content/).

## Khởi tạo cục bộ bằng một lệnh

Từ root, sau khi có `.env` và Docker Desktop:

```powershell
.\be\scripts\Initialize-KnowledgeWiki.ps1 -Provision
```

Script sinh APP_KEY/mật khẩu database một lần trong `.env`, giữ giá trị đã có, rồi khởi động `docker-compose.wiki.yml`. Stack `<COMPOSE_PROJECT_NAME>-wiki` có BookStack, MariaDB, healthcheck và hai volume riêng. Dùng `-p` rõ ràng để không nhập nhầm vào stack Helpdesk chính; không thay đổi lệnh khởi tạo PostgreSQL/Redis/MinIO.

`-Provision` dành cho wiki mới cục bộ: thay admin mặc định bằng editor với mật khẩu ngẫu nhiên; tạo sách `Helpdesk - Huong dan ho tro`, hai bài VPN/MFA và reader chỉ được đọc/xuất sách đó. Reader không có quyền tạo/sửa/xóa nội dung hay quản lý user/role. Token reader có hạn một năm; token quản trị tạm phục vụ setup được xóa trong `finally`. Quyền override của các role khác được giữ nguyên.

Mở **`http://localhost:6875`**, đăng nhập editor bằng **`.local/wiki/credentials.txt`**. Trạng thái bootstrap ở `.local/wiki/bootstrap.json`; thư mục chỉ tài khoản Windows hiện tại truy cập, không được commit. Tài khoản wiki độc lập với tài khoản Helpdesk, chưa có SSO.

Script tự cấu hình connector trong backend user-secrets cho Visual Studio Container và `.env` cho API Compose. **Restart profile Container (Dockerfile)** để API port 5000 nạp code/secrets mới. Nếu dùng frontend bundle cũ thì build lại.

Helper bootstrap dùng model/command của image BookStack đã pin, chỉ chấp nhận APP_URL localhost/127.0.0.1. Khi đổi image cần kiểm tra helper; không dùng nó với wiki đang dùng chung/production. Connector khi vận hành chỉ gọi REST API đọc. Với wiki có sẵn, tạo tài khoản/token qua UI và cấu hình backend theo phần dưới.

Chạy lặp không tạo trùng bài hoặc ghi đè nội dung/mật khẩu đã biên tập. Nếu đã đổi/xóa account, token hoặc quyền reader, review trạng thái trước khi chạy lại; script không tự thay token bị đổi. Giữ `.env`, user-secrets và `.local/wiki/` khi dọn Docker. Sau khi mất volumes wiki, chạy lại `-Provision` để dựng lại mẫu; tài liệu từng biên soạn trong volume bị mất cần backup riêng để khôi phục.

Các lần khởi động thường dùng (đổi project name nếu cần):

```powershell
docker compose -p helpdesk-wiki --env-file .env -f docker-compose.wiki.yml up -d --wait bookstack
```

## Sử dụng

- Trước đăng nhập: `/knowledge` → “Mở tra cứu Wikipedia” → nhập từ khóa → “Tìm trên Wikipedia”. Chỉ gửi từ khóa khi người đọc chủ động submit. Kết quả gồm tiêu đề, URL bài gốc và ngày cập nhật; không sao chép toàn văn hay tự trở thành bài Helpdesk đã duyệt.
- Có `knowledge.manage`: `/admin/knowledge` → “Nhập từ BookStack” → tìm trang → xem trước → xác nhận nhập bản nháp. Kiểm tra nội dung/ghi công/quyền sử dụng trong editor; người có `knowledge.publish` duyệt và xuất bản bằng luồng hiện có. Publisher chỉ có quyền publish không được duyệt nguồn wiki riêng hay nhập bài.

Đây là **nhập thủ công một chiều, tạo một lần**. Nhập lại cùng trang của cùng SourceId mở bài đã nhập, không ghi đè bản nháp hoặc snapshot public, kể cả nguồn đã đổi. Cập nhật tiếp trong editor Helpdesk; chưa có lịch đồng bộ, webhook hoặc so sánh thay đổi nguồn.

ID bài nhập suy ra ổn định từ namespace + SourceId + ID trang. Giữ SourceId khi đổi hostname của cùng wiki; các wiki khác nhau phải dùng SourceId khác nhau. Footer văn bản lưu nguồn BookStack, ID trang, revision/ngày cập nhật, ghi công và quyền sử dụng; footer có thể biên tập và chỉ công khai sau khi duyệt. Audit `KnowledgeImported` lưu ID bài/nguồn/trang/revision, không lưu token hoặc URL nội bộ. Chưa có entity provenance/hash riêng; thêm migration mới nếu bổ sung đồng bộ sau này.

## API

| Method / route | Quyền và contract |
| --- | --- |
| `GET /api/v1/public/knowledge-sources` | Anonymous; `{wikipediaEnabled,bookStackEnabled}`, không lộ URL/token/BookId. |
| `GET /api/v1/public/knowledge-sources/wikipedia?search=VPN&offset=0` | Anonymous; từ khóa 1–150 ký tự, offset 0–500; `{items:[{title,url,updatedAt}],nextOffset}`. |
| `GET /api/v1/community/knowledge-sources/bookstack/pages?search=&page=1` | `knowledge.manage`, phiên active; 12 kết quả/trang, page 1–1000. |
| `GET /api/v1/community/knowledge-sources/bookstack/pages/{id}` | `knowledge.manage`; preview văn bản, revision, ghi công/quyền sử dụng và URL public nếu được cấu hình. |
| `POST /api/v1/community/knowledge-sources/bookstack/imports` | `knowledge.manage`; `{pageId}` → `{entry,alreadyImported}`; không tự publish. |

Endpoint dùng no-store và rate limit public hiện có. Input sai → 400; chưa xác thực → 401; thiếu quyền → 403; trang không tồn tại/ngoài sách/draft/template → 404; nguồn đổi trong lúc export → 409; nguồn tắt/lỗi mạng/API → 503 với ProblemDetails/traceId an toàn. Backend kiểm tra phiên/quyền trước/sau fetch và dưới write lock khi ghi. Duplicate import serialize; bài/audit cùng transaction.

## Cấu hình nguồn có sẵn

Tài khoản BookStack cần `Access system API`, `Export content` và quyền đọc đúng sách được nhập. Không dùng token admin; token kế thừa quyền tài khoản tạo theo [tài liệu API](https://demo.bookstackapp.com/api/docs#authentication).

| Backend key `KnowledgeSources:BookStack:*` | Ý nghĩa |
| --- | --- |
| `Enabled` | Mặc định false, chỉ bật khi cấu hình đủ. |
| `SourceId` | Mặc định `support-wiki`; tối đa 60 chữ/số ASCII, `_`, `-`. |
| `BaseUrl` | URL nguồn tin cậy từ backend, ví dụ `https://wiki.example.org/`. Không nhận URL từ form. |
| `TokenId`, `TokenSecret` | Chỉ lưu ở backend. |
| `BookId` | Sách được nhập, ID > 0. |
| `AllowHttp` | Mặc định false; bật rõ ràng cho kết nối cục bộ HTTP. |
| `PublicBaseUrl` | Tùy chọn HTTPS, mặc định trống. Chỉ đặt nếu URL được phép xuất hiện trong bài public. |
| `Attribution`, `License` | Ghi công và quyền sử dụng **nội dung** theo đơn vị sở hữu. MIT của phần mềm không tự cấp quyền công khai tài liệu. |

Trong Docker, dùng `KnowledgeSources__BookStack__*`. Script provision cấu hình `BaseUrl=http://host.docker.internal:6875/` cho VS Container. Nếu chạy API trực tiếp trên host, override BaseUrl thành `http://localhost:6875/`. API Compose dùng `BOOKSTACK_CONNECTOR_ENABLED`, `BOOKSTACK_SOURCE_ID`, `BOOKSTACK_BASE_URL`, `BOOKSTACK_BOOK_ID`, `BOOKSTACK_TOKEN_ID`, `BOOKSTACK_TOKEN_SECRET` trong `.env`.

Với token tự tạo qua UI, cấu hình nguồn cục bộ bằng lệnh dưới; secret nhập ẩn, không nằm trong command arguments:

```powershell
.\be\scripts\Initialize-KnowledgeWiki.ps1 -ConfigureBackend -BookId 1 -TokenId YOUR_READ_ONLY_TOKEN_ID
```

Wikipedia mặc định bật; tắt bằng `KnowledgeSources__WikipediaEnabled=false`. Có thể cấu hình `KnowledgeSources__UserAgent` với tên ứng dụng/thông tin liên hệ phù hợp Wikimedia. Endpoint cố định là `https://vi.wikipedia.org/w/api.php`, người dùng không thay được hostname. Triển khai ngoài local cần domain/HTTPS, backup và quy trình quản lý token của đơn vị; không dùng bootstrap localhost.

## Bảo vệ dữ liệu và chuẩn bị AI

- Token chỉ ở backend; frontend không gọi wiki trực tiếp. JWT Helpdesk không được gửi cho nguồn ngoài. Không tự gửi title/description/comment Ticket ra Wikipedia.
- Không theo redirect hay dùng cookie nguồn; mỗi request timeout 10 giây, response tối đa 1 MiB kể cả chunked/decompressed. Wikipedia cache tối đa 128 truy vấn trong 5 phút, 5 liên kết/trang.
- Chặn trang ngoài sách, draft/template; kiểm tra metadata lại sau export. Nội dung nhập ≤ 28.000 ký tự, dành phần còn lại của giới hạn 30.000 cho ghi công; tiêu đề ≤ 200 theo contract Helpdesk.
- React render văn bản; không thực thi HTML, tải ảnh tracking hoặc tự nhập file. HTTP client logs thông thường không ghi URL từ khóa/token. POST không tự retry khi chưa rõ kết quả; lỗi Wikipedia không làm mất bài Helpdesk.
- URL wiki riêng không tự xuất hiện trong footer. PublicBaseUrl do người vận hành quyết định và người publish vẫn phải duyệt. Quyền đọc wiki không đồng nghĩa quyền công khai.
- Nguồn bị xóa/thu hồi quyền không tự thu hồi bản sao đã publish trong mô hình nhập thủ công; người duyệt phải thu hồi bài Helpdesk liên quan theo quy trình vận hành.
- AI công khai sau này chỉ lấy snapshot Helpdesk đã duyệt, URL/revision và ghi công được phép; kiểm tra lại published khi trả lời. Không dùng token wiki để trả lời trực tiếp từ tài liệu riêng. Kết quả Wikipedia hiện chưa là corpus AI. Nếu nhập toàn văn về sau cần metadata tác giả/revision/giấy phép theo [Wikimedia content reuse](https://developer.wikimedia.org/use-content/content/); ảnh có thể có điều kiện khác văn bản. Chưa thêm LLM/vector database/API key AI.

## Kiểm thử và kích hoạt cục bộ

```powershell
.\be\tests\Run-PersistenceTests.ps1 -Configuration Release -Filter 'FullyQualifiedName~KnowledgeSourceTests|FullyQualifiedName~KnowledgeImportTests|FullyQualifiedName~CommunityTests|FullyQualifiedName~FrontendCommunityJourneyTests|FullyQualifiedName~ModelTests'
cd fe
npm test
npm run lint
npm run build
```

Ngày 31/10/2026 đã chạy stack wiki, tạo editor/reader và hai bài. API Helpdesk mới đã list/preview/import từ BookStack thật; nhập lặp giữ cùng ID, chỉ tạo draft và anonymous đọc draft nhận 404. Đã nhập một bài thử vào bản nháp Helpdesk, chưa publish. Wikipedia qua API Helpdesk trả 5 kết quả thật. Reader bị chặn 403 khi quản lý user/role và tạo sách.

Đã tạo một sách kiểm tra riêng rồi xác nhận reader không đọc được (404) và chỉ nhìn thấy một sách được cấp quyền. Sách kiểm tra và token admin tạm đã được gỡ sau khi xác minh; hai bài hướng dẫn vẫn được giữ.

Browser chạy production bundle đã kiểm tra Wikipedia anonymous, nhập/mở draft, không tự publish, viewport 390px không tràn ngang và axe không có vi phạm trên hai màn hình kiểm tra. API/preview phục vụ kiểm tra đã dừng, phiên VS/Vite của người dùng được giữ. Bằng chứng ở `.local/wiki/live-verification.json`, `.local/wiki/` và log test tạm; không commit credentials/log. Restart VS Container để phiên API thường dùng nạp bản mới trên port 5000.

Kết quả: 29 backend tests liên quan pass, 0 skip; sau thay đổi validator UserAgent đã build Release warn-as-error và chạy lại 14 unit tests connector đều pass. Toàn bộ 490 frontend tests pass; lint/build pass. Lệnh setup một bước đã chạy lại thành công, giữ hai bài và credential hiện có. Đã kiểm tra từ container khác tới `host.docker.internal:6875` bằng token reader và nhận HTTP 200.

Tham chiếu: [BookStack export](https://www.bookstackapp.com/docs/user/export-import/), [LinuxServer BookStack image](https://docs.linuxserver.io/images/docker-bookstack/), [MediaWiki Search API](https://www.mediawiki.org/wiki/API:Search).
