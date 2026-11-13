# Demo Helpdesk qua Internet bằng máy tính cá nhân

Phù hợp khi bảo vệ đồ án/thử nghiệm, **chưa có tên miền**. Máy Windows chạy Docker Desktop; Cloudflare Quick Tunnel cấp địa chỉ HTTPS tạm dạng `https://...trycloudflare.com`. Không cần thuê VPS, mở port router hoặc đưa Visual Studio/Vite ra Internet. Máy phải bật, Docker chạy và có Internet trong buổi demo.

Đây là bản **demo public**, không phải hoàn thành toàn bộ yêu cầu production bước 18. Quick Tunnel không có cam kết uptime, đổi hostname khi tạo lại, tối đa 200 request đồng thời và không hỗ trợ SSE. SignalR của project có thể dùng WebSocket; trợ lý hiện không stream SSE. Xem [tài liệu Cloudflare](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/).

## Chỉ một lệnh để deploy từ đầu

Bật Docker Desktop, chờ Linux engine sẵn sàng; tại root repository chạy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Deploy-SelfHostDemo.ps1
```

Đây là lệnh dùng cho cả lần đầu và những lần chạy sau, kể cả khi containers/images/volumes đã bị xóa. Không cần tự chạy lần lượt initializer và Start. Script tự tạo secrets demo còn thiếu, build API/frontend/MinIO khi thiếu image, bật hạ tầng, tạo schema + admin nếu database demo **trống**, khởi động ứng dụng rồi mở tunnel HTTPS. URL chỉ in sau khi kiểm tra thành công và nằm ở **dòng cuối cùng**:

```text
[1/5] Preparing dedicated demo configuration...
...
DEPLOY READY
Administrator credentials (private): ...\credentials.txt
Keep Docker Desktop running and the computer awake during the demo.
Stop: powershell ... -File .\Start-SelfHostDemo.ps1 -Stop

URL: https://example.trycloudflare.com
```

Lần đầu hoặc sau khi xóa images có thể mất vài phút để tải/build. Log chi tiết từng bước nằm trong `.local/self-host-demo/deploy-*.log`, không tràn terminal hoặc in password. Tạo SQL lần đầu cần .NET SDK/runtime 8 và `dotnet-ef` 8.x như phần phần mềm của project. Không cần chạy Visual Studio hoặc Vite.

Việc tự khởi tạo schema ở lệnh này được giới hạn cho database demo riêng `helpdesk_demo` đang trống; kiểm tra lại trước khi áp dụng SQL. Đây là luồng khởi tạo demo mới đã được yêu cầu, không tự nâng schema database có dữ liệu. Nếu database đã có bảng, script giữ dữ liệu/secrets, chạy kiểm tra hiện tại và vẫn yêu cầu review/apply riêng khi có migration mới hoặc schema bất thường. Không tạo lại dữ liệu nghiệp vụ đã mất cùng volume; phục hồi từ backup nếu cần.

Nếu agent auto deploy đang chạy, script yêu cầu agent tạm dừng an toàn rồi bật lại sau khi xong, kể cả khi một bước lỗi. Agent chưa được cài thì không bắt buộc cài chỉ để deploy; muốn tự cập nhật sau push, xem [hướng dẫn auto deploy](self-host-auto-deploy.md).

Chỉ muốn khởi tạo/chạy preview trên máy, không mở Internet: dùng cùng lệnh với `-Local`. Khi không có tunnel cũ, lựa chọn này không tạo tunnel; nếu tunnel đang chạy từ trước thì vẫn cần lệnh Stop để đóng nó. URL preview cũng nằm ở dòng cuối. Các lệnh initializer/Start dưới đây vẫn giữ để review migration, bảo trì và chẩn đoán riêng.

Đã kiểm tra ngày 03/11/2026: lệnh mới build image còn thiếu, khởi tạo database demo trống và mở URL public thành công; chạy lại giữ nguyên hash file secrets/credentials, không khởi tạo lại schema. Đã xác nhận URL là dòng cuối, tiến trình deploy trả quyền điều khiển về terminal, agent nền được tiếp tục, probe login/me/logout và SignalR WebSocket qua URL thật pass. Bộ kiểm thử PowerShell hiện có 17 trường hợp auto deploy và 4 chẩn đoán startup đều pass. Những kiểm tra này không xóa volume chứa dữ liệu để tạo fixture.

## Bố trí

```text
Người xem → HTTPS trycloudflare.com → cloudflared → Nginx frontend
                                                     ├── SPA React đã build
                                                     └── /api, /hubs → API Production
                                                                          ├── PostgreSQL demo
                                                                          └── MinIO demo private
