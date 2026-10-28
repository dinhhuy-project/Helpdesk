# Đăng nhập và quản lý phiên — frontend bước 2

Đã nối frontend với các contract auth hiện có: `/login`, `/account`, `/account/password`, kiểm tra `/users/me`, refresh rotation, logout và đổi mật khẩu. Giữ nguyên backend/API/database/package; không có migration hoặc secret mới.

## Chạy và sử dụng

1. Chạy API Visual Studio **Container (Dockerfile)** trên port 5000, dùng database đã migrate và JWT config hiện có.
2. Trong `fe/`, chạy `npm run dev`, mở `http://localhost:5173/login`.
3. Đăng nhập bằng tài khoản active. Ứng dụng gọi login, sau đó `/users/me`; chỉ mở trang tài khoản sau khi xác nhận được hồ sơ.
4. Trang tài khoản hiển thị tên/email, các role và permission hiện tại. Nút **Cập nhật thông tin**, focus lại cửa sổ, tab trở về visible hoặc kết nối lại sẽ kiểm tra `/me`.
5. Vào **Đổi mật khẩu**, nhập mật khẩu hiện tại, mật khẩu mới 12–128 ký tự và xác nhận. Sau thành công, mọi phiên bị backend thu hồi; frontend trở về login và yêu cầu mật khẩu mới.
6. **Đăng xuất** xóa phiên/cache trên trình duyệt ngay và gửi yêu cầu thu hồi về backend. Nếu mạng lỗi hoặc bị giới hạn request, giao diện nói rõ chưa xác nhận được thu hồi từ máy chủ.

**Reload, đóng/mở lại trang hoặc mở tab mới cần đăng nhập lại.** Access/refresh token chỉ tồn tại trong memory của từng ứng dụng/tab; không có cookie auth hoặc “ghi nhớ đăng nhập”. Mỗi lần login tạo chuỗi refresh riêng; các tab không dùng chung credential. Muốn duy trì qua reload cần thay đổi transport HttpOnly/HTTPS/CSRF/CORS cùng backend, không chỉ sửa frontend storage.

API login/refresh/logout/đổi mật khẩu dùng chung rate limit backend, mặc định 5 request/60 giây/IP. Khi thử nhiều lần bằng Swagger và UI, tôn trọng Retry-After; không tăng giới hạn hoặc reset database chỉ để bỏ qua 429.

## Contract đã dùng

| API | Mục đích |
| --- | --- |
| `POST /api/v1/auth/login` | JSON email/password; nhận cặp token và profile |
| `GET /api/v1/users/me` | Bearer; xác nhận danh tính và lấy role/permission hiện tại |
| `POST /api/v1/auth/refresh` | JSON refreshToken; nhận và thay cả hai token |
| `POST /api/v1/auth/logout` | JSON refreshToken; 204 khi yêu cầu hợp lệ về hình dạng |
| `POST /api/v1/users/me/password` | Bearer; JSON currentPassword/newPassword, 204 rồi login lại |

Password không trim; email được trim trước khi gửi. Confirmation chỉ kiểm tra ở UI, không gửi field ngoài DTO backend. Runtime parser kiểm tra các field profile, loại token, timestamps và hình dạng refresh token; không giải mã JWT để suy ra role/permission. Backend vẫn xác thực JWT/session/user active và quyền trên mỗi request.

`returnTo` giữ đường dẫn nội bộ sau login; chặn URL ngoài origin, backslash/whitespace, đường dẫn lỗi và quay vòng về login. Sau đổi mật khẩu, lần login tiếp theo trở về tài khoản.

## Tổ chức code và dữ liệu

```text
features/auth/
  api/          typed login/refresh/logout/me/password API và parser
  session/      điều phối phiên, rotation, cache scope và concurrency
  providers/    auth context, expiry timer, focus/online/visibility listeners
  hooks/        useAuth, sensitive form state và rate-limit countdown
  components/   route guard, lỗi form và thông báo kết thúc phiên
  pages/        login, account, change-password
  lib/          returnTo validation và nhãn role/permission
app/providers/app-services.ts
                tạo QueryClient, bare transport, session và authenticated client
```

