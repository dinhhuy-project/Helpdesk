# Khung ứng dụng và quyền giao diện — frontend bước 3

Đã rà soát lại bước 2: bộ 114 tests auth/nền tảng đạt trước khi triển khai; sau tích hợp khung ứng dụng, bộ 133 tests đạt và browser smoke login/password/logout vẫn chạy được. Bước 3 có sidebar/header, layout public/private, permission guards, trang 403/404 và xử lý keyboard/focus. Không thay đổi API nghiệp vụ, database, JWT transport hoặc phiên bản package.

## Giao diện hiện có

- Chưa đăng nhập: header public, trang chủ/login/403/404. URL cần phiên sẽ chuyển về login với returnTo nội bộ đã kiểm tra.
- Đăng nhập xong: `ApplicationLayout` có sidebar desktop, header tên người dùng và toàn bộ role hiện tại, liên kết tài khoản và logout. Không có thao tác chọn một role để loại bỏ quyền từ các role khác.
- Mobile dưới 900px: sidebar thành dialog, có nút menu với aria-expanded/aria-controls. Escape/nút đóng trả focus về nút mở; chọn trang đóng menu và đưa focus vào nội dung mới. Đổi breakpoint hoặc chuyển route cũng đóng menu, khôi phục cuộn nền.
- Link đang mở có aria-current. Skip link đi thẳng đến main. Mỗi trang có document title riêng; chuyển route tập trung focus main, không tự giành focus ở lần tải trang đầu tiên.
- `/403` có thông báo chung không tiết lộ resource; URL không tồn tại hiển thị 404. Route error boundary xử lý 403/404 và hiển thị traceId đã chuẩn hóa nếu có, không render stack trace hoặc raw exception.

RootLayout chọn PublicLayout/ApplicationLayout theo phiên và route. Router lồng RequireSession, RequirePermission và Outlet; CurrentProfileBoundary nằm ở vùng nội dung private. Header logout vẫn dùng được khi profile đang tải hoặc bị lỗi.

