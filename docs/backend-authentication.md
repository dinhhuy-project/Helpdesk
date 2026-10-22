# Đăng nhập và thông tin người dùng

Đã có login email/mật khẩu, phát hành/validate access token JWT, hồ sơ user hiện tại, [quản lý phiên với refresh rotation/logout](backend-sessions.md) và [permission policies đọc database](backend-authorization.md). Giữ .NET 8 và `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.32. Không có thay đổi schema hoặc migration mới. Chưa triển khai OIDC hoặc cookie.

## Chạy và cấu hình

Profile Visual Studio **Container (Dockerfile)** tiếp tục dùng `http://localhost:5000`, host PostgreSQL override và seed/bootstrap admin khi cần. JWT issuer/audience/signing key được lấy từ user-secrets đã chuẩn bị bằng `be/scripts/Initialize-DevelopmentSecrets.ps1`. Xem mật khẩu admin tại **be → Manage User Secrets → BootstrapAdmin:Password**; không ghi mật khẩu/token vào source hoặc log.

Từ bước này, mọi chế độ **chạy API** đều yêu cầu JWT config đầy đủ, được validate trước khi phục vụ request:

- `Jwt:Issuer` và `Jwt:Audience`: không rỗng.
- `Jwt:SigningKey`: ít nhất 32 byte UTF-8, lấy từ user-secrets/environment/secret manager; key development hiện được sinh ngẫu nhiên từ 64 byte.
- `Jwt:AccessTokenMinutes`: 1–60, mặc định 15.
- `Jwt:RefreshTokenDays`: 1–30, mặc định 7, thời hạn tuyệt đối của phiên; rotation không kéo dài thời hạn này.

Không còn cờ `Jwt:RequireConfiguration` để cho phép API chạy thiếu key. Các lệnh seed/bootstrap và thao tác EF design-time không khởi chạy HTTP server nên không bắt buộc JWT config chỉ để thao tác database.

Khi chạy API bằng Compose, điền `JWT_SIGNING_KEY` trong `.env` (key ngẫu nhiên, không commit), issuer/audience mặc định `Helpdesk.Api`/`Helpdesk.Web`. Các biến `JWT_*` đã được truyền vào container API. Chỉ chạy PostgreSQL/Redis/MinIO thì chưa cần key này. User-secrets của Visual Studio không tự được mount vào service API Compose.

Production phải dùng HTTPS và secret manager; HTTP port 5000 chỉ phục vụ local development. Không gửi credentials/access token qua HTTP trên mạng không tin cậy.

## POST /api/v1/auth/login

Endpoint anonymous, có policy rate limit theo IP (`AuthRateLimit` mặc định 5 request/60 giây) và giới hạn request body 4096 byte.

```json
{
  "email": "admin@helpdesk.local",
  "password": "<mật khẩu trong user-secrets>"
}
```

Email bắt buộc, định dạng email, tối đa 320 ký tự. Password bắt buộc, tối đa 128 ký tự; không trim password. Quy tắc tạo mật khẩu mới 12–128 ký tự thuộc bootstrap, không dùng để chặn đăng nhập tài khoản cũ có password ngắn hơn.

Thành công trả HTTP 200:

```json
{
  "accessToken": "<JWT được cấp>",
  "tokenType": "Bearer",
  "expiresAt": "<thời điểm UTC dạng ISO 8601>",
  "refreshToken": "<credential ngẫu nhiên dùng một lần>",
  "refreshTokenExpiresAt": "<thời hạn tuyệt đối của phiên, UTC ISO 8601>",
  "user": {
    "id": "<UUID>",
    "email": "admin@helpdesk.local",
    "displayName": "Helpdesk Administrator",
    "avatarUrl": null,
    "departmentId": null,
    "roles": ["SystemAdmin"],
    "permissions": ["<permission hiện tại từ database>"]
  }
}
```

Đây là hình dạng response minh họa, không phải token/credential thật. Response thành công có `Cache-Control: no-store`. Trình duyệt nên giữ token trong bộ nhớ; khi access token hết hạn, dùng refresh token để lấy cặp mới theo [contract quản lý phiên](backend-sessions.md). Không lưu token dài hạn trong localStorage. API nhận credentials bằng JSON và không tự đặt cookie.

Lỗi trả ProblemDetails/ValidationProblemDetails có `traceId`:

| Status | Trường hợp |
| --- | --- |
| 400 | Body/field không hợp lệ |
| 401 | Sai mật khẩu, email không tồn tại/không xác định duy nhất, user inactive, tài khoản không có password hoặc stored hash không hợp lệ |
| 413 | Request vượt giới hạn body trên server hỗ trợ giới hạn này |
| 429 | Vượt rate limit, có Retry-After |