```

- Project Compose cố định `helpdesk-demo`, file `docker-compose.demo.yml` độc lập với stack phát triển. Database `helpdesk_demo`, volumes `helpdesk-demo_demo-postgres` và `helpdesk-demo_demo-minio` riêng. Không copy tài khoản, JWT, nội dung Ticket hoặc file thật từ Development.
- Preview tại **`http://127.0.0.1:8090`**, chỉ loopback trên máy chủ. Internet đi qua tunnel; không công khai 5000, 5173, PostgreSQL, MinIO Console hoặc Ollama. Frontend và API chung origin, không thêm wildcard CORS.
- API chạy `Production`, startup seeds Development và Swagger tắt. Tài khoản quản trị demo được tạo bằng CLI riêng, mật khẩu ngẫu nhiên; không lấy mật khẩu admin Development.
- Nginx chỉ tin `CF-Connecting-IP` từ đúng IP cloudflared. API chỉ nhận forwarded scheme/IP từ IP Nginx trong network riêng, trước HTTPS redirect và rate limit. Không bật chế độ tin tất cả proxy. Các header forwarding do người dùng tự gửi được Nginx thay thế.
- Network demo dùng `172.29.240.0/24`; IP tĩnh `.2` cho Nginx và `.4` cho tunnel, IP động nằm trong `.128/25`. Nếu máy/VPN đã dùng subnet này, cần đổi đồng bộ Compose, `ReverseProxy__KnownProxies__0` và template Nginx rồi rebuild; không gắn vào network của stack khác.
- Secrets/admin credentials chỉ nằm trong **`.local/self-host-demo/`**, bị Git/Docker build loại trừ và ACL chỉ cho user Windows đang chạy setup. Docker administrator trên máy vẫn có thể inspect environment; không chia sẻ output `docker compose config`, `docker inspect`, file `.env` hoặc credentials.

## Khởi tạo lần đầu

Chạy tại **root repository** bằng PowerShell, sau khi Docker Desktop Linux engine sẵn sàng. Cần .NET SDK/runtime 8 và `dotnet-ef` 8.x trên host để tạo SQL; các image frontend/backend được build bằng Docker, không yêu cầu chạy Vite hay Visual Studio.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Initialize-SelfHostDemo.ps1
```

Script sinh secrets riêng nếu chưa có, build `api/web/minio`, tạo SQL idempotent từ migrations hiện tại tại **`.local/self-host-demo/migrations.sql`**, ghi checksum/danh sách migration. Không copy `.env` Development, không chạy migration hay seed chỉ vì build. Lần đầu tải image/build cần thời gian và dung lượng Docker.

Mở SQL để review, xác nhận đích `helpdesk-demo/helpdesk_demo`, rồi chạy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Initialize-SelfHostDemo.ps1 -ApplyReviewedMigrations
```

Lệnh này chỉ áp dụng SQL đã chuẩn bị vào database demo cố định. Checksum/danh sách migrations phải khớp; dừng API/web demo và từ chối nếu tunnel đang chạy. SQL/migration lỗi sẽ dừng, không mở Internet. CLI seed Identity và bootstrap admin chạy lặp an toàn, không reset admin đã tồn tại. Không sinh lại secrets khi rerun.

Đọc tài khoản quản trị tại **`.local/self-host-demo/credentials.txt`** trên máy cá nhân; không đưa file lên Git hoặc chia sẻ cho người xem. Tạo tài khoản Requester/Agent có phạm vi phù hợp để người khác trải nghiệm. Database mới ban đầu chỉ có catalog Identity và admin, chưa có bộ Ticket của stack Development. Nếu cần nạp bộ dữ liệu một tuần, dùng cơ chế seed riêng đã có và cấu hình đúng kết nối demo; không chạy lệnh seed dành cho Development rồi kỳ vọng dữ liệu xuất hiện tại đây.

## Chạy mỗi buổi demo

Kiểm tra local trước:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-SelfHostDemo.ps1
```

Mở `http://127.0.0.1:8090`, đăng nhập bằng admin demo, chuẩn bị tài khoản/nội dung phù hợp. Lệnh kiểm tra DB/migration/index, bật PostgreSQL/MinIO/API/frontend và chờ web healthy. Không auto-build hoặc auto-migrate trong lệnh chạy hằng ngày. Không cần F5 và không cần `npm run dev` cho bản deploy.