- Credential nằm trong closure của session manager, không trong Zustand, React context, Query cache, URL, log, localStorage/sessionStorage hoặc persist middleware. State Zustand chỉ giữ trạng thái phiên, profile, scope, expiry và lỗi đã chuẩn hóa; không bật devtools/persist.
- Profile xác thực có một nguồn trong session state, không tạo thêm bản `/me` trùng trong Query cache. Các danh sách nghiệp vụ ở bước sau tiếp tục dùng TanStack Query.
- Form login/password gọi command qua hook, không dùng Query mutation để tránh giữ password trong mutation variables/cache. Field password được xóa sau submit. Không có retry tự động cho login/refresh/logout hoặc kết quả mutation không rõ do lỗi mạng.
- `apiClient` hiện có là transport công khai cho health. API nghiệp vụ cần xác thực dùng **`useAuth().client`**, truyền client đó vào typed API/hook của feature. Client này thêm Bearer, kiểm tra scope và phối hợp rotation; không tự tạo một Axios instance khác để bỏ qua session manager.
- Auth API luôn gọi bare transport riêng, tránh login/refresh/logout đi vào vòng xử lý 401 của protected client.

Ví dụ cho feature tiếp theo (không phải Ticket UI đã triển khai):

```ts
const { client, scope } = useAuth()
// Query key nghiệp vụ chứa scope và đầy đủ filter/page.
// queryFn: ({ signal }) => getTickets(client, filters, signal)
// key: ['tickets', scope, filters]
```

`scope` không chứa token hoặc claim sid. Scope đổi khi login/logout/đổi tài khoản hoặc `/me`/refresh phát hiện thay đổi role, permission, department. Trước khi công bố danh tính/quyền mới, ứng dụng hủy và xóa Query cache cũ. Request có AbortSignal và kiểm tra lại phiên/revision sau response, nên kết quả của user hoặc quyền cũ không được nhận trở lại dù transport bỏ qua abort.

Không coi các nhãn role/permission ở giao diện là authorization. Thay đổi membership/team chưa được phản ánh trực tiếp trong `/me` vẫn cần ACL backend và việc refetch từng resource; menu/resource guards đầy đủ triển khai tiếp ở bước 3 và các feature Ticket.

## Quy tắc refresh và logout

1. Trước protected request, chờ refresh đang chạy; nếu access token sắp hết hạn trong 10 giây thì gia hạn trước khi gửi.
2. Khi gặp 401, chỉ một request refresh chạy cho chuỗi phiên hiện tại; các request khác dùng chung promise. Request 401 đến muộn với access token cũ dùng cặp đã xoay, không tạo thêm rotation.
3. Chỉ replay protected request một lần sau 401. Nếu vẫn 401, kết thúc phiên; 403/429 từ resource không kích hoạt refresh. Không replay command do network/timeout/5xx.
4. Refresh thất bại vì network/timeout hoặc response không xác định: xóa credential và yêu cầu login. Không gửi lại refresh token cũ vì server có thể đã commit rotation và coi lần gửi sau là reuse.
5. Refresh trả 429: chưa rotate theo contract backend, giữ credential và chặn lần refresh tiếp theo đến Retry-After (60 giây nếu không có header). Không tạo vòng retry. Form login/password cũng có countdown chặn submit.
6. Logout thay phiên và xóa dữ liệu local ngay, kể cả refresh đang chạy. Response muộn không thể phục hồi phiên. Logout gửi token đã biết trước đó; backend thu hồi cả descendants nếu rotation đã diễn ra.
7. Login hoàn tất muộn sau khi phiên đã bị thay thế không được nhận vào UI; nếu đã biết credential trả về, client cố gắng logout chuỗi đó. Lỗi cleanup không phục hồi phiên local.
8. Absolute session expiry được kiểm tra khi gửi request và bằng timer khi trình duyệt idle; focus/online cũng kiểm tra lại. User inactive/revoked hết khả năng gia hạn sẽ trở về login với thông báo chung.