Các lỗi credential dùng chung title `Email hoặc mật khẩu không hợp lệ.` và không cho biết account có tồn tại/đang bị khóa hay không. Account không có hash vẫn thực hiện verify bằng dummy hash để giảm chênh lệch xử lý. Hash dùng ASP.NET Core PasswordHasher; hash cũ được rehash sau login đúng, update có điều kiện theo hash cũ và trạng thái active để không ghi đè thay đổi đồng thời.

Tra cứu email dùng lowercase và từ chối nếu có nhiều bản ghi khác nhau chỉ ở hoa/thường. Không tự gộp hoặc sửa dữ liệu cũ. Để tối ưu lookup ở quy mô lớn, có thể bổ sung cột/index email chuẩn hóa trong migration riêng.

## GET /api/v1/users/me

```http
Authorization: Bearer <accessToken nhận từ login>
```

Endpoint yêu cầu xác thực, lấy user ID từ claim `sub`. Không nhận requester/user ID hoặc role từ client để quyết định danh tính. Trả HTTP 200 với cấu trúc `user` ở trên, lấy role/permission hiện tại trực tiếp từ database và loại trùng. Không trả password hash, refresh-token hash hoặc EF entity. Response có `Cache-Control: no-store`.

JWT được ký HS256, type `at+jwt`, có `sub`, `sid`, `jti`, `iat`, `nbf`, `exp`, `iss`, `aud`. `sid` là ID bản ghi refresh token đi cùng access token đó. Token không chứa email, role hay permission. Backend validate chữ ký, thuật toán, type, issuer, audience, thời hạn (clock skew 30 giây), GUID user/session ID và sự hiện diện của các claim bắt buộc. Mỗi request có token hợp lệ còn kiểm tra phiên chưa hết hạn/thu hồi, thuộc đúng user và user active trong database. Deactivate, logout hoặc rotation có hiệu lực với access token cũ ngay trên request tiếp theo.

Thiếu/sai/hết hạn token, user đã bị xóa hoặc inactive trả 401 ProblemDetails và `WWW-Authenticate: Bearer`, không trả chi tiết lỗi JWT. Permission policies kiểm tra trạng thái tài khoản/phiên và grant hiện tại từ database; thiếu quyền trả 403 chung. `/users/me` chỉ yêu cầu xác thực, không yêu cầu quyền quản trị. Các API nghiệp vụ tiếp theo gắn `[RequirePermission]` và bổ sung kiểm tra phạm vi tài nguyên Ticket theo use case; xem [hướng dẫn phân quyền](backend-authorization.md).

Có `POST /api/v1/auth/refresh` và `POST /api/v1/auth/logout`, đều nhận JSON `{ "refreshToken": "<giá trị hiện tại>" }` và không yêu cầu access token còn hạn. Refresh trả cùng cấu trúc như login; logout trả 204. Refresh token dùng lại sau rotation gây thu hồi chuỗi token liên quan; xem [hành vi khi hết hạn, tái sử dụng và refresh đồng thời](backend-sessions.md). JWT cũ không có `sid` bị từ chối sau khi cập nhật code, cần login lại một lần.

## Thử trong Swagger

1. Stop Debugging rồi F5 lại profile Docker để dùng code mới.
2. Mở `http://localhost:5000/swagger`.
3. Gọi `POST /api/v1/auth/login` bằng credentials admin trong user-secrets.
4. Copy `accessToken`, chọn **Authorize** và dán token (không cần thêm tiền tố Bearer với scheme HTTP bearer).
5. Gọi `GET /api/v1/users/me`; kiểm tra hồ sơ, role và permission.

Swagger đánh dấu bearer cho endpoint có `[Authorize]`; login/refresh/logout được mô tả anonymous vì tự kiểm tra credentials trong JSON. Frontend hiện còn là starter template, chưa có UI đăng nhập hoặc API client auth; response contract được ghi ở đây để tích hợp sau.

## Cấu trúc code và kiểm thử

- `Application/Features/Auth`: use cases, profile model và abstractions user store/password verifier/token issuer, không phụ thuộc EF/HTTP.
- `Infrastructure/Authentication`: truy vấn EF, verify/rehash password, ký JWT và cấu hình bearer validation.
- `Contracts`: DTO request/response; Controller chỉ xử lý HTTP và gọi Application.
- Test HTTP với PostgreSQL riêng: login/me, role/permission cập nhật, lỗi credential dùng chung, inactive sau login, token sai chữ ký/issuer/audience/thuật toán/type, hết hạn/chưa hiệu lực, thiếu claim/expiry, unsigned/malformed/absent token, user không tồn tại, password rehash và rate limit.

```powershell
dotnet restore be/be.sln
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Không đăng nhập hoặc đổi password admin thật trong suite này. Không cần apply migration mới.

Tham khảo: [JWT bearer validation](https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication), [PasswordHasher](https://learn.microsoft.com/aspnet/core/security/data-protection/consumer-apis/password-hashing).
