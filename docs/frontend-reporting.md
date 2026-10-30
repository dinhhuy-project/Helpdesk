# Frontend bước 14 — Dashboard và báo cáo Ticket

Đã triển khai `/dashboard` và `/reports/tickets` bằng contract hiện có trong [ReportsController](../be/Controllers/ReportsController.cs) và [Reporting](backend-reporting.md). Không thêm package, permission, endpoint, migration hoặc cấu hình môi trường.

## Màn hình và bộ lọc

- Menu **Tổng quan** và **Báo cáo** mở khi `/users/me` hiện tại có ít nhất một quyền `ticket.read.own`, `ticket.read.team`, `ticket.read.all`. Role chỉ là nhãn; `user.manage` không cấp quyền báo cáo. Guard chờ xác thực/profile, backend tiếp tục áp dụng ACL trước aggregation.
- Dashboard lấy tổng Ticket, đang mở/đã giải quyết/đã đóng/đã hủy, chưa có nhóm/người xử lý; đủ bảy trạng thái và bốn priority kể cả count bằng 0. Các biểu đồ thanh hiển thị count API; không suy số liệu từ các trang Ticket.
- Báo cáo bổ sung bảng theo team hoặc agent, `page`, `pageSize`, `sortBy=total|open|slaBreached`, `descending`. Server thực hiện group/sort/page. `totalCount` là số nhóm có dữ liệu, không phải số Ticket hoặc số user/team toàn tổ chức. Trang không có nhóm vẫn có điều hướng về trang trước.
- Giữ bucket `id=null` là **Chưa phân công**, label inactive lịch sử và các chỉ số SLA null. Không biến bucket null thành UUID hay suy thêm bộ lọc chưa được backend hỗ trợ.
- Bộ lọc chung: `createdFrom`, `createdTo`, `status`, `priority`, `teamId`, `agentId`. Nhập ngày giờ theo timezone trình duyệt; URL/API dùng ISO có timezone, chuẩn hóa UTC. Cần đủ hai mốc, tăng dần, tối đa 366 ngày; khoảng `[from, to)` áp dụng theo `Ticket.CreatedAt`.
- Khi mở không có ngày, lấy khoảng mặc định từ **server**, ghim hai mốc vào URL bằng replace, rồi mới gọi daily/group. **Tải lại** giữ nguyên cohort; **30 ngày mặc định** lấy khoảng mặc định mới. Chuyển dashboard/báo cáo giữ chung URL filter; Back/Forward khôi phục bộ lọc. Đổi cohort, dimension hoặc sort/page size đưa nhóm về trang 1.
- Chọn tên team/agent có trong bảng để lọc sâu; bỏ lọc bằng nút tương ứng. Không gọi directory quản trị, không cấp `team.manage`/`user.manage` để mở báo cáo. UUID hợp lệ trong deep link vẫn do backend kiểm tra; ngoài scope và không có dữ liệu cùng trả kết quả rỗng.

## Ý nghĩa số liệu

- Đây là **trạng thái hiện tại của Ticket được tạo trong khoảng chọn**, không phải lịch sử trạng thái. Team/agent là phân công hiện tại. “Đang mở” gồm New, InProgress, WaitingForRequester, Reopened; resolved/closed/cancelled tách riêng. Count chưa phân công bao gồm cả Ticket đã kết thúc.
- Daily là count Ticket **được tạo theo ngày UTC**, có ngày count 0, không phải số Ticket resolve/ngày. Biểu đồ có bảng số liệu mở bằng bàn phím. Chỉ định dạng phần ngày UTC, không đổi thành ngày local ở múi giờ âm. Ngày đầu/cuối có thể không đủ 24 giờ.
- SLA dùng summary snapshot từ API: with/without SLA, excludedCancelled, eligible, breachedTickets; hai metric first response/resolution hiển thị met/breached/pending/stopped/unknown/measured/durationSamples/averageMinutes.
- `metPercent=null` hoặc `averageMinutes=null` hiển thị **Chưa có mẫu đo**; giá trị `0` vẫn là 0%/0 phút. Không tự tính tỷ lệ bằng total Ticket, không coi pending/stopped/unknown/without SLA là đạt. Mẫu thời lượng và mẫu đánh giá có denominator riêng.
- Mọi request trong một lượt dùng cùng cohort/ACL/filter. Mỗi panel ghi `asOf` riêng; các endpoint không có transaction snapshot chung nên có thể lệch tạm khi có cập nhật đồng thời. Với `createdTo` trong tương lai, server chỉ có dữ liệu đến `asOf`.

## Cache, lỗi và cập nhật

