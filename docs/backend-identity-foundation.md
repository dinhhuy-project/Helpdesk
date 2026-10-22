# Nền tảng API/configuration và seed Identity

Đợt này đồng bộ EF Core Tools về **8.0.32**; runtime/Relational/Design vẫn 8.0.32, provider Npgsql 8.0.11. Giữ nguyên .NET 8, Domain và migration InitialCreate đã áp dụng. Không có migration mới.

## Trạng thái

- API có ProblemDetails/ValidationProblemDetails, traceId, xử lý lỗi tập trung, CORS allowlist và policy rate limit chuẩn bị cho Authentication.
- Có lệnh seed Identity và bootstrap admin chủ động. Profile Visual Studio **Container (Dockerfile)** bật seed catalog và bootstrap admin đầu tiên khi startup qua `Identity__SeedOnStartup=true` và `Identity__BootstrapAdminOnStartup=true`; các profile khác mặc định tắt. Không tự chạy migration.
- Chưa có login, phát hành/validate JWT, refresh cookie, authentication middleware hoặc permission authorization. Có dữ liệu role không đồng nghĩa endpoint đã được bảo vệ.

## Cấu hình

`ConnectionStrings:Helpdesk` là key ưu tiên (đúng với Compose). Nếu trống, backend dùng `ConnectionStrings:DefaultConnection` để tương thích cấu hình Visual Studio hiện có. Không log hoặc đưa connection string vào response. Host dùng `localhost`; container Visual Studio dùng `host.docker.internal`; Compose dùng `postgres`. Lệnh `dotnet run` không tự đọc `.env`.

Profile Container (Dockerfile) đặt `Database__HostOverride=host.docker.internal`; backend dùng NpgsqlConnectionStringBuilder thay riêng host, giữ nguyên database/port/credentials. Vì vậy appsettings trên máy có thể tiếp tục dùng localhost khi chạy trực tiếp. Nếu dùng database từ xa, điều chỉnh override trong profile.

| Section/key | Mặc định / hành vi |
| --- | --- |
| `Cors:AllowedOrigins` | Rỗng: không cho cross-origin; Development có `http://localhost:5173` |
| `AuthRateLimit:PermitLimit` | 5 request mỗi window, hợp lệ 1–1000 |
| `AuthRateLimit:WindowSeconds` | 60 giây, hợp lệ 1–3600 |
| `Jwt:Issuer`, `Audience`, `SigningKey` | Chưa bắt buộc khi cả ba chưa cấu hình; cấu hình một phần bị từ chối |
| `Jwt:RequireConfiguration` | Mặc định false; profile Docker đặt true, bắt buộc issuer/audience/key đầy đủ |
| `Jwt:AccessTokenMinutes` | 15, hợp lệ 1–60 |
| `Jwt:RefreshTokenDays` | 7, hợp lệ 1–30 |

Khi cấu hình JWT, issuer/audience phải có và key ít nhất 32 byte UTF-8; dùng key ngẫu nhiên qua user-secrets/environment, không đưa vào source. Các options này chỉ định nghĩa cấu hình cho bước auth sau, chưa bật xác thực. Options CORS/rate limit/JWT được kiểm tra khi server khởi động; sai cấu hình dừng startup. CLI initializer chỉ kiểm tra cấu hình cần cho lệnh tương ứng.

### Chuẩn bị secrets cho profile Docker

Từ root, chạy một lần trên máy development:

```powershell
& ./be/scripts/Initialize-DevelopmentSecrets.ps1
```

Script bổ sung các key còn thiếu trong user-secrets của `be`, không ghi vào source, không xoay key/password đã có và không log giá trị bí mật. Mặc định: `BootstrapAdmin:Email=admin@helpdesk.local`, display name `Helpdesk Administrator`, password ngẫu nhiên từ 24 byte CSPRNG; `Jwt:Issuer=Helpdesk.Api`, `Audience=Helpdesk.Web`, access 15 phút, refresh 7 ngày, signing key từ 64 byte CSPRNG (Base64).

Trong Visual Studio, chuột phải project **be → Manage User Secrets** để xem `BootstrapAdmin:Email` và `BootstrapAdmin:Password`, hoặc chỉnh credentials trước lần bootstrap đầu. Script cũng hỗ trợ `-AdminEmail` và `-AdminDisplayName` khi các key chưa tồn tại. Giữ file secrets ngoài Git; user-secrets dành cho local development. Visual Studio Container Tools mount secrets cho app development; nếu vừa tạo secrets, Stop Debugging rồi F5 lại để nhận cấu hình mới.