Nếu dùng riêng lệnh Start sau khi volume PostgreSQL demo bị xóa/tạo lại, container healthy vẫn có thể chứa database trống và Start sẽ báo `helpdesk_demo is empty`. Chạy `Deploy-SelfHostDemo.ps1` để tự chuẩn bị/khởi tạo database demo trống trong một lệnh, hoặc dùng quy trình SQL review/apply thủ công ở trên. Việc này không khôi phục Ticket/file/tài khoản đã mất cùng volumes; muốn lấy lại dữ liệu nghiệp vụ phải restore backup. Khi có bảng nhưng thiếu history, dừng để rà soát schema thay vì tự reset. Nếu schema có history nhưng preflight vẫn lỗi, đọc log riêng `.local/self-host-demo/startup-preflight.log`, không chia sẻ nguyên log khi chưa kiểm tra secrets.

Khi sẵn sàng chia sẻ:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-SelfHostDemo.ps1 -Public
```

Lệnh bật tunnel và in **Internet DEMO URL**, lưu tại `.local/self-host-demo/public-url.txt`, kiểm tra HTTPS `/health/live` trước khi báo sẵn sàng. Nếu kiểm tra URL thất bại, tunnel được dừng. Quick Tunnel không yêu cầu account/token Cloudflare. Có URL nghĩa là người khác có thể mở các trang public; URL không thay thế authentication/phân quyền Helpdesk.

Hostname mới có thể chưa xuất hiện ngay trong DNS. Script chờ và retry có giới hạn; nếu Windows lưu kết quả NXDOMAIN ban đầu, script thử làm mới DNS cache tạm **một lần**, không đổi DNS server/router. Nếu vẫn lỗi `NameResolutionFailure`, kiểm tra mạng/resolver, chờ rồi thử lại; không tắt xác minh TLS để né lỗi. Khi tunnel được tạo lại, chỉ dùng URL mới nhất do lệnh in ra.

API, hub và frontend dùng cùng URL, nên không cần sửa `VITE_API_BASE_URL` mỗi lần hostname đổi. Gửi URL cho người xem, thử bằng điện thoại dùng 4G/5G để xác nhận truy cập từ bên ngoài. Không gửi credentials quản trị; chỉ chia sẻ tài khoản demo đã giới hạn quyền. Không đưa thông tin cá nhân, Ticket thật hoặc hướng dẫn nội bộ chưa được duyệt vào database public.

**Kết thúc buổi demo:**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-SelfHostDemo.ps1 -Stop
```

Đóng tunnel và dừng toàn bộ demo, giữ volumes/data. Development/VS container không bị dừng. Chạy Start lại để tiếp tục; URL có thể thay đổi. Chạy Start không có `-Public` không tự đóng tunnel đã mở trước đó: dùng `-Stop` nếu muốn chấm dứt truy cập Internet.

## AI local và Wiki

AI mặc định tắt trong bản demo để không giả định Ollama đang chạy. Nếu muốn dùng model đã setup trên cùng máy:

1. Bật runtime local hiện có: `docker compose -p helpdesk-ai -f docker-compose.ai.yml up -d --wait ollama`.
2. Kiểm tra model đã tải: `docker compose -p helpdesk-ai -f docker-compose.ai.yml exec -T ollama ollama show qwen3:1.7b`.
3. Sửa **`.local/self-host-demo/.env`**: `DEMO_AI_ENABLED=true`, model qua `DEMO_AI_MODEL`. Chạy lại Start; API container sẽ được recreate để nạp cấu hình. Không mở port Ollama ra Internet, không share context/wiki riêng cho AI.

Demo API dùng `http://host.docker.internal:11434/`, timeout 120 giây; Nginx chờ 150 giây. CPU có thể vẫn chậm/timeout. Chỉ tài liệu Knowledge Operational đã được rà soát và publish trong **database demo** mới là nguồn cho trợ lý. Thiếu nguồn thì báo chưa đủ thông tin. Model local miễn phí, nhưng người xem public vẫn có thể tiêu thụ CPU; API có rate limit và một inference đang chạy/process. Không coi đây là cơ chế chống DDoS đầy đủ.

BookStack connector tắt mặc định để không tự đưa Wiki Development vào bản public. Wikipedia vẫn là panel tham khảo riêng. Muốn đưa wiki riêng vào bản deploy cần chốt quyền nguồn, endpoint và phạm vi export trước; không tunnel MinIO Console, Wiki admin hay Docker socket.

## Cập nhật và backup

Muốn **tự cập nhật sau mỗi push lên develop**, dùng [agent auto deploy trên máy cá nhân](self-host-auto-deploy.md). Agent đợi CI pass, build riêng, giữ tunnel/data/secrets và chặn migration mới để review. Dừng agent trước khi thực hiện quy trình cập nhật thủ công bên dưới.

Khi đổi source:

