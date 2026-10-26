# Quản lý phiên: refresh rotation và logout

Backend có `POST /api/v1/auth/refresh` và `POST /api/v1/auth/logout`, cùng response login mở rộng. Refresh dùng bảng `identity.refresh_tokens` và liên kết `replaced_by_token_id` đã có trong InitialCreate. Code hiện có thêm [quản trị tài khoản/đổi mật khẩu](backend-accounts.md), cần [migration AccountAdministration](account-administration-review.md) trước khi chạy. Profile **Container (Dockerfile)** vẫn dùng `http://localhost:5000` với JWT/user-secrets hiện có; sau nâng cấp F5 lại, không tạo lại admin.

## Contract và cách truyền token

Login trả HTTP 200 với các field cũ `accessToken`, `tokenType`, `expiresAt`, `user` và hai field mới:

```json
{
  "refreshToken": "<credential ngẫu nhiên 86 ký tự base64url>",
  "refreshTokenExpiresAt": "<thời hạn tuyệt đối của phiên, UTC ISO 8601>"
}
```

Đoạn JSON trên chỉ minh họa hai field bổ sung. Mỗi lần login tạo một chuỗi token độc lập, kể cả cùng user. `Jwt:RefreshTokenDays` (environment `Jwt__RefreshTokenDays`, Compose `JWT_REFRESH_TOKEN_DAYS`) là thời hạn phiên, mặc định 7 ngày, hợp lệ 1–30. Mọi token được rotation từ login đó giữ nguyên thời hạn này. Thay cấu hình chỉ ảnh hưởng login mới.

Credentials được truyền rõ ràng qua JSON; access token qua `Authorization: Bearer`. Backend không đặt hoặc đọc cookie xác thực. Client trình duyệt hiện nên giữ token trong bộ nhớ, tránh localStorage và không log body chứa token. Việc tải lại trang sẽ cần login lại khi client không còn credential trong bộ nhớ. Frontend hiện chưa có auth client/interceptor.

Các endpoint auth chỉ nhận `application/json`, giới hạn body 4096 byte. Refresh token phải có đúng 86 ký tự `[A-Za-z0-9_-]`, không trim. Response thành công/lỗi credential có `Cache-Control: no-store`. Production dùng HTTPS. Khi bổ sung cookie HttpOnly để duy trì phiên trình duyệt, phải thiết kế CSRF, Secure/SameSite và CORS credentials cùng lúc; hiện không có credential do trình duyệt tự gửi để xác thực request.

## POST /api/v1/auth/refresh

```http
POST /api/v1/auth/refresh
Content-Type: application/json
```

```json
{ "refreshToken": "<giá trị mới nhất nhận từ login hoặc refresh>" }
```

Endpoint không yêu cầu access token; access token hết hạn không ngăn việc refresh. Thành công trả **200** với cùng cấu trúc đầy đủ của login, gồm access token mới, refresh token mới, hồ sơ và quyền hiện tại. Sau thành công, token cũ bị thu hồi, trỏ tới token thay thế; client phải thay **cả hai token**.

| Trường hợp | Kết quả |
| --- | --- |
| Refresh token hợp lệ, chưa dùng, user active | 200, tạo cặp mới và thu hồi cặp cũ trong transaction |
| Token hết hạn, đã thu hồi, không tồn tại, user inactive | 401 ProblemDetails, cùng title `Phiên đăng nhập không hợp lệ hoặc đã hết hạn.`, có `traceId`, không có token |
| Token đã rotation bị dùng lại | 401 và thu hồi mọi token kế tiếp trong chuỗi đó, kể cả access token đang dùng |
| Thiếu/sai định dạng token | 400 ValidationProblemDetails; không echo credential |
| Content-Type dạng form | 415 |
| Vượt giới hạn auth | 429 với Retry-After; chưa thực hiện rotation |

Phiên có thời hạn tuyệt đối, không sliding expiration. Khi hết hạn phiên, user login lại dù trước đó đã refresh nhiều lần. Access token mặc định 15 phút, vẫn kiểm tra issuer/audience/signature/type/lifetime với clock skew 30 giây. Refresh/session expiry được so sánh trực tiếp với giờ UTC, không có khoảng gia hạn đó.

Khi refresh một phiên của user inactive, chuỗi đó được thu hồi bền vững; activate user lại không khôi phục chuỗi đã thu hồi. Các API có bearer luôn từ chối user inactive. API status và đổi mật khẩu trong module quản trị thu hồi toàn bộ phiên trong cùng transaction với thay đổi tài khoản; bật active lại không khôi phục token cũ.

## POST /api/v1/auth/logout

Nhận cùng JSON `{ "refreshToken": "<token của phiên cần kết thúc>" }`. Không cần access token còn hạn. Token hợp lệ về hình dạng luôn trả **204 No Content**, kể cả token không tồn tại hoặc logout nhiều lần; không tiết lộ trạng thái phiên qua response. Token thiếu/sai định dạng trả 400, quá rate limit trả 429.

Logout thu hồi token được gửi cùng mọi token thay thế trong chuỗi. Vì vậy logout bằng token cũ vẫn kết thúc được cặp hiện tại, kể cả khi refresh vừa chạy xong. Không xóa lịch sử token, không kết thúc các lần login độc lập khác của cùng user. Client xóa token khỏi bộ nhớ sau logout; 429/network error không chứng minh rằng server đã thu hồi phiên.

## Access token và tái sử dụng

