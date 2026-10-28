# Nền tảng API/configuration và seed Identity

EF Core Tools/runtime/Relational/Design dùng **8.0.32**, provider Npgsql 8.0.11, giữ .NET 8. Sau nền tảng InitialCreate đã có [AccountAdministration](account-administration-review.md) cho email chuẩn hóa và audit. Cần áp dụng migration đã review trước khi chạy code hiện tại; startup không tự migrate.

## Trạng thái

- API có ProblemDetails/ValidationProblemDetails, traceId, xử lý lỗi tập trung, CORS allowlist và policy rate limit chuẩn bị cho Authentication.
- Có lệnh seed Identity và bootstrap admin chủ động. Profile Visual Studio **Container (Dockerfile)** bật seed catalog và bootstrap admin đầu tiên khi startup qua `Identity__SeedOnStartup=true` và `Identity__BootstrapAdminOnStartup=true`; các profile khác mặc định tắt. Không tự chạy migration.
- Đã có login, phát hành/validate access token JWT, authentication middleware, `/api/v1/users/me`, [refresh rotation/logout](backend-sessions.md) và [permission policies](backend-authorization.md); xem [API Authentication](backend-authentication.md). Cookie và phân quyền từng tài nguyên Ticket sẽ được bổ sung theo use case.

## Cấu hình

`ConnectionStrings:Helpdesk` là key ưu tiên (đúng với Compose). Nếu trống, backend dùng `ConnectionStrings:DefaultConnection` để tương thích cấu hình Visual Studio hiện có. Không log hoặc đưa connection string vào response. Host dùng `localhost`; container Visual Studio dùng `host.docker.internal`; Compose dùng `postgres`. Lệnh `dotnet run` không tự đọc `.env`.

Profile Container (Dockerfile) đặt `Database__HostOverride=host.docker.internal`; backend dùng NpgsqlConnectionStringBuilder thay riêng host, giữ nguyên database/port/credentials. Vì vậy appsettings trên máy có thể tiếp tục dùng localhost khi chạy trực tiếp. Nếu dùng database từ xa, điều chỉnh override trong profile.

| Section/key | Mặc định / hành vi |
| --- | --- |
| `Cors:AllowedOrigins` | Rỗng: không cho cross-origin; Development có `http://localhost:5173` |
| `AuthRateLimit:PermitLimit` | 5 request mỗi window, hợp lệ 1–1000 |
| `AuthRateLimit:WindowSeconds` | 60 giây, hợp lệ 1–3600 |
| `Jwt:Issuer`, `Audience`, `SigningKey` | Bắt buộc đầy đủ khi chạy API ở mọi môi trường |
| `Jwt:AccessTokenMinutes` | 15, hợp lệ 1–60 |
| `Jwt:RefreshTokenDays` | 7, hợp lệ 1–30 |

JWT issuer/audience phải có và key ít nhất 32 byte UTF-8; dùng key ngẫu nhiên qua user-secrets/environment, không đưa vào source. Options CORS/rate limit/JWT được kiểm tra khi server khởi động; sai cấu hình dừng startup. API đã dùng options này để ký và xác thực access token. CLI initializer chỉ kiểm tra cấu hình cần cho lệnh tương ứng.

### Chuẩn bị secrets cho profile Docker

Từ root, chạy một lần trên máy development:

```powershell
& ./be/scripts/Initialize-DevelopmentSecrets.ps1
```

Script bổ sung các key còn thiếu trong user-secrets của `be`, không ghi vào source, không xoay key/password đã có và không log giá trị bí mật. Mặc định: `BootstrapAdmin:Email=admin@helpdesk.local`, display name `Helpdesk Administrator`, password ngẫu nhiên từ 24 byte CSPRNG; `Jwt:Issuer=Helpdesk.Api`, `Audience=Helpdesk.Web`, access 15 phút, refresh 7 ngày, signing key từ 64 byte CSPRNG (Base64).

Trong Visual Studio, chuột phải project **be → Manage User Secrets** để xem `BootstrapAdmin:Email` và `BootstrapAdmin:Password`, hoặc chỉnh credentials trước lần bootstrap đầu. Script cũng hỗ trợ `-AdminEmail` và `-AdminDisplayName` khi các key chưa tồn tại. Giữ file secrets ngoài Git; user-secrets dành cho local development. Visual Studio Container Tools mount secrets cho app development; nếu vừa tạo secrets, Stop Debugging rồi F5 lại để nhận cấu hình mới.

