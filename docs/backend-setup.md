# Khởi tạo backend với Docker Compose

Hạ tầng local cho backend ASP.NET Core .NET 8 hiện có. Backend đã có Domain, EF Core/Npgsql, Identity/auth, quản trị tổ chức và API Ticket đến phần 14. Xem [persistence](backend-persistence.md), [nền tảng Identity](backend-identity-foundation.md) và [roadmap](backend-roadmap.md). MinIO client đã có cho attachments; Redis chưa tích hợp nghiệp vụ. Storage mặc định tắt đến khi [review/apply AttachmentStorage](attachment-storage-review.md) và hoàn tất [cấu hình bucket/app credentials](backend-attachments.md).

## Chuẩn bị

- Docker Desktop đang chạy Linux containers và Docker Compose v2.24 trở lên.
- .NET SDK 8 nếu chạy API trực tiếp trên máy.
- Chạy các lệnh dưới đây tại thư mục gốc repository.

```powershell
Copy-Item .env.example .env
```

Điền `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `MINIO_ROOT_PASSWORD` trong `.env`. Dùng chuỗi ngẫu nhiên dài, ví dụ chỉ gồm chữ và số để tương thích connection string PostgreSQL và Redis đã chuẩn bị; mật khẩu MinIO tối thiểu 8 ký tự. Không commit `.env`. Không chạy `docker compose config` không có `--quiet` khi chia sẻ log vì lệnh sẽ in credentials đã nội suy.

## Chạy hạ tầng

```powershell
docker compose config --quiet
docker compose up -d --build --wait postgres redis minio
docker compose ps
```

PostgreSQL và MinIO dùng named volumes. Redis chỉ là cache nên không lưu bền. Tất cả cổng publish chỉ bind `127.0.0.1`.

| Dịch vụ | Trên máy host | Trong mạng Compose |
| --- | --- | --- |
| PostgreSQL | `localhost:5432` | `postgres:5432` |
| Redis | `localhost:6379` | `redis:6379` |
| MinIO API | `http://localhost:9000` | `http://minio:9000` |
| MinIO Console | `http://localhost:9001` | `http://minio:9001` |
| Backend (profile backend) | `http://localhost:5000` | `http://api:8080` |

Có thể đổi cổng host qua `.env`; cổng nội bộ giữ nguyên. Khi `.env` có MINIO_ENABLED=true và hai MINIO_APP credentials, lệnh `docker compose up -d --build --wait postgres redis minio` tự tạo/kiểm tra bucket private, app user và policy trong startup MinIO. Healthcheck chỉ thành công sau bootstrap; không cần service khởi tạo riêng. Giữ `.env` thì sau khi xóa containers/images/volumes có thể chạy lại đúng một lệnh này để khởi tạo lại MinIO với cùng credentials. Build source cần mạng nếu chưa có cache.

Trên máy mới thiếu app keys, chạy `& ./be/scripts/Initialize-ComposeStorage.ps1` một lần để sinh credentials còn thiếu, bật storage và gọi Compose. Với Visual Studio, đồng bộ riêng MinIO sang user-secrets bằng `& ./be/scripts/Initialize-DevelopmentStorage.ps1 -UseComposeConfiguration` khi cấu hình thay đổi; profile Container dùng `Minio__HostOverride=host.docker.internal`. Xem [hướng dẫn storage và smoke test](backend-attachments.md).

**Sau khi xóa volume PostgreSQL**, database mới rỗng, cần áp dụng lại các migrations đã review trước khi chạy API/seed. Lệnh hạ tầng không tự chạy migration, bootstrap admin hoặc khôi phục dữ liệu đã xóa. PostgreSQL/MinIO dùng credentials đã lưu trong `.env`; Redis vẫn dùng mật khẩu cũ.

