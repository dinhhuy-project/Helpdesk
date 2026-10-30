# Frontend bước 15 — Nghiệm thu và chuẩn bị phát hành

Bước 15 bổ sung bộ browser acceptance chạy lại được ở local/CI, kiểm tra accessibility, phiên/cache, lỗi mạng và image Nginx/HTTPS. Không thay API nghiệp vụ, schema, grant hoặc dữ liệu Helpdesk đang dùng. Các bước frontend 1–15 đủ cho nghiệm thu MVP; **phát hành production vẫn cần hoàn tất các cổng vận hành của backend bước 18** bên dưới.

## Chạy nghiệm thu

Yêu cầu Node theo `fe/package.json`, .NET 8, Docker Desktop/Linux và frontend dependencies. Playwright và axe chỉ là devDependencies, không nằm trong image runtime hoặc browser bundle. Browser Chromium được cài riêng theo [hướng dẫn CI Playwright](https://playwright.dev/docs/ci).

```powershell
cd fe
npm ci
npx playwright install chromium
npm run lint
npm test
npm run build
cd ..

# Một lệnh tạo database/MinIO tạm, chạy cả 9 browser journeys rồi dọn tài nguyên test.
./be/tests/Run-FrontendAcceptanceTests.ps1

# Toàn bộ backend tests, gồm các typed API/SignalR journeys không bật browser.
./be/tests/Run-PersistenceTests.ps1

# Build và kiểm tra frontend image với chứng chỉ TLS tạm chỉ dành cho probe.
docker build -f fe/Dockerfile -t helpdesk-web:acceptance fe
node fe/scripts/verify-web-image.mjs
```

Runner tự build `dist`, mở **Vite preview** bằng port localhost ngẫu nhiên, gọi các C# journey fixtures qua PostgreSQL/MinIO test rồi dừng preview. Không dùng Vite development/HMR hoặc server đang chạy của người dùng. Không cần chạy Visual Studio API, migrate hay seed database đang dùng. Không dùng connection production để chạy test.

Trong CI đã có các biến `HELPDESK_TEST_*` trỏ tới service tạm, có thể chạy `npm run test:e2e -- --release` trong `fe/`. Nếu không có cấu hình DB/storage test, runner fail thay vì báo xanh do bỏ qua PostgreSQL tests. C# fixtures bắt buộc nhận receipt xác nhận browser đã chạy khi bật release acceptance.

Chạy lại một journey khi chẩn đoán, rồi bỏ lựa chọn để chạy bộ đầy đủ:

```powershell
$env:HELPDESK_ACCEPTANCE_JOURNEYS = 'Workflow'
# Tùy chọn dùng Edge đã có trên Windows thay Chromium:
$env:HELPDESK_BROWSER_CHANNEL = 'msedge'
./be/tests/Run-FrontendAcceptanceTests.ps1
Remove-Item Env:HELPDESK_ACCEPTANCE_JOURNEYS
Remove-Item Env:HELPDESK_BROWSER_CHANNEL
```

Trên Windows, image probe dùng OpenSSL đi cùng Git tại `C:/Program Files/Git/usr/bin/openssl.exe`; đặt `HELPDESK_OPENSSL` nếu khác. Linux dùng `openssl` trên PATH. Chỉ certificate/key trong thư mục OS temp do probe tự tạo được dọn khi kết thúc; không đụng certificate, image, container hoặc volume đang dùng của ứng dụng.

## Ma trận nghiệm thu

| Nhóm | Kiểm tra bằng app/API thật |
| --- | --- |
| Discussion | Requester tạo Ticket qua UI, trao đổi hai tài khoản; public/internal/access/activity tách biệt; không tải hoặc giữ internal trong requester cache. |
| Accounts | Quản lý user/role/grant/audit đúng policy, active/version, scope cấp role và bảo vệ admin cuối; phiên khi deactivate/đổi mật khẩu. |
| Catalog | Department/team/membership/category, version và leadership; dữ liệu dùng được trong form Ticket. |
| Workflow | Requester → điều phối có read.all/assign → trưởng nhóm đúng membership → Agent xử lý → Requester reopen/confirm/cancel. Hai browser Agent cùng sửa gây 409, giữ resolution draft và chủ động dùng version mới. |
| Attachment | Multipart sau Ticket/comment, file public/internal, ownership, Bearer download kiểm tra byte UTF-8, requester không nhận internal; không gọi MinIO trực tiếp. |
| SLA | Policy active/version, snapshot, public response, resolve, policy mới không thay deadline cũ; null không bị coi là đạt. |
| Notifications | List/detail/count/mark-read và mất quyền; REST vẫn chạy khi hub bị ngắt. |
| Realtime | JS SignalR 10/server 8 thật qua LongPolling, nhiều tài khoản/phiên, refresh rotation/JWT expiry, disconnect/reconnect/rewatch, mất membership/group, cleanup logout. |
| Reporting | Dashboard/team/agent/daily so cùng cohort/ACL/filter; nhóm chưa phân công, inactive, count 0/SLA null, phân trang server, ngày UTC tại timezone âm. |
| Release checks | Deep link → login → returnTo, memory-only reload → login, đổi sang Agent trên URL Ticket ngoài scope không giữ dữ liệu requester; storage không chứa token. |
| Lỗi mạng | Sau khi server commit POST create, chặn receipt trên browser: báo chưa xác định, khóa gửi, không tự retry, DB chỉ có một Ticket. GET lỗi mạng có error/retry rồi phục hồi. |
| Responsive/a11y | 390px không tràn ngang, bảng cuộn và dialog/focus/Escape/bàn phím; axe quét bốn ngữ cảnh workflow, login deep link, form create, mobile menu và trang ngoài scope. |

Các journey REST cũ chủ động ngắt hub để tránh nhận realtime làm đổi version giữa bài kiểm tra concurrency; journey realtime riêng bật hub thật. API requests được browser forward tới loopback application fixture, không mock dữ liệu nghiệp vụ. Kiểm tra WebSocket upgrade/TLS qua Nginx là probe riêng; chưa phải toàn bộ authenticated SignalR journey xuyên public reverse proxy.

Accessibility dùng [axe với Playwright](https://playwright.dev/docs/accessibility-testing), tags WCAG 2 A/AA, 2.1 AA, 2.2 AA. Kết quả tự động áp dụng cho những màn hình/trạng thái đã quét, không phải chứng nhận mọi trang đáp ứng WCAG. Trước phát hành cần rà soát thủ công screen reader, zoom 200%, bàn phím, dialog và file download trên browser mục tiêu. Hiện runner tự động dùng Chromium; có thể chạy Edge bằng biến môi trường. Chưa nghiệm thu Firefox/WebKit.

## Image frontend và reverse proxy

`fe/Dockerfile` build bằng Node 24 rồi chỉ copy `dist` vào Nginx 1.30 stable. `.dockerignore` loại `.env`, node_modules, artifacts test và file local. `VITE_API_BASE_URL` là build arg public, mặc định `/`. Bản production từ chối API origin HTTP để tránh mixed content; development vẫn cho phép localhost HTTP.

Nginx template tại `fe/deploy/default.conf.template`:

- HTTP 8080 chuyển 308 sang HTTPS; HTTPS 8443 dùng `/etc/nginx/tls/fullchain.pem` và `privkey.pem` được mount lúc chạy. Không có key hoặc certificate thật trong repository/image.
- SPA được deploy ở **root origin**, deep link trả `index.html`; file `/assets` không có phải 404, không trả HTML giả JS. HTML không cache; asset hash thành công có immutable cache. API/hub/health không fallback về SPA.
- `API_UPSTREAM` mặc định `http://api:8080`, thuộc network backend riêng. Giữ method/path/body/Bearer, chuyển tiếp proto/client IP, HTTP/1.1 Upgrade/Connection theo [NGINX WebSocket proxying](https://nginx.org/en/docs/http/websocket.html). Có timeout cho LongPolling, không buffer API/hub; giới hạn request 12 MiB để đủ multipart file 10 MiB.
- Access log chỉ method/path/status/timing, không query/header/body. Không ghi upstream error có thể chứa query token tại proxy location. Cần dùng status/timing và traceId backend cho quan sát lỗi.

Ví dụ cho operator sau khi chuẩn bị domain, certificate và network staging/production riêng:

```powershell
docker build -f fe/Dockerfile -t helpdesk-web:<release> fe
# Thay <network>, <tls-directory>, <release>; không dùng certificate probe cho production.
docker run -d --name helpdesk-web --restart unless-stopped --network <network> `
  -p 80:8080 -p 443:8443 -e API_UPSTREAM=http://api:8080 `
  --mount type=bind,source=<tls-directory>,target=/etc/nginx/tls,readonly `
  helpdesk-web:<release>
```

Lệnh trên là mẫu triển khai, chưa tự chạy hoặc publish. Root `docker-compose.yml` hiện dành cho development, API đặt `ASPNETCORE_ENVIRONMENT=Development`; không dùng nguyên cấu hình đó làm production.

Image probe chạy container riêng, port ngẫu nhiên, TLS xác minh bằng CA certificate tạm, HTTP redirect, SPA deep link/asset/no-store/404, API method/body/Bearer, WSS upgrade và log redaction. Upstream của probe là HTTP/WebSocket protocol fixture; security/ACL nghiệp vụ được kiểm tra riêng với API/PostgreSQL/MinIO thật trong các journeys.

## Kiểm tra trên URL đã deploy

Khi có URL HTTPS thực:

```powershell
cd fe
# Cùng origin:
npm run verify:deployment -- https://helpdesk.example.com
# Nếu API/hub khác origin, API phải là HTTPS origin không có path:
npm run verify:deployment -- https://helpdesk.example.com https://api.example.com
```

Probe chỉ đọc document/assets/health, thử challenge 401 và OPTIONS CORS, không đăng nhập hoặc tạo dữ liệu. TLS dùng trust store mặc định; từ chối `NODE_TLS_REJECT_UNAUTHORIZED=0`. Chứng chỉ private CA phải được trust hợp lệ, không tắt TLS verification. Cùng origin không cần CORS cho request browser; khác origin cần exact allowlist tại `Cors:AllowedOrigins`, Bearer không dùng credentials cookie. Probe kiểm tra origin hợp lệ và origin ngoài allowlist, API/hub không trả HTML/redirect thay 401.

**Chưa có hostname/certificate hoặc môi trường staging được chỉ định trong yêu cầu này, nên chưa chạy probe trên hạ tầng public thực, chưa publish image hoặc chạy GitHub workflow từ xa.** Cần thử login/upload/download/SignalR WebSocket với tài khoản staging sau khi probe này đạt; probe không thay E2E có phiên trên hạ tầng thật.

## Phối hợp backend bước 18 trước production

| Cổng vận hành | Việc còn phải xác minh trên môi trường đích |
| --- | --- |
| Proxy/HTTPS | Backend phải xử lý ForwardedHeaders **chỉ từ proxy/network tin cậy**, trước HTTPS redirection/rate-limit; source hiện chưa có middleware này. Kiểm tra không redirect loop, IP người dùng đúng, không tin header giả từ kết nối ngoài. Chốt HSTS/certificate renewal. |
| Cấu hình | Production environment, signing key/DB/storage secrets qua secret store; tắt development seed/admin mẫu, không expose Swagger hoặc database/MinIO console ra public. Chốt origin API/hub và CORS thực. |
| Readiness | `/health/live` hiện chỉ chứng minh process sống. Cần readiness theo DB/migration/storage và chiến lược worker; không coi live=ready để chuyển traffic. |
| Dữ liệu | Review/apply migration bằng quy trình riêng, backup PostgreSQL và object storage, thử restore có evidence; chốt retention phiên/audit/attachment cleanup, không xóa lịch sử để “dọn” trước release. |
| Realtime | Hiện một API instance; chưa có backplane/outbox đa instance. Kiểm tra auth/group/reconnect thực qua proxy, timeout và không log query token. Không scale ngang trước khi có thiết kế tương ứng. |
| Vận hành | Theo dõi 401/403/409/429/5xx, traceId, worker/SLA/cleanup/notification retry; log redaction ở mọi hop. Lưu image digest, commit và cấu hình public đã build. |
| Rollout/rollback | Deploy staging trước, giữ asset hash cũ trong khoảng chuyển phiên, không cache index, có rollback image/config và kế hoạch tương thích migration. Chạy smoke + E2E staging; reload hiện yêu cầu login lại vì token chỉ trong memory. |

Các cổng này thuộc [backend roadmap — bước 18](backend-roadmap.md), không được đánh dấu hoàn thành chỉ vì frontend/CI đã đạt.

Tham chiếu vận hành: [ASP.NET Core trusted proxy và thứ tự middleware](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0), [readiness/liveness](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-8.0), [bản stable Nginx](https://nginx.org/en/download.html). Operator cần giữ base image và runtime được vá, lưu digest đã nghiệm thu trước rollout.

## Kết quả nghiệm thu

Kết quả local ngày 30/10/2026:

| Kiểm tra | Kết quả |
| --- | --- |
| Frontend unit/component | 478/478 tests, 33 files |
| ESLint và build/TypeScript | Đạt |
| Backend đầy đủ + CLI seed/grant smoke | 545/545 tests, 0 skipped; CLI đạt trên DB tạm |
| Backend Release `-warnaserror` | 0 warning, 0 error |
| Browser acceptance production bundle | 9/9 journeys đạt trên Chromium; bộ 9/9 cũng đạt trên Edge |
| Axe | 8 ngữ cảnh, 0 violation trong các tags đã chọn |
| Image frontend | Build Node 24/Nginx 1.30 đạt; probe trusted TLS/redirect/SPA/assets/API/Bearer/WebSocket/log redaction đạt |
| Deployment probe | Đạt qua image/protocol fixture với CA tạm; chưa chạy trên URL public/staging thực |
| CI | YAML parse và local commands đạt; chưa chạy workflow trên GitHub từ xa |

Trong quá trình nghiệm thu đã sửa selector searchbox và đồng bộ browser assertion với KPI mới sau refetch để tránh đọc bảng cũ trong lúc router chuyển bộ lọc. Không tự retry bài kiểm tra để che lỗi; bộ Chromium đầy đủ được chạy lại và đạt sau sửa.

Artifact local nằm dưới `fe/test-results/acceptance` (gitignored); gồm TRX, screenshot dữ liệu fixture và JSON axe chỉ rule/impact/count. Không lưu HAR/trace/DOM/raw headers hoặc credential. CI giữ artifact browser một ngày; giữ credential fixture ở stdin, không dùng secrets/dữ liệu Helpdesk thật. Các container/volume test riêng và preview do runner tạo đã được dọn khi kết thúc; không thay đổi `.env` hoặc database đang dùng.