Không có password/signing key mặc định trong launchSettings. Nếu chuyển sang máy mới, chạy script trước F5. Login và `/users/me` đã có; dùng [hướng dẫn Swagger và contract](backend-authentication.md).

CORS yêu cầu origin HTTP(S) chính xác, không wildcard, path hoặc dấu `/` cuối. Chưa bật `AllowCredentials`; login/refresh/logout nhận credentials qua JSON, không dùng cookie. Nếu bổ sung cookie cần review CSRF và CORS credentials riêng. Có thể override bằng `Cors__AllowedOrigins__0`.

Policy `authentication` giới hạn theo `RemoteIpAddress`, hàng đợi bằng 0, trả 429 ProblemDetails và Retry-After. Login, refresh và logout dùng chung giới hạn này. Không tin trực tiếp header X-Forwarded-For; khi triển khai proxy cần cấu hình trusted proxies riêng. Rate limit hiện lưu trong bộ nhớ từng process.

## Lỗi API

- Lỗi input của `[ApiController]`: HTTP 400, `errors` theo field với thông báo chung, không echo giá trị đầu vào.
- Optimistic concurrency và unique/FK conflict của EF/PostgreSQL: HTTP 409 với thông báo chung.
- Lỗi bất ngờ: HTTP 500; không trả stack trace, exception message, SQL hoặc config ngay cả trong Development.
- Status chưa có body (ví dụ 404): ProblemDetails qua status-code middleware.
- Response lỗi có `traceId`. Handler log exception type/status/trace, không log exception object/message hoặc body. Tắt raw exception log của `ExceptionHandlerMiddleware` để không ghi trùng dữ liệu nhạy cảm.

## Seed catalog

### Tự động với profile Container (Dockerfile)

Chọn profile **Container (Dockerfile)** và F5: `IdentityStartupSeeder` chạy trước khi API nhận request, tạo role/permission còn thiếu bằng cùng `IdentityInitializer.SeedAsync()`, sau đó gọi bootstrap admin đầu tiên từ user-secrets. Nếu catalog đã đầy đủ thì không ghi thay đổi; role đã có luôn giữ nguyên grant/description. Tiếp theo, khi `DevelopmentData:SeedOnStartup=true` (đã bật trong profile Container), tạo [phòng ban/team/danh mục/Ticket mẫu](backend-development-data.md) nếu cả các bảng nghiệp vụ còn trống.

Cả ba cờ `Identity:SeedOnStartup`, `Identity:BootstrapAdminOnStartup` và `DevelopmentData:SeedOnStartup` mặc định false, được bật riêng trong `launchSettings.json` của profile Docker. Startup chuẩn bị catalog trước khi bootstrap/sample. Chế độ này chỉ cho phép môi trường Development; bật trong Production sẽ dừng startup. Compose và profile http/https không tự bật các cờ này. Có thể tắt riêng dữ liệu mẫu mà vẫn giữ seed Identity/bootstrap admin.

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
| Requester | ticket.create, ticket.read.own, ticket.comment.public, ticket.confirm, ticket.reopen, ticket.cancel |
| SupportAgent | Requester + ticket.read.team, ticket.update.status, ticket.comment.internal |
| TeamLeader | SupportAgent + ticket.assign + team.manage (quản trị nhóm còn cần membership IsLeader theo phần 8) |
| SystemAdmin | Đủ 17 PermissionCodes khi role được tạo mới, gồm user.manage, role.manage, department.manage, category.manage, team.manage, ticket.manage.sla, ticket.read.all và ba quyền requester workflow |

Đây là bộ quyền mặc định cho role được tạo mới; truy cập Ticket vẫn phải có resource authorization ở bước nghiệp vụ. TeamLeader có team.manage vẫn chỉ quản trị nhóm được chỉ định IsLeader active, không có quyền quản trị nhóm toàn hệ thống. Ba quyền requester workflow cần chính requester hoặc read.all và read scope, không suy ra từ role hoặc quyền xử lý của agent.

