# Phân quyền theo permission hiện tại

Đã đăng ký policy cho 14 mã trong `PermissionCodes.All`, gồm `role.manage`, `department.manage`, `category.manage` tách khỏi `user.manage`, với helper `[RequirePermission(...)]` và permission checker đọc PostgreSQL. Giữ .NET 8, EF/Npgsql 8.x. Phần 9 đã được người dùng xác nhận apply; phần 10 thêm [scope Ticket dùng chung](backend-ticket-access.md), không có migration, permission hoặc secret mới.

## Luồng quyết định truy cập

1. Authentication kiểm tra chữ ký/issuer/audience/type/lifetime của JWT, claim `sub`/`sid`, trạng thái phiên và user active như [luồng quản lý phiên](backend-sessions.md).
2. Với endpoint có permission policy, handler lấy user/session ID từ principal đã xác thực.
3. `PermissionChecker` truy vấn database để kiểm tra cùng lúc: phiên thuộc đúng user, chưa hết hạn/thu hồi, user active và có grant qua `UserRole → RolePermission → Permission`.
4. Chỉ khi đủ điều kiện mới cho phép thực thi endpoint. Không tìm thấy grant thì từ chối.

Permission của nhiều role được hợp lại: chỉ cần một role đang gán có grant cho permission được yêu cầu. Tên role `SystemAdmin` không tự vượt qua kiểm tra; gỡ một grant khỏi role đó làm user không còn được phép nếu không có role khác cấp cùng quyền. Backend không đọc role/permission từ JWT, header, query hoặc body để quyết định grant.

Không cache grant theo user, phiên hoặc process. Mỗi lần kiểm tra permission đều chạy query mới; thêm/gỡ `UserRole` hoặc `RolePermission` có hiệu lực ở lần kiểm tra tiếp theo với cùng JWT, không cần login/refresh hoặc khởi động lại API. Metadata policy được đăng ký lúc startup; dữ liệu grant được đọc khi thực thi.

## Gắn policy vào API

Controller/action nghiệp vụ sử dụng helper sau, với mã permission cố định trong source:

```csharp
using be.Api.Authorization;
using be.Domain.Constants;

[RequirePermission(PermissionCodes.UserManage)]
```

Helper kế thừa `AuthorizeAttribute`, đã yêu cầu Bearer authentication. Có thể dùng trên class hoặc method. Dạng chuẩn ASP.NET Core tương đương:

```csharp
[Authorize(Policy = PermissionCodes.UserManage)]
```

Các attribute kết hợp bằng **AND**. Ví dụ class có `TicketReadTeam`, action có `TicketAssign` thì request phải có cả hai permission. Nhiều `[RequirePermission]` trên cùng action cũng có ý nghĩa AND.

Helper từ chối tên ngoài `PermissionCodes.All`, kể cả khác hoa/thường hoặc dùng tên role thay cho permission. Không nhận tên policy từ request để quyết định việc bảo vệ endpoint. Khi dùng `[Authorize(Policy = ...)]` trực tiếp, dùng constant đã đăng ký để tránh lỗi cấu hình do gõ sai.

Các policy hiện có:

| Constant | Tên policy |
| --- | --- |
| `TicketCreate` | `ticket.create` |
| `TicketReadOwn` | `ticket.read.own` |
| `TicketReadTeam` | `ticket.read.team` |
| `TicketReadAll` | `ticket.read.all` |
| `TicketAssign` | `ticket.assign` |
| `TicketUpdateStatus` | `ticket.update.status` |
| `TicketCommentPublic` | `ticket.comment.public` |
| `TicketCommentInternal` | `ticket.comment.internal` |
| `TicketManageSla` | `ticket.manage.sla` |
| `UserManage` | `user.manage` |
| `RoleManage` | `role.manage` |
| `TeamManage` | `team.manage` |
| `DepartmentManage` | `department.manage` |
| `CategoryManage` | `category.manage` |

`ticket.read.all`, `ticket.read.team`, `ticket.read.own` là các permission khác nhau; một policy không tự thay thế policy khác. Use case đọc Ticket cần chọn quy tắc phù hợp với các scope này khi triển khai truy vấn.

## Kiểm tra trong code

Trong controller đã có `[Authorize]`, có thể inject `IAuthorizationService` và kiểm tra một policy cố định:

```csharp
var result = await authorization.AuthorizeAsync(User, HttpContext, PermissionCodes.TicketAssign);
if (!result.Succeeded)
    return Forbid();
```

`IAuthorizationService.AuthorizeAsync` kiểm tra principal được caller cung cấp, không tự validate chữ ký JWT. Dùng principal từ authentication middleware; không tạo identity từ body/header hoặc lấy user ID của người gọi từ request. Query permission kiểm tra lại user/session để hỗ trợ kiểm tra imperative và chặn trạng thái vừa thay đổi sau bước authentication. Handler truyền `RequestAborted` vào query khi có HTTP context.