Không có password/signing key mặc định trong launchSettings. Nếu chuyển sang máy mới, chạy script trước F5. JWT được cấu hình sẵn nhưng **chưa có API login/phát token hoặc middleware xác thực** ở bước này.

CORS yêu cầu origin HTTP(S) chính xác, không wildcard, path hoặc dấu `/` cuối. Chưa bật `AllowCredentials`; cần review thêm cookie/CSRF trước bước refresh. Có thể override bằng `Cors__AllowedOrigins__0`.

Policy `authentication` giới hạn theo `RemoteIpAddress`, hàng đợi bằng 0, trả 429 ProblemDetails và Retry-After. Chỉ áp dụng khi endpoint được gắn `[EnableRateLimiting(ApiPolicies.AuthenticationRateLimit)]`; hiện chưa có endpoint login nên chưa áp dụng lên endpoint nghiệp vụ nào. Không tin trực tiếp header X-Forwarded-For; khi triển khai proxy cần cấu hình trusted proxies riêng. Rate limit hiện lưu trong bộ nhớ từng process.

## Lỗi API

- Lỗi input của `[ApiController]`: HTTP 400, `errors` theo field với thông báo chung, không echo giá trị đầu vào.
- Optimistic concurrency và unique/FK conflict của EF/PostgreSQL: HTTP 409 với thông báo chung.
- Lỗi bất ngờ: HTTP 500; không trả stack trace, exception message, SQL hoặc config ngay cả trong Development.
- Status chưa có body (ví dụ 404): ProblemDetails qua status-code middleware.
- Response lỗi có `traceId`. Handler log exception type/status/trace, không log exception object/message hoặc body. Tắt raw exception log của `ExceptionHandlerMiddleware` để không ghi trùng dữ liệu nhạy cảm.

## Seed catalog

### Tự động với profile Container (Dockerfile)

Chọn profile **Container (Dockerfile)** và F5: `IdentityStartupSeeder` chạy trước khi API nhận request, tạo role/permission còn thiếu bằng cùng `IdentityInitializer.SeedAsync()`, sau đó gọi bootstrap admin đầu tiên từ user-secrets. Nếu catalog đã đầy đủ thì không ghi thay đổi; role đã có luôn giữ nguyên grant/description. Không tạo dữ liệu Ticket hoặc phòng ban.

Cả hai cờ `Identity:SeedOnStartup` và `Identity:BootstrapAdminOnStartup` mặc định false, được bật riêng trong `launchSettings.json` của profile Docker. Bootstrap tự chuẩn bị catalog trước, kể cả khi chỉ bật cờ bootstrap. Chế độ này chỉ cho phép môi trường Development; bật trong Production sẽ dừng startup. Compose và profile http/https không tự bật các cờ này.

Database phải kết nối được và đã áp dụng migration. Nếu seed/bootstrap thất bại, API không khởi động phục vụ request; kiểm tra kết nối PostgreSQL (host container Visual Studio thường dùng `host.docker.internal`), migration history và BootstrapAdmin trong user-secrets rồi chạy lại. Không tự migrate hoặc bỏ qua lỗi. Có thể tắt `Identity__BootstrapAdminOnStartup` trong profile nếu chỉ muốn seed catalog và dùng lệnh bootstrap thủ công bên dưới.

Thông báo startup phân biệt lỗi không kết nối được, sai thông tin đăng nhập, database không tồn tại, thiếu schema/table và thiếu quyền PostgreSQL. Chỉ cung cấp hướng dẫn cố định, không đưa raw exception, connection string hoặc password vào log/exception cuối.

### Lệnh thủ công