Chạy lại không thay đổi role description, không xóa grant tùy chỉnh, không khôi phục grant đã bị gỡ khỏi role đã tồn tại. Do đó role có sẵn nhưng chưa có grant sẽ giữ nguyên, cần quản lý quyền riêng. Không xóa/recreate role để áp lại mặc định lên môi trường đã tùy chỉnh.

Role SystemAdmin có sẵn muốn bật quyền quản trị tài khoản/role mới cần operator chủ động chạy `grant-account-administration` sau migration và seed catalog. Lệnh bổ sung riêng user.manage/role.manage còn thiếu, có audit, chạy lặp an toàn và không được gọi từ startup. Xem [trình tự nâng cấp](account-administration-review.md).

Phần 7 bổ sung `department.manage` vào catalog (tổng 13 permission). Sau khi review/apply DepartmentAdministration và seed, operator dùng `grant-department-administration` để bổ sung riêng quyền phòng ban cho SystemAdmin cũ nếu cần. Seed startup vẫn giữ grant cũ; không tự gọi lệnh grant. Xem [nâng cấp phòng ban](department-administration-review.md).

Phần 8 dùng `team.manage` đã có, catalog vẫn 13 mã. TeamLeader được seed mới nhận thêm team.manage; role cũ giữ grant và operator có thể chạy `grant-team-leadership` sau migration/seed nếu muốn bật quyền này. Lệnh không tự gán IsLeader: SystemAdmin phải chỉ định leadership cho từng membership active qua API riêng. Xem [phạm vi và hướng dẫn nhóm hỗ trợ](backend-support-teams.md).

Phần 9 bổ sung category.manage, catalog tại bước đó có 14 mã. Sau review/apply TicketCategoryAdministration và seed, operator dùng `grant-category-administration` nếu muốn bật quyền mới cho SystemAdmin cũ. Chỉ bổ sung đúng quyền này, lặp an toàn và có audit; startup không tự gọi. Lookup danh mục dùng ticket.create, tách khỏi quyền quản trị. Xem [danh mục Ticket](backend-ticket-categories.md).

Phần 12 thêm ticket.confirm/reopen/cancel, catalog tổng **17 mã**, không có migration mới. Role cũ giữ grant; operator seed rồi chạy `grant-ticket-workflow` nếu muốn cấp đúng ba quyền cho bốn role mặc định trong một transaction. Lệnh có audit/lặp an toàn, không sửa quyền khác hoặc custom role và không chạy khi startup. Xem [contract workflow và trình tự nâng cấp](backend-ticket-workflow.md).

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

Exit code: 0 thành công/no-op; 1 cấu hình/điều kiện/DB lỗi; 2 truyền thêm tham số bị từ chối. Lỗi điều kiện đã biết có thông báo hướng dẫn cố định; lỗi DB bất ngờ chỉ log loại lỗi, không log raw exception. Sau bootstrap, admin có thể gọi API login và `/users/me`.

## Kiểm thử

```powershell
dotnet restore be/be.sln
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Các test HTTP dùng TestServer; controller `__tests` chỉ được đăng ký trong test factory, không nằm trong API production. Test kiểm tra lỗi sanitized, validation/traceId, CORS, rate limit, config không hợp lệ và connection-string fallback.

PostgreSQL test dùng container riêng: kiểm tra seed đồng thời không trùng, bảo toàn grant/description đã chỉnh, không nâng quyền email có sẵn, hai API startup bootstrap đồng thời chỉ tạo một admin, hash xác minh được và chạy lại không thay tài khoản kể cả khi không còn credentials bootstrap. Test xác minh cấu hình JWT bắt buộc/đầy đủ và chặn bootstrap trong Production. Runner còn gọi entry point `seed-identity` trên chính database test sau suite. Không seed vào database Helpdesk thật trong quá trình kiểm thử.

Tham khảo: [user-secrets cho development](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-8.0), [mount secrets của Visual Studio Container Tools](https://learn.microsoft.com/visualstudio/containers/container-volume-mapping).

Bước tiếp theo: áp dụng permission theo use case quản trị/Ticket, bổ sung resource authorization khi xây dựng Ticket và tích hợp frontend theo contract auth hiện tại. Nếu chuyển sang cookie HttpOnly, cần bổ sung CSRF và cấu hình production HTTPS tương ứng.