- Query bundle theo scope phiên + bộ lọc chuẩn hóa, thêm dimension/page/sort cho báo cáo. Chỉ công bố bundle khi toàn bộ endpoint thành công. Loading/refetch/error ẩn các panel cũ, không đổi lỗi thành count 0; dữ liệu về muộn của cohort/phiên cũ bị hủy, query riêng tư `gcTime=0`.
- 403 hiển thị mất quyền; 409 thông báo scope thay đổi trong lúc tổng hợp và yêu cầu tải lại. Nút tải lại kiểm tra profile hiện tại trước khi invalidate. 429 hiển thị traceId/Retry-After, khóa nút theo thời gian chờ và ngừng polling/focus retry cho lỗi đó. Lỗi mạng có thao tác thử lại qua tải lại số liệu.
- Refetch khi mount, focus/reconnect, mỗi 60 giây ở tab hoạt động và thao tác thủ công. Notification reconciliation/SignalR reconnect invalidate report root; Ticket đang watch thay đổi cũng invalidate báo cáo. Event chỉ là tín hiệu, không cộng/trừ count từ payload. Không phụ thuộc hoàn toàn vào realtime.
- REST commands đã đánh dấu queries stale; khi quay lại báo cáo luôn refetch. Logout/đổi user/quyền hủy request và xóa cache toàn phiên qua session hiện có.
- Form, thanh KPI và chart co theo mobile; bảng rộng nằm trong vùng cuộn nhận focus/bàn phím. Không truy vấn hoặc render danh sách Ticket để cộng tổng toàn hệ thống.

## Kiểm thử

```powershell
cd fe
npm test
npm run lint
npm run build
cd ..
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~ReportingTests|FullyQualifiedName~FrontendReportingJourneyTests|FullyQualifiedName~FrontendRealtimeJourneyTests'
```

- FE kiểm tra guard/profile, bộ lọc lỗi và duplicate, timezone/URL, bootstrap khoảng server, Back/Forward/drill-down, page/sort server, zero/null/unassigned/inactive, lỗi/traceId/429, hủy request cũ, mất quyền/logout và realtime invalidation.
- Lượt kiểm tra cuối: **477 frontend tests / 33 files đạt**, `npm run lint` và `npm run build` đạt. Browser Edge với API/database fixture đạt trên desktop và mobile 390px, timezone America/New_York, kể cả mất membership; không có page/console error ngoài hub bị chặn có chủ đích.
- `FrontendReportingJourneyTests` chạy typed client TypeScript thật qua Node/Vite SSR với application + PostgreSQL riêng cho test. So dashboard với **server `totalCount` cùng cohort** và daily, kiểm tra own/team/all, negative filter ngoài scope, phân trang nhóm null/inactive, SLA 50%/10–20 phút/null, mốc cuối exclusive và mất membership dù permission strings không đổi. Không cộng các trang Ticket. Backend job CI hiện có chạy journey này, không cần secret/job mới.
- Nhóm kiểm thử tích hợp ở lệnh trên đã đạt **19 tests**, bao gồm chạy lại realtime JS SignalR 10/server 8 của bước 13; runner cũng kiểm tra CLI seed/grant trên database test. Không chạy lại toàn bộ backend suite hoặc EXPLAIN trong thay đổi frontend này; xem [query/index review backend](report-query-review.md).

Browser mode local dùng Playwright/Edge đã cài ngoài repo; không thêm dependency vào ứng dụng:

```powershell
# Terminal 1
cd fe
npm run dev

# Terminal 2, tại root; sửa đường dẫn module Playwright theo máy
$env:HELPDESK_PLAYWRIGHT_MODULE = 'C:/path/to/node_modules/playwright/index.mjs'
$env:HELPDESK_FRONTEND_URL = 'http://localhost:5173'
$env:HELPDESK_BROWSER_CHANNEL = 'msedge'
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~FrontendReportingJourneyTests'
```

Nhánh browser kiểm tra cùng API/database fixture, nhiều tài khoản, timezone America/New_York, điều hướng bàn phím, ngày UTC, bộ lọc nhóm, bảng cuộn 390px và membership bị thu hồi khi report đang mở. Nhánh này cố ý ngắt hub để nghiệm thu REST độc lập; Bước 14 ban đầu chỉ chạy typed client/API trên CI; [bước 15](frontend-release-acceptance.md) đã thêm Chromium và chạy cả 9 browser journeys trên production bundle. Realtime thật được nghiệm thu riêng ở [bước 13](frontend-realtime.md).

[Bước 15: nghiệm thu toàn luồng và chuẩn bị phát hành](frontend-release-acceptance.md) đã có runner browser/CI và kiểm tra image HTTPS; CSV/Excel, lịch sử assignment/trạng thái và dashboard toàn tổ chức cho người thiếu quyền nằm ngoài contract MVP.