Chạy từ root với connection string của database đã migration. Nếu dùng user-secrets, chạy trong Development:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project be/be.csproj --no-launch-profile -- seed-identity
```

Lệnh tạo permission/role còn thiếu. Chỉ **role mới được tạo** nhận tập quyền mặc định sau:

| Role | Quyền khi khởi tạo |
| --- | --- |
| Requester | ticket.create, ticket.read.own, ticket.comment.public |
| SupportAgent | Requester + ticket.read.team, ticket.update.status, ticket.comment.internal |
| TeamLeader | SupportAgent + ticket.assign |
| SystemAdmin | Đủ 11 PermissionCodes hiện tại, gồm user.manage, team.manage, ticket.manage.sla và ticket.read.all |

Đây là bộ quyền mặc định ban đầu; truy cập Ticket vẫn phải có resource authorization ở bước nghiệp vụ. TeamLeader chưa được cấp team.manage toàn hệ thống. Quyền xác nhận/mở lại ticket cho requester và quyền quản trị bổ sung sẽ được review cùng use case, không suy ra từ role.

Chạy lại không thay đổi role description, không xóa grant tùy chỉnh, không khôi phục grant đã bị gỡ khỏi role đã tồn tại. Do đó role có sẵn nhưng chưa có grant sẽ giữ nguyên, cần quản lý quyền riêng. Không xóa/recreate role để áp lại mặc định lên môi trường đã tùy chỉnh.

Hai initializer dùng chung PostgreSQL advisory transaction lock, tránh tạo trùng khi chạy đồng thời. Mỗi lệnh là một transaction và kiểm tra không còn migration pending trước khi ghi; không tự migrate database.

## Bootstrap admin ban đầu

Chạy `seed-identity` trước. Cung cấp `BootstrapAdmin:Email`, `DisplayName`, `Password` qua environment hoặc user-secrets. Không truyền password thành tham số command line. Ví dụ PowerShell nhập password ẩn và chỉ giữ environment cho thời gian chạy lệnh:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:BootstrapAdmin__Email = Read-Host 'Email admin'
$env:BootstrapAdmin__DisplayName = Read-Host 'Tên hiển thị'
$bootstrapSecret = Read-Host 'Mật khẩu admin (12–128 ký tự)' -AsSecureString
try {
    $env:BootstrapAdmin__Password = [System.Net.NetworkCredential]::new('', $bootstrapSecret).Password
    dotnet run --project be/be.csproj --no-launch-profile -- bootstrap-admin
}
finally {
    Remove-Item Env:BootstrapAdmin__Email, Env:BootstrapAdmin__DisplayName, Env:BootstrapAdmin__Password -ErrorAction SilentlyContinue
    $bootstrapSecret.Dispose()
}
```

Email được trim/lowercase và kiểm tra định dạng; display name 1–200 ký tự; password 12–128 ký tự. Hash bằng ASP.NET Core `PasswordHasher<User>` IdentityV3, PBKDF2 với 210.000 iterations; không lưu mật khẩu thô.

Lệnh và startup chỉ tạo **admin đầu tiên**. Nếu đã có bất kỳ UserRole SystemAdmin nào (kể cả account inactive), bootstrap no-op mà không yêu cầu lại credentials, không reset password/activate hoặc tạo thêm admin. Nếu chưa có admin nhưng email đã thuộc một user, bootstrap từ chối và không nâng quyền user đó. Đổi password trong secrets sau khi tạo admin không đổi password trong database. Phục hồi tài khoản và tạo admin bổ sung cần luồng quản trị riêng.

Exit code: 0 thành công/no-op; 1 cấu hình/điều kiện/DB lỗi; 2 truyền thêm tham số bị từ chối. Lỗi điều kiện đã biết có thông báo hướng dẫn cố định; lỗi DB bất ngờ chỉ log loại lỗi, không log raw exception. Chạy initializer thành công chưa cho phép đăng nhập vì Auth API chưa triển khai.

## Kiểm thử

```powershell
dotnet restore be/be.sln
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Các test HTTP dùng TestServer; controller `__tests` chỉ được đăng ký trong test factory, không nằm trong API production. Test kiểm tra lỗi sanitized, validation/traceId, CORS, rate limit, config không hợp lệ và connection-string fallback.

PostgreSQL test dùng container riêng: kiểm tra seed đồng thời không trùng, bảo toàn grant/description đã chỉnh, không nâng quyền email có sẵn, hai API startup bootstrap đồng thời chỉ tạo một admin, hash xác minh được và chạy lại không thay tài khoản kể cả khi không còn credentials bootstrap. Test xác minh cấu hình JWT bắt buộc/đầy đủ và chặn bootstrap trong Production. Runner còn gọi entry point `seed-identity` trên chính database test sau suite. Không seed vào database Helpdesk thật trong quá trình kiểm thử.

Tham khảo: [user-secrets cho development](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-8.0), [mount secrets của Visual Studio Container Tools](https://learn.microsoft.com/visualstudio/containers/container-volume-mapping).

Bước tiếp theo: triển khai Login + `/users/me`, JWT validation và permission policies; sau đó refresh/logout với rotation và cookie/CSRF strategy đã chốt.