`<title>` dùng metadata của React 19, được đưa vào document head; xem [React title](https://react.dev/reference/react-dom/components/title).

## Menu và permission

Nguồn permission là `/users/me` hoặc profile trong response refresh, không phải role/permission tự đọc từ JWT. `features/auth/lib/permissions.ts` ánh xạ 17 code backend; test so trực tiếp với `be/Domain/Constants/PermissionCodes.cs` để phát hiện lệch contract.

| Nhóm menu | Quyền |
| --- | --- |
| Trang chủ, tài khoản, bảo mật | Có phiên đã xác nhận |
| Danh sách Ticket, dashboard, báo cáo | Một trong ticket.read.own / ticket.read.team / ticket.read.all |
| Tạo Ticket | ticket.create; không bắt buộc thêm read permission chỉ để mở form |
| User | user.manage |
| Role/grant | role.manage; không suy từ user.manage |
| Department | department.manage |
| SupportTeam | team.manage |
| Category | category.manage |
| SLA policy | ticket.manage.sla; không tự cấp quyền báo cáo/Ticket read |
| Notification | Có phiên đã xác nhận |

`hasPermissions` hỗ trợ anyOf/allOf, có thể kết hợp hai điều kiện; danh sách policy rỗng được gửi rõ ràng sẽ fail closed. Không có bypass theo tên SystemAdmin/TeamLeader. Giao diện dùng union permission server trả về cho user nhiều role; backend tiếp tục kiểm tra permission và scope từng resource.

Menu và route guard dùng chung catalog/rule, tránh trường hợp ẩn menu nhưng deep link lại bỏ qua policy. Các module Ticket/quản trị/notification/report **chưa có trang nghiệp vụ frontend** được đánh dấu **Sắp có**, không có href hoặc dữ liệu mô phỏng. Những URL đã dành cho module này được guard trước: thiếu permission trả UI 403; có permission vẫn 404 đến khi trang thật được triển khai. Đây không phải API đọc Ticket và không thay thế ACL backend.

Để thêm feature ở bước sau: triển khai page/API/hook, thay route 404 dự phòng bằng page thật bên trong policy hiện có, rồi đổi availability của menu. Kiểm tra action permission và scope tài nguyên riêng ở feature; việc menu được hiện không cho phép tự bỏ kiểm tra requester/team/internal ở backend.

Hiện route không dùng data loader. Nếu bổ sung loader sau này, phải kiểm tra phiên trước khi fetch trong loader; element guard không ngăn router chạy loader trước khi render.

## Không hiển thị dữ liệu trước xác thực

- RequireSession không mount nội dung khi login/profile confirmation chưa hoàn tất.
- CurrentProfileBoundary chặn cả nội dung và permission guard khi profilePending, profileError hoặc chưa có user. Sidebar cũng không giữ lại các mục theo grant cũ trong lúc đó; header không hiển thị role chưa được xác nhận. Kiểm tra nền do focus/online giữ profile đã xác nhận trong lúc chờ để không xóa form đang nhập; khi có kết quả đổi grant hoặc lỗi, guard/sidebar đánh giá lại ngay. Nếu trước đó profile đã lỗi thì retry vẫn chặn nội dung đến khi xác nhận thành công.
- Nếu `/me` lỗi, có ErrorState/traceId/retry; không quay về dữ liệu cũ để coi như đã xác thực. Logout vẫn khả dụng. Khi mất hiệu lực phiên, session manager của bước 2 xóa credential/cache và guard trở về login.
- Khi `/me` thay đổi grant/role, scope/cache được cập nhật theo bước 2; sidebar và guard đánh giá lại ngay, kể cả giữ nguyên URL. Một SystemAdmin đã bị gỡ grant không được xem module quản trị chỉ vì tên role.

Browser không phải nguồn authorization cuối cùng. Group membership, tài khoản active, phân công và internal visibility vẫn phải được kiểm tra trên API mỗi lần truy cập tài nguyên.

## Thử tương thích SignalR JS 10 / server 8

Đã thêm phép thử tích hợp có thể chạy lại, **không tạo kết nối SignalR tự động trong UI** và chưa triển khai inbox/realtime của bước 12–13.

```powershell
# Cài dependency theo lockfile nếu chưa có
cd fe
npm ci
cd ..
./be/tests/Run-SignalRCompatibilityTests.ps1
```

Runner tạo PostgreSQL 16 với database `helpdesk_mapping_test_signalr_<GUID>`, port/password ngẫu nhiên, không mount volume ứng dụng. Fixture áp dụng migrations vào DB test trống, tạo user/Ticket test và chạy NotificationsHub thật của ứng dụng .NET 8. Cuối lượt dọn container test; không dùng `.env`, tài khoản hoặc database Helpdesk thật.

Do TestServer không có TCP port, test mở Kestrel bridge chỉ trên loopback với port OS cấp, chuyển HTTP/LongPolling nguyên trạng vào pipeline ứng dụng thật. Node chạy [signalr-compatibility.mjs](../fe/scripts/signalr-compatibility.mjs), dùng `@microsoft/signalr` từ `fe/node_modules`. Credential test đi qua stdin, không nằm trên argv hoặc output; SDK logging tắt để tránh URL access_token xuất hiện trong lỗi.

Đã xác nhận với **JS 10.0.11**, hub assembly/runtime **.NET 8** và transport **LongPolling**:

1. Anonymous negotiate bị 401; login và `/me` dùng JWT thật.
2. Negotiate/handshake thành công, nhận NotificationsChanged và gọi Ping.
3. WatchTicket hợp lệ được chấp nhận, Ticket ngoài scope bị chặn; UnwatchTicket hoạt động.
4. Query access_token không xác thực được REST.
5. Sau refresh, JWT cũ bị từ chối; connection stop/start lấy token mới và nhận lại sự kiện ban đầu.
6. Sau logout, REST và hub negotiate/start bằng JWT đã revoke đều bị từ chối.

Kết quả: **1 integration test đạt, 0 skipped**. Chưa kiểm thử WebSocket/SSE, browser cross-origin hoặc reverse proxy production; không suy những kết quả đó từ LongPolling qua bridge. Chưa nghiệm thu delivery notification, watch lifecycle trong SPA hoặc reconnect/backoff của bước realtime. [Microsoft mô tả tương thích client/server được hỗ trợ](https://learn.microsoft.com/en-us/aspnet/core/signalr/client-features?view=aspnetcore-8.0); kết luận của repo dựa trên phép thử thực tế ở trên.

CI backend đã thêm Node 24 và `npm ci --prefix fe` trước build/test để test JS này chạy cùng suite PostgreSQL. Không cần GitHub secret mới. Khi chạy full `Run-PersistenceTests.ps1`, cần Node và `npm ci` trong fe trước; các filter backend không chọn test JS vẫn có thể chạy riêng. Workflow GitHub từ xa chưa được chạy trong lượt local này.

## Kiểm tra chất lượng

```powershell
cd fe
npm run lint
npm test
npm run build
```

- **133 frontend tests / 17 files đạt**, gồm 114 tests đã có và 19 tests mới về permission catalog/parity, multi-role, không bypass admin, user/role grant tách biệt, read scope OR, guard không mount child trước profile, navigation/403/404, revalidation/error, giữ draft khi kiểm tra nền và menu/focus.
- Backend Release `-warnaserror` đạt 0 warning/error; target/package vẫn .NET/EF/Npgsql 8.x. Chỉ thêm test/harness, không sửa nghiệp vụ backend hoặc tạo migration.
- Edge headless desktop/mobile với API fixtures: sidebar/breakpoint, menu modal, Escape/focus/scroll restore, title/focus khi điều hướng, thay grant trên URL đang mở, profile lỗi, 403/404. Chạy lại smoke auth của bước 2; không có page error hoặc overflow ngang trong các màn kiểm tra.
- Lượt này không chạy lại toàn bộ persistence suite; đã chạy riêng integration SignalR mới trên PostgreSQL thật. Không thay mật khẩu hay ghi dữ liệu lên DB Helpdesk để kiểm tra UI.

Bước tiếp theo: [bước 4 — danh sách và chi tiết Ticket](frontend-roadmap.md), dùng authenticated client, scope/query keys và policy đã chuẩn bị.
