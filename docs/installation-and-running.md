# Cài đặt, khởi tạo và chạy Helpdesk

Tài liệu thực hành cho **Windows + PowerShell + Docker Desktop**, theo source hiện tại ngày 31/10/2026. Cách phát triển chính: PostgreSQL/Redis/MinIO bằng Compose, backend bằng Visual Studio **Container (Dockerfile)**, frontend bằng Vite. BookStack là stack tùy chọn chạy riêng.

Tất cả lệnh mặc định chạy ở **root repository**, nơi có `docker-compose.yml`; những khối chuyển sang `fe/` sẽ ghi rõ. Không chép nội dung `.env`, user-secrets hoặc file credentials vào tài liệu, Git hay log chia sẻ.

## Chạy hằng ngày với Visual Studio

Sau khi đã cấu hình `.env`, cài dependencies frontend (`npm --prefix fe ci`) và review/apply các migration, chỉ cần:

1. Bật **Docker Desktop**, chờ Linux engine sẵn sàng. Tại root repository chạy một lệnh:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-Development.ps1
   ```

2. Terminal khác tại root chạy frontend bằng một lệnh:

   ```powershell
   npm --prefix fe run dev
   ```

3. Visual Studio mở `be/be.sln`, chọn project `be` làm startup project, chọn **Container (Dockerfile)** rồi **F5**.

Mở frontend `http://localhost:5173`; API/Swagger ở `http://localhost:5000/swagger`. Vite proxy API/SignalR về cổng 5000 theo cấu hình mặc định. Sau khi thay user-secrets bằng script, restart phiên Debug đang chạy để backend nạp lại cấu hình.

`Start-Development.ps1` thực hiện:

- Kiểm tra Docker Linux engine, `.env` và các công cụ CLI.
- Khởi động PostgreSQL/Redis/MinIO và chờ healthy. Tạo app credentials MinIO còn thiếu bằng initializer sẵn có; giữ credentials đã tồn tại.
- Bật stack Wiki nếu `BOOKSTACK_CONNECTOR_ENABLED=true`, bật Ollama nếu `LOCAL_ASSISTANT_ENABLED=true`; giữ đúng project/volumes riêng. Model chỉ được tải nếu chưa có, không pull lại tag đã tồn tại mỗi lần chạy. Lần đầu tải có thể lâu và cần Internet/disk/RAM.
- Đồng bộ connection/port PostgreSQL, app MinIO, Wiki và AI từ Compose đã resolve sang backend user-secrets. JWT/admin được initializer bổ sung nếu thiếu và giữ nguyên nếu đã có. Không in secrets ra terminal.
- Kiểm tra migration history khớp source và báo rõ nếu thiếu/sai. Không tự migrate, seed dữ liệu nghiệp vụ, provision Wiki hay publish tài liệu. F5 vẫn chạy các startup seed Development lặp an toàn đã cấu hình.
- Nếu API **Compose** đang chạy thì dừng đúng service `api` để nhường cổng cho Visual Studio. Không dừng container Debug của Visual Studio hay frontend đang chạy.

Root `.env` là nguồn cấu hình tích hợp cho lệnh này; environment của terminal vẫn có thể override giá trị theo quy tắc Compose. Không sửa riêng connection/app key trong user-secrets rồi kỳ vọng giữ giá trị đó sau khi chạy script đồng bộ. Profile Dockerfile đã có host overrides `host.docker.internal` cho PostgreSQL/MinIO/Ollama; giữ port API 5000.

Nếu mới clone/máy mới, thực hiện phần khởi tạo bên dưới một lần. Nếu xóa volumes, cần review/apply migration lại và khôi phục dữ liệu phù hợp; Wiki mất volume cần provision/restore theo hướng dẫn riêng. Script khởi động không thay thế backup hoặc tự đưa dữ liệu demo thành nguồn AI thực tế. Nếu không bật Wiki/AI trong `.env`, script bỏ qua các stack đó và đồng bộ trạng thái tắt sang backend.

Đã kiểm tra ngày 02/11/2026: script chạy hai lần thành công trên các stack đang có; lần lặp giữ nguyên giá trị user-secrets và không tải lại model đã có. Backend CLI đọc được PostgreSQL bằng cấu hình vừa đồng bộ; số dữ liệu nghiệp vụ giữ nguyên. Lệnh frontend trả HTTP 200 tại 5173. Container Visual Studio kết nối được tới các cổng PostgreSQL/MinIO/BookStack/Ollama qua `host.docker.internal`. Có kiểm tra thông báo lỗi khi Docker chưa sẵn sàng và khi Compose JSON không hợp lệ, không in dữ liệu cấu hình. Chưa thao tác nút F5 trực tiếp trong IDE; phép thử Vite đã được dừng để nhường cổng cho lần chạy của bạn.