MinIO Community hiện phân phối source-only nên Dockerfile build bản cố định `RELEASE.2025-10-15T17-29-55Z` từ mã nguồn chính thức. Lần build đầu cần mạng và có thể mất vài phút. Tham khảo [hướng dẫn MinIO](https://github.com/minio/minio#source-only-distribution) và [release](https://github.com/minio/minio/releases/tag/RELEASE.2025-10-15T17-29-55Z). Repository upstream đã ngừng bảo trì; cấu hình này dành cho local, cần đánh giá giải pháp storage trước production.

## Chạy API trong Docker

```powershell
docker compose --profile backend up -d --build --wait
Invoke-WebRequest http://localhost:5000/health/live
```

Trước khi bật API Compose, bổ sung `JWT_SIGNING_KEY` (ngẫu nhiên, ít nhất 32 byte) vào `.env`; issuer/audience mặc định `Helpdesk.Api`/`Helpdesk.Web`. User-secrets của Visual Studio không tự có trong Compose. Xem [hướng dẫn login/JWT](backend-authentication.md). Chạy riêng các service hạ tầng không yêu cầu key này.

Swagger: `http://localhost:5000/swagger`. Endpoint `GET /health/live` trả `200 Healthy` khi tiến trình API hoạt động; **không kiểm tra kết nối PostgreSQL/Redis/MinIO**. Compose chờ healthcheck của ba dịch vụ trước khi khởi động API; chưa có readiness check cho API hoặc dependency sau khởi động.

### Debug bằng Visual Studio: Container (Dockerfile)

Chọn project `be` làm Startup Project, chọn profile **Container (Dockerfile)** và nhấn F5. Profile cố định HTTP host port `5000` ánh xạ vào port `8080` trong container; Swagger mở tại `http://localhost:5000/swagger`.

Profile này bật seed role/permission và bootstrap admin đầu tiên, giữ nguyên dữ liệu/quyền/tài khoản đã có. Trước F5 trên máy mới, chạy `& ./be/scripts/Initialize-DevelopmentSecrets.ps1` từ root để chuẩn bị credentials admin và JWT trong user-secrets; xem [hướng dẫn Identity/JWT](backend-identity-foundation.md). Không tự chạy migration. Cần PostgreSQL sẵn sàng và connection string đúng cho container.

Profile cũng bật `DevelopmentData__SeedOnStartup=true`: sau Identity/admin, tạo 3 phòng ban, 3 team với admin làm member/leader, cây 7 danh mục và 5 Ticket mẫu kèm activity khi các bảng nghiệp vụ còn trống. Có dữ liệu nghiệp vụ thì bỏ qua, không ghi đè hoặc nhân đôi. Tắt riêng cờ này trong launchSettings nếu không muốn mẫu; chỉ cho phép Development. Xem [dữ liệu mẫu](backend-development-data.md).

Profile đặt `Database__HostOverride=host.docker.internal` để thay riêng host trong connection string: `localhost` trong appsettings của máy host không dùng trực tiếp từ container được. Database, port, username và password được giữ nguyên. Nếu PostgreSQL chạy ở máy khác, sửa host override cho phù hợp. Các profile chạy trực tiếp và Compose không đặt override này.

Nếu API từ Compose đang chạy, dừng riêng service API trước để giải phóng port `5000`:

```powershell
docker compose stop api
```

PostgreSQL, Redis và MinIO vẫn chạy. Visual Studio khởi chạy container độc lập, không tự khởi động các service Compose hoặc đọc `.env` của Compose.

Hoặc chạy API trực tiếp để debug sau khi bật hạ tầng:

```powershell
dotnet restore be/be.sln
dotnet build be/be.sln --no-restore
dotnet run --project be/be.csproj --launch-profile http
```

Swagger local: `http://localhost:5158/swagger`, health: `http://localhost:5158/health/live`. Compose tự đọc `.env` để nội suy cấu hình container; `dotnet run` **không tự đọc `.env`**. Khi thêm các client backend, dùng environment variables hoặc user-secrets với địa chỉ `localhost` và cổng host trong bảng trên.

## Kiểm tra và dừng

```powershell
docker compose exec postgres sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
docker compose exec redis sh -c 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli ping'
Invoke-WebRequest http://localhost:9000/minio/health/live
docker compose logs --tail 100 api
docker compose --profile backend down
```

Lệnh `down` giữ nguyên dữ liệu volumes. Không dùng `down -v` để dừng thông thường. `POSTGRES_*` chỉ khởi tạo tài khoản/database khi volume còn trống; thay mật khẩu trong `.env` không tự đổi mật khẩu database đã tồn tại. Không xóa volume để khắc phục lỗi credentials.

Domain nằm trong `be/Domain`; xem [quy ước Domain](backend-domain.md). Model có 20 entity/tám migration; người dùng đã migrate SlaMvp phần 16. [Dashboard/báo cáo phần 17](backend-reporting.md) không thêm migration, secret/grant/service, chỉ cần F5 code mới, giữ port Container 5000. Để Ticket mới có SLA, tạo policy active qua API có ticket.manage.sla theo [hướng dẫn SLA](backend-sla.md). Có thể bắt đầu frontend; [roadmap](backend-roadmap.md) còn phần 18 chuẩn bị vận hành production.