1. Dừng demo/tunnel bằng `-Stop`.
2. Chạy initializer không switch để rebuild và chuẩn bị SQL.
3. Backup trước nâng schema; review SQL, chạy `-ApplyReviewedMigrations`.
4. Start local, kiểm thử rồi `-Public`.

Không dùng `down -v` hoặc Docker Desktop Delete volumes khi muốn giữ dữ liệu. Giữ `.local/self-host-demo/.env` để dùng lại credentials của volumes đã có; mất file này cần khôi phục backup secrets, không tự đổi password trong `.env` để “sửa” database đang tồn tại.

Ví dụ backup database demo, không redirect file nhị phân qua PowerShell:

```powershell
docker compose -p helpdesk-demo --env-file .local/self-host-demo/.env -f docker-compose.demo.yml exec -T postgres pg_dump -U helpdesk_demo -d helpdesk_demo -Fc -f /tmp/helpdesk-demo.dump
if ($LASTEXITCODE -ne 0) { throw 'Demo backup failed.' }
docker compose -p helpdesk-demo --env-file .local/self-host-demo/.env -f docker-compose.demo.yml cp postgres:/tmp/helpdesk-demo.dump .local/self-host-demo/helpdesk-demo.dump
```

Dump này không bao gồm file đính kèm: cần backup volume MinIO riêng và thử restore. Đặt backup ngoài ổ/máy đang chạy để tránh mất cả dữ liệu lẫn bản sao khi máy hỏng.

## Kiểm tra và giới hạn vận hành

```powershell
# Có node_modules frontend, Node theo package.json. Không in tokens/password.
node fe/scripts/verify-self-host-demo.mjs http://127.0.0.1:8090 --login

# HTTPS public, chỉ probe anonymous; không gửi credentials nếu URL không phải demo của bạn.
$demoUrl = Get-Content .local/self-host-demo/public-url.txt -Raw
node fe/scripts/verify-self-host-demo.mjs $demoUrl
```

Probe kiểm tra SPA deep links, assets, `.env` không lộ, API không fallback HTML, challenge 401, public read, WebSocket SignalR thật; `--login` đọc admin credentials local và kiểm tra login/me/logout. Không dùng probe đăng nhập vào URL bên thứ ba.

Windows cần bật **Docker Desktop khi đăng nhập**, có đủ RAM/disk, tắt sleep tự động trong khoảng demo, giữ mạng ổn định và không đóng nắp laptop nếu việc đó khiến máy sleep. Script không tự thay power plan/firewall/router, không tạo startup task Windows. Không cần expose DB/API ports hoặc cho người khác quyền điều khiển Docker Desktop. Với bản chạy dài hạn nên chuyển sang máy server/VPS hoặc named tunnel có domain, backup/restore, readiness sâu, giám sát và quy trình release đầy đủ.

Backend hiện target .NET 8; Microsoft công bố hết hỗ trợ **10/11/2026**. Cần kế hoạch nâng runtime/packages lên bản còn hỗ trợ trước khi vận hành dài hạn; thay đổi đó là task riêng, không tự nâng framework trong bước demo. [Chính sách Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

## Kết quả nghiệm thu ngày 03/11/2026

- Build Release warn-as-error: 0 warning/error; 15 tests proxy/API foundation pass. Có negative tests cho proxy lạ, forwarding bị tắt, địa chỉ trust không hợp lệ và thứ tự trước HTTPS redirect.
- Build được image API, web demo và web HTTPS mặc định. Probe web HTTPS mặc định pass cho TLS trusted, redirect, SPA/assets/API/Bearer/WebSocket và log không chứa token. Fixture probe được cập nhật theo contract public list hiện tại.
- Probe local và **URL HTTPS public thật** pass: SPA, API routing, challenge 401, login/me/logout, anonymous knowledge và SignalR JS WebSocket thật. Probe deploy hiện có cũng pass cho HTTPS/deep links/public lists/hub negotiation/CORS.
- Đã thử stop/start stack giữ dữ liệu, cấp URL mới; gặp và xử lý negative DNS cache khi hostname đổi. Database demo có 11 migrations và 1 admin riêng. Không sao chép database Development ra public.
- ESLint pass; Compose demo parse/validation pass. CI bổ sung kiểm tra Compose bằng giá trị giả, không mở tunnel Internet trong CI. Chưa chạy lại toàn bộ unit/E2E backend/frontend hay workload tải dài hạn; chưa kiểm tra từ điện thoại 4G và chưa có SLA vận hành/restore drill cho deployment này.

Tham khảo xử lý proxy: [ASP.NET Core forwarded headers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).