## 1. Phần mềm cần cài

| Công cụ | Yêu cầu của project |
| --- | --- |
| Git | Để lấy source và cập nhật code. |
| Docker Desktop | Đang chạy **Linux containers**, có Docker Compose v2.24 trở lên. |
| .NET SDK | SDK **8.x**; backend target `net8.0`, EF Core/Npgsql dùng 8.x. Cài SDK mới hơn không thay thế việc có runtime .NET 8 khi chạy app/test trên host. |
| Node.js và npm | Dùng **Node 24** theo `.nvmrc` và CI. `package.json` cũng cho phép Node 22 từ 22.12 hoặc 26+. |
| Visual Studio | Có workload ASP.NET/web, hỗ trợ .NET 8 và Container Tools để dùng profile Dockerfile. Không bắt buộc nếu chạy API bằng CLI/Compose. |

Kiểm tra terminal nhận công cụ và Docker engine đang hoạt động:

```powershell
git --version
docker version
docker compose version
dotnet --list-sdks
dotnet --list-runtimes
node --version
npm --version
```

Mở source đã clone và di chuyển vào thư mục gốc. Ví dụ trên máy hiện tại:

```powershell
Set-Location E:\Web\Helpdesk
```

Nếu PowerShell chặn script đã kiểm tra và tin cậy, có thể mở terminal riêng với `powershell.exe -NoProfile -ExecutionPolicy Bypass`; không cần đổi policy toàn máy. Không chạy lại các script khởi tạo khi chỉ muốn xem cấu hình.

## 2. Bản đồ cấu hình và cổng

| Thành phần | Cấu hình được đọc |
| --- | --- |
| Docker Compose | `.env` ở root để nội suy environment/port/credentials. |
| Backend chạy trực tiếp hoặc VS Dockerfile | `appsettings*.json`, **user-secrets khi Development**, environment và profile. Không tự đọc `.env` root. VS gắn user-secrets vào container debug. |
| Frontend Vite | `fe/.env.local` và `fe/.env.example`; `API_PROXY_TARGET` điều khiển proxy dev. |
| Frontend build | `VITE_*` được đưa vào bundle và là dữ liệu public; không chứa password/JWT signing key/token wiki. |

Các địa chỉ dưới đây dùng cổng mặc định:

| Dịch vụ | Trình duyệt/máy host | Địa chỉ backend dùng trong mạng Compose |
| --- | --- | --- |
| Frontend Vite | `http://localhost:5173` | Không có service frontend trong Compose root. |
| API VS Dockerfile hoặc Compose | `http://localhost:5000` | Service Compose: `http://api:8080`. |
| API profile `http` chạy trên host | `http://localhost:5158` | Không dùng service name `api`. |
| PostgreSQL | `localhost:5432` | `postgres:5432`. |
| Redis | `localhost:6379` | `redis:6379`. |
| MinIO API / Console | `http://localhost:9000` / `http://localhost:9001` | `minio:9000`. |
| BookStack tùy chọn | `http://localhost:6875` | VS/API Compose kết nối qua `host.docker.internal:6875`. |

`localhost` bên trong container là chính container đó. VS profile hiện có `Database__HostOverride` và `Minio__HostOverride=host.docker.internal`. Nếu đổi cổng trong `.env`, đồng bộ lại user-secrets/proxy liên quan; profile VS Dockerfile vẫn cố định cổng API 5000.

## 3. Khởi tạo lần đầu