## Đổi mật khẩu và lỗi

Password command chờ refresh hiện tại, rồi giữ các protected request/refresh mới chờ thao tác này hoàn tất. Nếu chính password request bị 401, nó vẫn được refresh/replay một lần mà không tự chờ chính mình. Sau 204, xóa cặp token/cache; không cần gửi logout lần nữa vì backend đã revoke mọi phiên trong transaction.

| Kết quả | Giao diện/phiên |
| --- | --- |
| 400 validation | Lỗi field qua ProblemDetails; giữ phiên khi đổi mật khẩu |
| 400 `InvalidCurrentPassword` | Gắn lỗi vào mật khẩu hiện tại, giữ phiên |
| 401 login | Thông báo credential không hợp lệ; không refresh |
| 401 refresh hoặc replay protected vẫn 401 | Xóa phiên, yêu cầu login; không khẳng định lý do chính xác là hết hạn hay inactive |
| 403 | Hiển thị bị từ chối; không refresh hoặc logout lặp |
| 409 passwordless/business conflict | Hiển thị lỗi backend, không tự tạo workflow reset mật khẩu |
| 429 | Hiển thị thông báo/countdown theo Retry-After, không tự gửi lại |
| Network/timeout/5xx khi đổi password | Kết quả có thể chưa rõ: kết thúc phiên, yêu cầu login lại, thông báo thử mật khẩu mới nếu thay đổi đã được lưu |
| Logout lỗi | Local vẫn anonymous; thông báo chưa xác nhận thu hồi server, không tuyên bố remote logout thành công |

TraceId được giữ để tra cứu; không render HTML hoặc Axios request/config vào lỗi. Không có UI đăng ký, quên mật khẩu, OIDC hoặc reset password của người khác trong bước này.

## Kiểm thử và giới hạn xác minh

Chạy từ `fe/`:

```powershell
npm run lint
npm test
npm run build
```

Lượt triển khai bước 2: **114 tests / 15 files đạt**, lint và TypeScript/Vite build đạt. Không thêm package nên không thay lockfile. CI hiện có đã gọi `npm test`, tự bao gồm suite mới khi push; chưa chạy workflow GitHub từ xa.

Các tests mới gồm: concurrent 401/single refresh, 401 đến muộn, request chờ rotation, hủy một waiter, refresh response mất, 429 cooldown, logout/login/refresh race, đổi tài khoản với response cũ, cache/scope, user inactive/revoked, absolute expiry khi idle, profile xác nhận/mất quyền, password/refresh barrier, lỗi password và password response muộn sau đổi user. UI integration tests chạy qua wiring Axios thật với adapter fixture, kiểm tra routing, Bearer, JSON DTO, form và không lưu credential vào storage/cache.

Đã kiểm tra Edge headless desktop/mobile với **API fixtures**: guard/deep link, login lỗi, access expiry/rotation, `/me`, password lỗi/thành công, logout đang refresh, reload và browser storage rỗng; không có page error hoặc overflow ngang. Không thay mật khẩu tài khoản thật hoặc ghi database Helpdesk để chạy các test này. Chưa nghiệm thu browser với credential/backend thật, reverse proxy production hoặc SignalR; các bước đó không được suy ra từ fixture tests.

Tham khảo contract: [backend authentication](backend-authentication.md), [sessions](backend-sessions.md), [accounts/password](backend-accounts.md). [Bước 3 — khung ứng dụng và quyền giao diện](frontend-shell.md) đã bổ sung boundary chặn nội dung/menu khi xác nhận foreground hoặc profile lỗi, cùng sidebar và policy guards; kiểm tra nền do focus không xóa form đang nhập. Bước tiếp theo là Ticket list/detail.
