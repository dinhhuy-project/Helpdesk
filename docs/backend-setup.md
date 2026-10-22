# Khởi tạo backend với Docker Compose

Hạ tầng local cho backend ASP.NET Core .NET 8 hiện có. Backend đã có Domain, EF Core/Npgsql DbContext + mapping và migration InitialCreate (người dùng xác nhận đã apply). Xem [persistence](backend-persistence.md), [review migration](initial-create-review.md) và [nền tảng API/seed Identity](backend-identity-foundation.md). Chưa có Redis client, MinIO client hoặc API nghiệp vụ. ConnectionStrings__Helpdesk đã được đăng ký cho DbContext; các key Redis/MinIO vẫn là cấu hình chuẩn bị.

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

Có thể đổi cổng host qua `.env`; cổng nội bộ giữ nguyên. Đăng nhập MinIO Console bằng credentials trong `.env`, tạo bucket `helpdesk-attachments` và giữ quyền truy cập private. Chưa tự động tạo bucket hay cấp tài khoản ứng dụng; credentials root trong Compose chỉ phục vụ local bootstrap. Khi tích hợp upload, cần tài khoản riêng giới hạn quyền vào bucket.

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

Domain đã được khởi tạo trong `be/Domain`; xem [quy ước Domain](backend-domain.md). DbContext, Fluent API và InitialCreate đã có cho 18 entity cùng model/integration tests. Xem [review và cách áp dụng migration](initial-create-review.md) trước khi cập nhật database đích, sau đó triển khai Authentication và RBAC theo `guides.md`.