Thực hiện lần lượt 3.1 → 3.6 trước khi chạy ứng dụng. Nếu máy hiện tại đã cấu hình/migrate, chuyển tới [chạy hằng ngày](#5-chạy-hằng-ngày).

### 3.1. Tạo `.env` và restore dependencies

Chỉ copy khi file chưa có, để không thay credentials đang sử dụng:

```powershell
if (-not (Test-Path -LiteralPath .env)) {
    Copy-Item .env.example .env
}
dotnet tool restore
dotnet restore be/be.sln
Push-Location fe
try { npm ci }
finally { Pop-Location }
```

Tool manifest `.config/dotnet-tools.json` pin `dotnet-ef` 8.0.32; không cần cài global EF major khác. Dùng `npm ci` theo lockfile.

Mở `.env` bằng editor, điền giá trị ngẫu nhiên riêng cho:

- `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `MINIO_ROOT_PASSWORD`.
- Nên dùng chuỗi hex/chữ-số dài để dễ dùng với Compose/connection string hiện có; MinIO root password tối thiểu 8 ký tự.
- Giữ các cổng mặc định nếu chưa có lý do đổi. `MINIO_APP_*` sẽ được script sinh ở bước sau.
- `JWT_SIGNING_KEY` chỉ bắt buộc khi chọn API Compose; VS dùng key trong user-secrets ở bước 3.3.

Nếu đã có volume PostgreSQL, đổi `POSTGRES_PASSWORD` trong file không tự đổi password trong database. Không đổi credentials hoặc xóa volume để thử sửa lỗi kết nối.

### 3.2. Khởi tạo MinIO và chạy hạ tầng

```powershell
.\be\scripts\Initialize-ComposeStorage.ps1
docker compose config --quiet
docker compose up -d --build --wait postgres redis minio
docker compose ps
```

Script sinh app credentials MinIO còn thiếu, bật `MINIO_ENABLED` và khởi động MinIO. Startup của MinIO tạo/kiểm tra bucket private `helpdesk-attachments`, app user và policy; healthcheck chỉ đạt sau bootstrap. Lệnh `up` tiếp theo khởi động đủ ba dịch vụ, giữ dữ liệu/credentials đã có.

Build MinIO/client lần đầu cần Internet và có thể mất vài phút. Không dùng MinIO root làm tài khoản truy cập file của API. Redis hiện là dịch vụ hạ tầng dự phòng, chưa có tích hợp cache nghiệp vụ trong backend.

### 3.3. Cấu hình backend Development

```powershell
.\be\scripts\Initialize-DevelopmentSecrets.ps1
.\be\scripts\Initialize-DevelopmentStorage.ps1 -UseComposeConfiguration
```

Script thứ nhất tạo JWT key và cấu hình admin ban đầu nếu chưa có, không reset giá trị cũ. Script thứ hai đồng bộ MinIO từ `.env` sang user-secrets; chạy lại khi cấu hình storage thay đổi.

**Connection string PostgreSQL cần cấu hình riêng**; hai script trên không tự thêm connection string. Khối dưới lấy cấu hình Compose trong bộ nhớ rồi lưu vào user-secrets, không in password. Nó đặt connection cho backend chạy trên **host**; VS profile sẽ thay riêng host bằng `host.docker.internal`:

```powershell
$composeLines = @(docker compose config --format json)
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve Compose configuration.' }
$composeConfig = ($composeLines -join "`n") | ConvertFrom-Json
$pgSettings = $composeConfig.services.postgres.environment
$pgBinding = @($composeConfig.services.postgres.ports | Where-Object { $_.target -eq 5432 })
if ($pgBinding.Count -ne 1) { throw 'Expected one PostgreSQL host port.' }
function Quote-PgValue([string]$Value) { return "'" + $Value.Replace("'", "''") + "'" }
$connection = "Host=localhost;Port=$($pgBinding[0].published);Database=$(Quote-PgValue $pgSettings.POSTGRES_DB);Username=$(Quote-PgValue $pgSettings.POSTGRES_USER);Password=$(Quote-PgValue $pgSettings.POSTGRES_PASSWORD);Include Error Detail=false"
@{ 'ConnectionStrings:Helpdesk' = $connection } |
    ConvertTo-Json -Compress |
    dotnet user-secrets set --project be | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot save database user-secrets.' }
Remove-Variable composeLines, composeConfig, pgSettings, pgBinding, connection
```

Không chạy riêng `docker compose config` hoặc `dotnet user-secrets list` rồi đưa output lên chat/log chung: có thể in toàn bộ secrets. Để xem admin trên máy cá nhân, mở **Visual Studio → be → Manage User Secrets**, đọc `BootstrapAdmin:Email` và `BootstrapAdmin:Password`; email mặc định của script là `admin@helpdesk.local`.

### 3.4. Review và áp dụng migration

Các CLI backend/EF tiếp theo dùng Development để đọc user-secrets. Chạy trong cùng terminal:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet build be/be.sln --no-restore
dotnet ef migrations list --project be --startup-project be
New-Item -ItemType Directory -Path .local/migrations -Force | Out-Null
dotnet ef migrations script 0 --idempotent --project be --startup-project be --output .local/migrations/Helpdesk.sql
```

Mở `.local/migrations/Helpdesk.sql`, kiểm tra database đích và SQL trước khi áp dụng. Với database đang có dữ liệu, backup trước theo [mục 9](#9-dừng-dịch-vụ-backup-và-dựng-lại). Sau khi đã review:

```powershell
dotnet ef database update --project be --startup-project be
dotnet ef migrations list --project be --startup-project be
```

EF áp dụng các migration chưa có theo đúng thứ tự. Source hiện có 11 migration, mới nhất `20261031163221_KnowledgeRetrieval`; xem [review chuẩn hóa tài liệu](knowledge-standardization-review.md) và [review truy xuất tài liệu](knowledge-retrieval-review.md). Tích hợp BookStack/Wikipedia trước đó không thêm migration. API/F5 và lệnh `docker compose up` **không tự migrate**.

Nếu dùng SQL đã review để nâng cấp thay cho EF CLI, kiểm tra migration history sau khi chạy; không sửa migration đã apply. Chi tiết: [PublicCommunity review](public-community-review.md), [persistence](backend-persistence.md).

### 3.5. Seed role/permission và admin đầu tiên

Sau migration, vẫn dùng terminal Development ở bước 3.4:

```powershell
dotnet run --project be --no-launch-profile -- seed-identity
dotnet run --project be --no-launch-profile -- bootstrap-admin
```

`seed-identity` bổ sung catalog còn thiếu và giữ grant của role đã tồn tại. `bootstrap-admin` tạo admin đầu tiên từ user-secrets; đã có admin thì không tạo/reset tài khoản. Nếu email bootstrap thuộc một tài khoản khác, lệnh dừng để tránh chiếm tài khoản.

Profile VS Container cũng tự seed Identity/bootstrap admin khi F5, nên việc chạy CLI trước giúp chuẩn bị và kiểm tra độc lập; chạy lặp không tạo trùng. Với role mặc định được tạo mới, grant được lấy từ catalog mới nhất. Database nâng cấp từ code cũ cần xem [mục 7](#7-cập-nhật-code-migration-và-quyền-trên-database-cũ) nếu thiếu quyền mới.

### 3.6. Cấu hình frontend

```powershell
if (-not (Test-Path -LiteralPath fe/.env.local)) {
    Copy-Item fe/.env.example fe/.env.local
}
```

Mặc định: `VITE_API_BASE_URL=/`, `API_PROXY_TARGET=http://localhost:5000`. Vite proxy `/api`, `/health`, `/hubs`, kể cả WebSocket. Nếu chạy API profile `http`, sửa **`API_PROXY_TARGET=http://localhost:5158`** trong `fe/.env.local` rồi restart Vite.

## 4. Chọn cách chạy backend

Chỉ chọn một backend dùng port 5000 cùng lúc. Frontend ở cả ba cách vẫn chạy bằng `npm run dev` như mục 5.

### A. Visual Studio Container (Dockerfile) — cách phát triển chính

1. Mở solution `be/be.sln`, đặt project `be` làm Startup Project.
2. Đảm bảo PostgreSQL/Redis/MinIO ở bước 3.2 đang chạy và đã migrate.
3. Nếu API Compose đang chiếm port 5000, chạy `docker compose stop api`.
4. Chọn **Container (Dockerfile)**, nhấn **F5**. API tại `http://localhost:5000`, Swagger tại `/swagger`.

Profile ánh xạ host 5000 → container 8080, bật seed Identity/admin và `DevelopmentData__SeedOnStartup=true`. Khi dữ liệu nghiệp vụ còn trống, tạo 3 phòng ban, 3 team, cây 7 danh mục, 5 Ticket và activity; nếu đã có dữ liệu nghiệp vụ thì bỏ qua. Chi tiết [seed Development](backend-development-data.md).

VS không tự khởi động các service Compose. Nếu không muốn bộ mẫu nhỏ, đặt cờ DevelopmentData của profile thành `false` trước F5. Bộ demo một tuần ở mục 6 không phụ thuộc seed nhỏ này.

### B. API chạy trực tiếp trên host

```powershell
dotnet run --project be --launch-profile http
```

API/Swagger: `http://localhost:5158/swagger`. Profile này không tự bật các cờ seed của Container; dùng CLI bước 3.5 và chỉnh proxy frontend về 5158. Muốn giữ port 5000 khi chạy trên host:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project be --no-launch-profile --urls http://localhost:5000
```

Connection PostgreSQL/MinIO dùng `localhost` và cổng host. Nếu trước đó provision wiki cho VS, đặt tạm `KnowledgeSources__BookStack__BaseUrl=http://localhost:6875/` trong environment của terminal chạy host; cấu hình wiki của VS được giữ nguyên.

### C. API bằng Docker Compose

Điền `JWT_SIGNING_KEY` riêng trong `.env`, ngẫu nhiên ít nhất 32 byte theo cấu hình JWT. Không coi key user-secrets đã tự được chuyển sang Compose. Database/MinIO được nội suy từ `.env`; CLI seed/admin ở bước 3.5 đã chuẩn bị tài khoản trong cùng database.

```powershell
docker compose --profile backend config --quiet
docker compose --profile backend up -d --build --wait postgres redis minio api
Invoke-WebRequest http://localhost:5000/health/live -UseBasicParsing
```

API tại `http://localhost:5000`; profile Compose hiện là Development, nhưng không kế thừa launchSettings/cờ seed của VS. Không cần bootstrap admin lại nếu đã chạy bước 3.5. Swagger và API có sẵn, nhưng frontend chưa được khởi động bởi lệnh Compose này.

## 5. Chạy hằng ngày

Không cần copy lại `.env`, restore hoặc seed lại nếu code/dependencies/cấu hình chưa đổi.

**Terminal root — hạ tầng:**

```powershell
docker compose up -d --build --wait postgres redis minio
# Nếu đã khởi tạo wiki; đổi project name nếu COMPOSE_PROJECT_NAME khác helpdesk:
docker compose -p helpdesk-wiki --env-file .env -f docker-compose.wiki.yml up -d --wait bookstack
```

**Backend:** F5 profile VS Container, hoặc một trong hai cách CLI/Compose ở mục 4.

**Terminal khác — frontend:**

```powershell
Set-Location E:\Web\Helpdesk\fe
npm run dev
```

Mở `http://localhost:5173`. Trang `/knowledge`, `/incidents` truy cập trước login; `/login` để đăng nhập; menu sau login phụ thuộc permission hiện tại. Với port mặc định, thử `http://localhost:5000/health/live` và `/swagger` để kiểm tra API.

## 6. Dữ liệu demo và kho tri thức — tùy chọn

### Demo một tuần cho dashboard/báo cáo

Sau migration/Identity/admin, từ root:

```powershell
.\be\scripts\Seed-DemoWeek.ps1
```

Script backup PostgreSQL Compose trước khi bổ sung 30 tài khoản demo phân role, 144 Ticket và dữ liệu lịch sử/SLA/comment/notification. Xem tài khoản tại **`.local/demo-week/credentials.txt`**, ma trận tại [backend-demo-week.md](backend-demo-week.md). Giữ nguyên tài khoản/nghiệp vụ đang có.

Timeline mô phỏng 7 ngày gần nhất **tại lần seed đầu**. Chạy lại không dời timeline, nhân đôi hay reset mật khẩu; do đó lần demo sau không tự thành một tuần dữ liệu mới. Không dùng account demo cho production. Ticket mới muốn có SLA cần policy active; bộ demo tạo policy khi phù hợp và giữ policy active đã tồn tại.

### Bài hướng dẫn và sự cố mẫu công khai

Trên database cũ, review/cấp quyền cộng đồng ở mục 7 trước; với database mới đã seed catalog hiện tại thì SystemAdmin có quyền tương ứng. CLI dưới sẽ **xuất bản** 6 bài biên soạn sẵn và 2 sự cố gắn `[Demo]`, không công khai Ticket thật:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$previousCommunitySeed = $env:CommunityDemoSeed__Enabled
try {
    $env:CommunityDemoSeed__Enabled = 'true'
    dotnet run --project be --no-launch-profile -- seed-community-demo
}
finally { $env:CommunityDemoSeed__Enabled = $previousCommunitySeed }
```

Xem `/knowledge`, `/incidents`; quản trị tại `/admin/knowledge`, `/admin/incidents`. [Quyền và quy trình xuất bản](public-community.md).

### BookStack và Wikipedia

Trước khi dùng nội dung cho thực tế/AI, xem [chuẩn hóa tài liệu và chuẩn bị 8 bản nháp](knowledge-authoring.md). Tài liệu demo chỉ có ở `?dataset=demo`; bài cũ chưa phân loại được giữ trong quản trị để rà soát.

```powershell
.\be\scripts\Initialize-KnowledgeWiki.ps1 -Provision
```

Lệnh này khởi tạo wiki riêng, editor/reader, 2 bài VPN/MFA và cấu hình connector trong user-secrets/`.env`. BookStack tại `http://localhost:6875`; tài khoản ở **`.local/wiki/credentials.txt`**. Restart API sau provision. Trạng thái bootstrap `.local/wiki/bootstrap.json` cần được giữ riêng để chạy lặp.

User có `knowledge.manage` vào `/admin/knowledge` để nhập trang thành **bản nháp**; người có `knowledge.publish` duyệt trước public. Nhập lặp chỉ mở bài đã nhập, không cập nhật từ nguồn hay ghi đè nội dung. Wikipedia mặc định bật tại `/knowledge`, không cần API key; chỉ gọi ra Internet khi người đọc chủ động tìm. Chi tiết [knowledge-integrations.md](knowledge-integrations.md).

Helper provision wiki chỉ dùng cho instance localhost mới với image đã pin; wiki có sẵn/production cần cấu hình token đọc theo tài liệu riêng. Tài khoản wiki và Helpdesk độc lập, chưa có SSO.

## 7. Cập nhật code, migration và quyền trên database cũ

Sau khi lấy code mới, chạy lại `dotnet tool restore`, `dotnet restore be/be.sln`, `npm ci` trong `fe/` khi manifests/lockfile đổi. Kiểm tra migration, backup và review SQL theo bước 3.4 trước khi update database. Restart API/Vite sau thay đổi code hoặc environment cần nạp lại.

Chạy `seed-identity` để có permission mới không tự thay grant của role cũ. Những lệnh dưới **chủ động mở rộng quyền**, chỉ chạy sau khi đã xem nhu cầu; không cần chạy toàn bộ trên database mới:

| CLI sau `dotnet run --project be --no-launch-profile --` | Tác động |
| --- | --- |
| `grant-account-administration` | Cấp quyền quản trị tài khoản/role liên quan cho SystemAdmin theo catalog. |
| `grant-department-administration` | Cấp quyền quản trị phòng ban cho SystemAdmin. |
| `grant-category-administration` | Cấp quyền quản trị danh mục cho SystemAdmin. |
| `grant-community-administration` | Cấp 4 quyền quản lý/xuất bản tri thức và sự cố cho SystemAdmin. |
| `grant-ticket-workflow` | Cấp confirm/reopen/cancel cho 4 role mặc định theo ma trận, vẫn kiểm tra Ticket ACL. |
| `grant-team-leadership` | Cấp `team.manage` cho TeamLeader; vẫn cần membership trưởng nhóm active đúng phạm vi. |

Giữ terminal Development để các lệnh đọc đúng connection/user-secrets. Tài khoản dùng nhiều role được hợp quyền; tên role không thay thế permission hoặc kiểm tra tài nguyên từ backend.

## 8. Kiểm tra và các lệnh thường dùng

**Hạ tầng/API** — từ root:

```powershell
docker compose ps
docker compose exec -T postgres sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
docker compose exec -T redis sh -c 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli ping'
Invoke-WebRequest http://localhost:9000/minio/health/live -UseBasicParsing
Invoke-WebRequest http://localhost:5000/health/live -UseBasicParsing
Invoke-RestMethod http://localhost:5000/api/v1/public/knowledge-sources
```

Kỳ vọng PostgreSQL nhận kết nối, Redis `PONG`, MinIO/API HTTP 200. API `/health/live` chỉ kiểm tra process sống, không chứng minh database/migration/storage sẵn sàng. Login, upload/download, SLA và SignalR cần kiểm tra luồng tương ứng.

**Build/test backend** — từ root:

```powershell
dotnet build be/be.sln -c Release -warnaserror
.\be\tests\Run-PersistenceTests.ps1 -Configuration Release
```

Runner tự tạo PostgreSQL/MinIO test riêng, migrate/kiểm thử/CLI smoke rồi dọn tài nguyên do nó tạo. `dotnet test` không có test environment phù hợp có thể bỏ qua integration tests; không dùng kết quả skip để kết luận đầy đủ đã đạt.

**Frontend** — từ root:

```powershell
Push-Location fe
try {
    npm run lint
    npm test
    npm run build
    # Chỉ lần đầu hoặc khi cần tải browser cho E2E:
    npx playwright install chromium
}
finally { Pop-Location }
.\be\tests\Run-FrontendAcceptanceTests.ps1
```

Frontend acceptance dùng production bundle với API/database/storage test riêng, không yêu cầu server Vite/API đang mở. `npm run test:e2e` chỉ dùng trực tiếp khi đã chuẩn bị các environment test riêng theo [frontend release acceptance](frontend-release-acceptance.md). Không chạy nhiều build cùng configuration trong khi test host đang giữ DLL trên Windows.

**Log** — API Compose hoặc wiki, không áp dụng cho container debug độc lập của VS:

```powershell
docker compose logs --tail 100 api
docker compose logs --tail 100 minio
docker compose -p helpdesk-wiki --env-file .env -f docker-compose.wiki.yml logs --tail 100 bookstack
```

Với VS Dockerfile, xem Output/Debug hoặc cửa sổ Containers. Kiểm tra/redact trước khi chia sẻ log.

## 9. Dừng dịch vụ, backup và dựng lại

**Dừng bình thường, giữ dữ liệu:** Ctrl+C cho frontend/host API, Stop Debugging cho VS; từ root:

```powershell
docker compose --profile backend stop
docker compose -p helpdesk-wiki --env-file .env -f docker-compose.wiki.yml stop
```

Chỉ dừng API Compose để nhường port cho VS: `docker compose stop api`. Có thể dùng `docker compose --profile backend down` để bỏ containers/network và giữ named volumes. Không thêm `-v` khi chỉ muốn dừng.

**Backup PostgreSQL trước nâng cấp** — dump nhị phân trong container rồi copy, tránh redirect binary qua PowerShell:

```powershell
New-Item -ItemType Directory -Path .local/backups -Force | Out-Null
$backupFile = 'helpdesk-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.dump'
docker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -f /tmp/helpdesk-manual-backup.dump'
if ($LASTEXITCODE -ne 0) { throw 'Database backup failed.' }
docker compose cp postgres:/tmp/helpdesk-manual-backup.dump (Join-Path .local/backups $backupFile)
if ($LASTEXITCODE -ne 0) { throw 'Cannot copy database backup.' }
```

Giữ backup ở nơi truy cập hạn chế, ngoài Git. Dump PostgreSQL không chứa object MinIO hoặc dữ liệu wiki: cần backup MinIO và cả hai volume wiki riêng, cùng cấu hình/secrets cần cho khôi phục. Không coi file credentials hoặc demo seed là bản backup dữ liệu người dùng.

**Nếu containers/images đã bị xóa nhưng volumes còn:** chạy lại hạ tầng, wiki đã cấu hình, backend/frontend theo mục 5. Không cần seed/migrate lại nếu database vẫn ở đúng phiên bản.

**Nếu volumes đã bị xóa:** giữ nguyên `.env`/user-secrets, chạy lại bước 3.2; review/apply toàn bộ migration vào PostgreSQL mới, seed Identity/admin ở bước 3.5 rồi chọn bộ demo ở mục 6. VS chỉ tạo mẫu nhỏ khi trống; không tự khôi phục dữ liệu thật. Muốn khôi phục dữ liệu thật phải dùng backup PostgreSQL, MinIO và wiki phù hợp thay vì seed lại.

Để dựng lại wiki mẫu sau mất volumes, dùng `Initialize-KnowledgeWiki.ps1 -Provision` và giữ `.local/wiki/` để tái sử dụng bootstrap. Bài người dùng từng biên soạn cần restore backup riêng. Bộ demo một tuần dùng mốc lần seed vào database mới, còn chạy lại trên database có marker thì giữ mốc cũ.

## 10. Lỗi thường gặp

| Hiện tượng | Kiểm tra/cách xử lý |
| --- | --- |
| `Startup Identity seed failed` | Database đã healthy? Đúng DB/user/password trong user-secrets? Đã apply migration? VS cần host override `host.docker.internal`. |
| `password authentication failed` | Password đang có trong volume có khớp cấu hình không? Thay `.env` không tự đổi user/password của database đã tạo. |
| Không có admin đăng nhập | Chạy seed/CLI bootstrap hoặc F5 đúng profile, xem `BootstrapAdmin:*` trong Manage User Secrets; script không reset admin đã có. |
| API báo thiếu JWT/signing key | VS: chạy Initialize-DevelopmentSecrets; Compose: điền `JWT_SIGNING_KEY` ở `.env` và recreate API. |
| Upload bị tắt/lỗi MinIO | Chạy Initialize-ComposeStorage, đồng bộ DevelopmentStorage, kiểm tra Minio enabled/endpoint và restart API; không thay app key bằng root key. |
| Port 5000 đang bận | Không chạy đồng thời API Compose và VS Dockerfile; dừng đúng API đang dùng cổng. |
| Vite lỗi proxy hoặc hub không nối | API có chạy đúng 5000/5158 không? `API_PROXY_TARGET` khớp chưa? Restart Vite sau khi sửa environment. |
| Port 5173 đang bận | Vite dùng strictPort; dừng đúng frontend cũ thay vì chờ nó tự chuyển cổng. |
| Đăng nhập được nhưng 403/thiếu menu | Kiểm tra grant hiện tại và scope ở database; seed catalog không tự cấp quyền mới cho role cũ. |
| SLA trống/dashboard ít dữ liệu | Tạo policy active cho Ticket mới, hoặc nạp bộ demo; kiểm tra bộ lọc thời gian/ACL và mốc tuần demo đã seed. |
| BookStack source 503 | Wiki đã healthy? Connector đã enabled/token chưa hết hạn? Đúng BookId và URL theo host/container? Restart API sau provision. |
| Wikipedia 503 | Kiểm tra Internet/DNS/TLS/rate limit tới vi.wikipedia.org; kho Helpdesk vẫn dùng được. |
| CORS khi frontend/API khác origin | Cấu hình `Cors:AllowedOrigins` đúng origin HTTP(S), không path/wildcard; cùng origin qua Vite proxy không cần mở CORS tùy tiện. |
| DB đúng cấu hình nhưng CLI đọc nơi khác | `dotnet run` không đọc `.env`; kiểm tra terminal Development và environment đang override user-secrets. |

## 11. Build frontend container và chuẩn bị phát hành

Root Compose hiện phục vụ **development**, không có service `fe`/`web`, không phải cấu hình production đầy đủ. Có thể build image frontend từ root:

```powershell
docker build -f fe/Dockerfile -t helpdesk-web:local fe
```

Image dùng Nginx HTTPS/SPA fallback, yêu cầu mount certificate/key thật tại `/etc/nginx/tls` và cấu hình `API_UPSTREAM` trên network có backend. Không chạy image này rồi kỳ vọng nó tự có HTTPS hoặc kết nối được API từ `localhost` trong container. Xem [hướng dẫn image/deploy/E2E](frontend-release-acceptance.md).

Trước production cần hoàn tất backend bước 18: proxy/ForwardedHeaders tin cậy, HTTPS/readiness, backup/restore/retention, cấu hình secrets/CORS và vận hành worker/realtime. Tắt seed Development/demo, không public database/MinIO Console/Swagger như cấu hình local. AI local là tùy chọn riêng theo hướng dẫn bên dưới, không bắt buộc để chạy các nghiệp vụ Helpdesk.

### AI local miễn phí (tùy chọn)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File be/scripts/Initialize-LocalAssistant.ps1
```

Lệnh khởi động stack Ollama riêng, tải `qwen3:1.7b`, cấu hình `.env`/backend user-secrets; sau đó restart backend. Cần đủ RAM/disk Docker và Internet cho lần tải đầu. Không có migration mới ở bước trợ lý. Muốn có câu trả lời kèm nguồn cần bài Knowledge Operational đã được duyệt/publish; thiếu tài liệu thì API hướng dẫn liên hệ hỗ trợ. Xem [cấu hình và API trợ lý local](public-assistant.md) để kiểm tra endpoint, model và các trường hợp dự phòng.

Publish/thu hồi tài liệu tự cập nhật index trong PostgreSQL. Có thể kiểm tra ID/revision và lập checkpoint local bằng CLI `knowledge-index check --manifest <đường-dẫn-tuyệt-đối.json>` trong terminal Development đã cấu hình kết nối DB. Xem [câu lệnh đối soát và phục hồi chỉ mục](knowledge-index-synchronization.md); bước 4 không có migration mới, chỉ rebuild trong khoảng bảo trì khi thực sự cần.

## 12. Tài liệu chuyên sâu

- [Backend Docker/VS setup](backend-setup.md), [Identity/admin/JWT](backend-identity-foundation.md), [migration/persistence](backend-persistence.md).
- [Attachments/MinIO](backend-attachments.md), [seed Development](backend-development-data.md), [demo một tuần](backend-demo-week.md).
- [Cổng tri thức/sự cố](public-community.md), [BookStack/Wikipedia](knowledge-integrations.md).
- [Chuẩn hóa tài liệu](knowledge-authoring.md), [truy xuất đoạn](knowledge-retrieval.md), [API trợ lý AI local](public-assistant.md).
- [Frontend README](../fe/README.md), [nghiệm thu/phát hành](frontend-release-acceptance.md), [GitHub Actions](github-actions.md).