Application có abstraction `IPermissionChecker` tại `Application/Common/Authorization`, không phụ thuộc HTTP/EF. Infrastructure triển khai truy vấn EF và authorization handler scoped theo request; API chứa attribute. Caller của checker phải lấy danh tính từ ngữ cảnh đã xác thực, không tin ID do client tự khai.

## HTTP và OpenAPI

| Status | Ý nghĩa |
| --- | --- |
| 401 | Authentication từ chối JWT/phiên/user hoặc thiếu token; có `WWW-Authenticate: Bearer` |
| 403 | Request đã xác thực nhưng không thỏa policy; ProblemDetails chung, có `traceId`, `Cache-Control: no-store` |
| 500 | Lỗi bất ngờ khi tra quyền; handler lỗi chung che chi tiết, endpoint không được thực thi |

403 không tiết lộ permission còn thiếu hoặc dữ liệu role. Swagger tự thêm Bearer security cùng response 401/403 dạng ProblemDetails cho `[RequirePermission]`, kể cả attribute nằm trên class. Auth endpoints có `[AllowAnonymous]` tiếp tục dùng credentials của chính endpoint đó.

`GET /api/v1/users/me` giữ `[Authorize]`: mọi user có phiên hợp lệ và active có thể xem hồ sơ của mình, kể cả chưa được gán role. Hồ sơ trả role/permission hiện tại từ database. API quản lý user dùng `user.manage`; cấp role/grant dùng `role.manage` cùng [scope và guard quản trị](backend-accounts.md). Health endpoint giữ cách truy cập hiện có. Các API nghiệp vụ tiếp theo cần được gắn permission tương ứng khi thêm endpoint.

## Permission và phạm vi tài nguyên

Policy xác nhận user có quyền thực hiện loại thao tác. Ticket đã có [TicketScope và guard](backend-ticket-access.md) kiểm tra requester/assignee/member/leader hiện tại. GET Ticket dùng Authorize và hợp các read permission bằng OR trong resource query, không đặt nhiều attribute AND. Scope lọc trước count/page/detail; không có read grant trả 403, Ticket ngoài scope hoặc không tồn tại cùng trả 404. Có ticket.update.status không đồng nghĩa được sửa mọi Ticket hoặc mọi field. Comment/internal/file tiếp tục cần rule riêng khi triển khai.

Thay đổi quyền/deactivate không hủy request đọc đã vượt qua kiểm tra trước lúc transaction quản trị commit. TicketWriteGuard dùng transaction lock chung, khóa user/Ticket và kiểm tra lại session/quyền/scope/version trước khi thay đổi, cùng cơ chế đồng bộ với quản trị quyền/tổ chức. Use case ghi vẫn phải validate workflow, ghi TicketActivity và save/commit trong transaction đó. Cờ access trả về cho UI không được dùng thay guard.

Khi thêm permission mới: khai báo constant và đưa vào `PermissionCodes.All`, bổ sung catalog seed và policy usage/tests tương ứng. Seed tiếp tục bảo toàn grant đã chỉnh của role có sẵn; việc cấp quyền mới cho role có sẵn cần thao tác quản trị được xác định rõ.

## Kiểm thử

```powershell
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Các test kiểm tra 14 policy với bốn role mặc định, thêm/gỡ role membership và grant trên JWT đang dùng, nhiều role, kết hợp class/action policy, SystemAdmin mất grant, claim/header không cấp quyền, 401/403 và Swagger. Permission checker được kiểm tra trực tiếp với user inactive, phiên hết hạn/thu hồi/sai owner và permission không đăng ký. API quản trị có tests scope/grant và last-admin riêng; [SupportTeam](backend-support-teams.md) có scope leader theo nhóm, lọc trước phân trang; [Category](backend-ticket-categories.md) tách category.manage khỏi ticket.create. Mutation recheck trong transaction; lỗi tra quyền giữ response sanitized.

Endpoint `__tests/permissions`, `__tests/team-permissions` và harness ghi `__tests/ticket-scope` chỉ nằm trong assembly test, được TestServer đăng ký; không xuất hiện trong API production. Harness dùng production guard và save/activity thật để kiểm tra không ghi ngoài scope. Toàn bộ test database chạy trên PostgreSQL tạm, không sửa grant hay tài khoản thật. GitHub Actions hiện có tự chạy suite này; không cần thêm secret hoặc thay workflow.

Tham khảo: [ASP.NET Core policy-based authorization](https://learn.microsoft.com/aspnet/core/security/authorization/policies?view=aspnetcore-8.0), [resource-based authorization](https://learn.microsoft.com/aspnet/core/security/authorization/resourcebased?view=aspnetcore-8.0).