JWT có claim `sid` là ID bản ghi refresh token đi cùng nó. Mỗi request được bảo vệ kiểm tra bản ghi này thuộc `sub`, còn hạn, chưa bị thu hồi và user active. Đây là kiểm tra database trên mỗi request, nên logout, refresh hoặc replay detection làm access token cũ bị từ chối với 401 ngay ở request tiếp theo. Request đã vượt qua kiểm tra trước lúc transaction thu hồi commit có thể tiếp tục hoàn tất.

Endpoint có [permission policy](backend-authorization.md) còn kiểm tra grant hiện tại trong database; đổi quyền không yêu cầu rotation hoặc login lại. JWT không chứa snapshot role/permission để quyết định truy cập.

Sau cập nhật này, JWT phát hành từ code cũ thiếu `sid` không còn được chấp nhận; login lại một lần. Không cần đổi signing key hay reset dữ liệu. `sid` thay đổi theo mỗi cặp token, không phải ID ổn định của toàn chuỗi phiên.

Ví dụ: login tạo `R0/A0`; refresh `R0` tạo `R1/A1`, khi đó `R0/A0` không còn dùng được. Refresh `R1` tạo `R2/A2`. Nếu ai đó gửi lại `R0`, backend trả 401 và thu hồi cả `R1`, `R2`; `A2` cũng bị từ chối. Một lần login khác tạo chuỗi riêng và tiếp tục hoạt động.

Client cần giữ tối đa **một request refresh đang chạy** cho một chuỗi token; các request cần token mới chờ cùng kết quả rồi dùng cặp mới. Với nhiều tab cùng chia sẻ credentials, phải điều phối cả giữa các tab. Không refresh lặp vô hạn khi nhận 401. Nếu response refresh bị mất sau khi server đã commit, gửi lại token cũ bị coi là tái sử dụng và cần login lại. Không có grace window hoặc trả lại plaintext token thay thế từ database.

Hai request refresh cùng token: chỉ một request có thể trả 200; request còn lại thấy token đã thu hồi, trả 401 và thu hồi kết quả của request thắng. Khi refresh đua với logout, sau cả hai thao tác hoàn tất không còn cặp token dùng được trong chuỗi đó.

## Lưu trữ và concurrency

Refresh token sinh từ 64 byte CSPRNG (512 bit ngẫu nhiên), encode base64url, chỉ trả giá trị thô trong response. Database lưu SHA-256 dạng hex ở `token_hash`, có unique index. Không dùng JWT làm refresh token và không lưu plaintext. SHA-256 ở đây dùng cho credential ngẫu nhiên entropy cao; password người dùng vẫn dùng PasswordHasher/PBKDF2 riêng.

Mọi thao tác tạo/rotate/logout chạy trong transaction và khóa hàng user bằng PostgreSQL `FOR UPDATE` trước khi đọc trạng thái token để cập nhật. Khóa serialize thao tác phiên của cùng user, hoạt động giữa nhiều API instance, không phụ thuộc khóa trong process. Lookup token hash trước transaction chỉ xác định owner; quyết định token còn dùng được diễn ra sau khóa. Rotation insert bản ghi mới, revoke bản ghi cũ và gắn liên kết trong cùng transaction. Phát hiện replay commit việc thu hồi trước khi trả thất bại.

Thu hồi dùng recursive CTE theo `replaced_by_token_id`, giới hạn trong cùng user, có chống lặp khi dữ liệu cũ tạo cycle. Token cũ được giữ để phát hiện replay. Hiện chưa có job dọn lịch sử; khi thêm cleanup phải giữ các ancestor ít nhất tới lúc cả chuỗi hết hạn và phối hợp khóa với thao tác phiên. Không chạy thao tác cleanup/xóa dữ liệu tự động ở startup.

Login, refresh, logout dùng chung policy `authentication`, mặc định tổng cộng 5 request/60 giây theo IP, hàng đợi 0. Rate limit hiện thuộc từng API process; việc rotation/revoke được bảo vệ bằng database trên mọi instance. Không log token, hash, request body hoặc response body chứa credentials.

## Thử và kiểm tra

1. F5 lại profile Docker, mở `http://localhost:5000/swagger`.
2. Login bằng credentials admin hiện có trong user-secrets, giữ cặp token trả về.
3. Authorize bằng access token, gọi `/api/v1/users/me` để xác nhận phiên.
4. Gọi `/api/v1/auth/refresh`, thay access token trong Authorize và giữ refresh token mới. Access token cũ gọi `/me` sẽ nhận 401.
5. Gọi `/api/v1/auth/logout` bằng refresh token mới: nhận 204, access token vừa cấp gọi `/me` nhận 401.
6. Muốn thử replay: login lại, refresh một lần rồi gửi lại refresh token đầu tiên. Sau 401, cả token mới nhất cũng không còn dùng được. Nếu gặp 429 khi thử liên tiếp, chờ Retry-After.

```powershell
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Test dùng PostgreSQL tạm: hash-only storage, liên kết rotation/absolute expiry, JWT hết hạn vẫn refresh/logout được, session hết hạn, token sai owner/thiếu sid, user inactive, replay nhiều thế hệ, giữ phiên độc lập, logout idempotent, refresh đồng thời và race logout/refresh, validation, JSON transport, rate limit. Workflow GitHub Actions hiện có tự chạy toàn bộ suite mới; không cần thêm secrets. Không thử login hoặc sửa admin thật trong suite.

Tham khảo: [RFC 9700 mục 4.14.2 về refresh token rotation và replay](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.14.2), [PostgreSQL row locks](https://www.postgresql.org/docs/16/explicit-locking.html#LOCKING-ROWS), [ASP.NET Core CSRF](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery?view=aspnetcore-8.0).
