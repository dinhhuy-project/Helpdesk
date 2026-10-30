# Danh sách và chi tiết Ticket — frontend bước 4

Đã kiểm tra nền tảng bước 3 bằng bộ 133 tests trước khi triển khai. Bước 4 mở `/tickets` và `/tickets/:id`, tích hợp API thật bằng authenticated client của phiên; không đổi package, schema hoặc contract backend.

## Màn hình và bộ lọc

- Sidebar mở liên kết **Danh sách yêu cầu**, giữ active khi xem detail. `/tickets/new` là route riêng theo `ticket.create`, đã mở ở [bước 5](frontend-create-ticket.md); quyền tạo không bị gộp với quyền đọc.
- Danh sách có tiêu đề, mã `HD-000042`, trạng thái, độ ưu tiên, tình trạng phân công và ngày tạo. Nội dung mô tả chỉ được lấy qua detail.
- Form tìm trong **tiêu đề**, tìm số Ticket chính xác, trạng thái, priority, requester là chính mình, agent là chính mình, đã/chưa có agent, khoảng ngày tạo, sort và page size. “Được giao cho tôi” chỉ thêm bộ lọc, không cấp quyền đọc.
- Sort hỗ trợ `createdAt`, `updatedAt`, `ticketNumber`, `priority`; có tăng/giảm dần. Mọi filter, sort, count và pagination đều do backend thực hiện; frontend không tự lọc Ticket theo ID hoặc role.
- URL lưu toàn bộ bộ lọc và trang. Áp dụng bộ lọc/sort/page size đưa về trang 1; Back/Forward khôi phục form và truy vấn. Link detail giữ URL danh sách qua navigation state để quay lại; link detail trực tiếp dùng `/tickets` làm điểm quay về.
- URL hỗ trợ cả `teamId`, `categoryId`, `requesterId`, `agentId` theo contract. Bộ lọc team/category từ liên kết được giữ khi sửa các bộ lọc khác và có thông báo; **Xóa bộ lọc** xóa toàn bộ. Chưa có combobox team/agent/category theo scope đọc: không gọi API quản trị để lách permission và không yêu cầu nhập UUID thủ công.
- Khoảng thời gian là `[createdFrom, createdTo)`: form dùng giờ thiết bị, gửi ISO UTC. URL nhận ISO có `Z` hoặc offset, chuẩn hóa UTC; offset `+` phải được URL-encode. Search trim, tối đa 200 ký tự, không tìm mã bằng trường search.
- Validation URL chặn page ngoài `1..1.000.000`, pageSize ngoài `1..100`, enum/sort/bool/GUID sai, giá trị lặp, số Ticket ngoài positive Int64 và khoảng ngày đảo ngược. URL sai hiển thị thông báo/xóa bộ lọc, không âm thầm bỏ điều kiện rồi tải danh sách rộng hơn.
- Trang ngoài phạm vi kết quả vẫn giữ page/count của API, có empty state và nút về trang trước. Không tự đổi page khi server trả rỗng.

`ticketNumber` trong query được giữ bằng string để không làm tròn Int64. Response backend hiện trả JSON number: giao diện chỉ format mã khi `Number.isSafeInteger`, dùng nhãn “Mã yêu cầu” nếu vượt giới hạn JavaScript; điều hướng vẫn bằng UUID. Nếu cần hiển thị chính xác mã lớn hơn `2^53-1`, cần chốt thay đổi wire contract sang string ở cả backend/frontend.

## Ba tài nguyên riêng

| Tài nguyên | API | Cách sử dụng |
| --- | --- | --- |
| Danh sách | `GET /api/v1/tickets` | `PagedResult<TicketSummary>`, không chứa description/activity |
| Quyền tài nguyên | `GET /api/v1/tickets/{id}/access` | Lấy trước; chỉ mount detail khi `canRead=true` |
| Chi tiết | `GET /api/v1/tickets/{id}` | `TicketDetails`; giữ `version` server trả, không tự tăng |
| Hoạt động | `GET /api/v1/tickets/{id}/activities` | Request/cache/pagination riêng; pageSize 20, mới nhất trước |

Detail hiển thị mô tả, resolution summary nếu có, trạng thái/priority, mốc thời gian và tình trạng phân công. DTO detail chỉ có ID requester/category/team/agent: dùng “Bạn” khi trùng danh tính phiên, trạng thái phân công và mục thu gọn **Mã tham chiếu**. [Bước 9](frontend-ticket-workflow.md) bổ sung projection workflow riêng với tên team/agent hiện tại theo read scope. Không bịa tên hoặc lấy danh sách user quản trị.

Activity dùng `activityPage` trên URL, độc lập với page của danh sách. API tự lọc public/internal và count theo ACL hiện tại; client không gửi `isInternal=true` để thăm dò quyền. Giao diện chỉ trình bày action đã biết, thời gian, actor “Bạn”/“Người dùng”/“Hệ thống”, chuyển trạng thái, public resolution summary hoặc hạn SLA được projection cho phép. Không dump raw JSON, không lấy body comment qua activity. Chưa có form comment/internal note ở bước này.

Các cờ `canAssign`/`canUpdateStatus` thuộc access response, không được xem là quyền đọc hoặc đầy đủ mọi transition. [Bước 9](frontend-ticket-workflow.md) dùng context/lookup riêng từ backend để mở phân công và workflow cùng version. Không có bypass theo tên role SystemAdmin, assignee hoặc role TeamLeader.

## ACL, cache và trạng thái lỗi

- Route list/detail yêu cầu phiên đã xác nhận và ít nhất một `ticket.read.own/team/all`. Backend quyết định scope cuối cùng; client giữ nguyên tập kết quả và count trả về.
- GUID sai/empty bị chặn trước request. Access/detail/activity trả 404 dùng thông báo chung “không tồn tại hoặc không có quyền”; 403 có trang từ chối quyền, không retry refresh token. 401 dùng cơ chế refresh/logout đã có ở bước 2.
- Access đang tải/tải lại thì detail và activity unmount, dữ liệu cũ bị loại khỏi cache ngay khi không còn observer (`gcTime: 0`). Chỉ mount lại sau quyết định mới. Không flash nội dung cũ khi chuyển ID hoặc revalidate access.
- List/detail/access/activity có query key chứa scope phiên và đầy đủ filter/page/ID/visibility liên quan, không chứa token. Scope đổi/logout hủy request và clear cache theo nền tảng auth. Không dùng placeholder dữ liệu của filter/Ticket trước.
- Loading/refetch che dữ liệu cũ; lỗi detail che toàn bộ nội dung. Activity lỗi dịch vụ có panel retry riêng; activity bị 403/404 cũng che detail đang hiển thị. Window visibility/reconnect refetch theo QueryClient; chưa có subscription SignalR của trang Ticket ở bước 4.
- ErrorState giữ ProblemDetails, validation message, traceId và Retry-After. Đã sửa parser Retry-After để đọc header không phân biệt hoa/thường. Query không tự retry 400/401/403/404/429.
- Title/description/resolution/activity được render thành text React; không dùng HTML từ người dùng. Table có vùng cuộn bàn phím trên mobile, form có label, mỗi trang có title và main focus theo shell.

## Kiểm chứng

Từ `fe/`:

```powershell
npm run lint
npm test
npm run build
```

Bộ frontend có 177 tests đạt, gồm URL/validation, requester/team/all bằng API fixture, Bearer/cancellation, reset/restore bộ lọc, phân trang server, direct link/access gating, không suy quyền từ role/assignee, thu hồi grant/access và clear cache, request cũ về muộn, dữ liệu text an toàn, activity tách biệt và lỗi 403/404/429.

Frontend fixture kiểm tra hành vi client, không thay thế chứng minh ACL server. Đã chạy 76 integration tests có PostgreSQL thật trong container test riêng:

```powershell
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~TicketAccessTests|FullyQualifiedName~TicketQueryTests|FullyQualifiedName~TicketActivityVisibilityTests|FullyQualifiedName~TicketDiscussionTests'
```

Đã smoke bằng Edge headless tại desktop 1440px/mobile 390px với API fixtures: login vào deep link, filter/sort/page size gửi server, thứ tự access → detail → activity, backlink giữ filter, text an toàn, không tràn ngang trang, ngoài scope 404 và mất quyền khi tab hoạt động lại. Không có lỗi JavaScript. Đây là browser test với fixture; chưa đăng nhập tài khoản nghiệp vụ để nghiệm thu trên database Helpdesk thực tế.

Đã chạy lại browser smoke của shell (menu/focus/permission/403/404) và `./be/tests/Run-SignalRCompatibilityTests.ps1`: 1/1 integration test JS SignalR 10 với hub .NET 8 qua LongPolling đạt trên PostgreSQL test riêng. Không triển khai toàn bộ realtime trong trang Ticket ở bước này.

## Chạy thử với API local

1. Khởi chạy backend Visual Studio Container port 5000 với migrations/grants hiện có, rồi `npm run dev` trong `fe/`.
2. Mở `/tickets` hoặc `/tickets/{UUID}`; nếu chưa có phiên sẽ qua login, sau đó trở lại link đã yêu cầu. Token chỉ ở memory nên reload phải login lại.
3. Dùng các tài khoản requester/team/all có dữ liệu và membership khác nhau; kiểm tra tập kết quả/count trong Network và mở link Ticket ngoài scope.
4. Thử đổi URL filter, Back/Forward, trang rỗng, chuyển sang Ticket khác; thay grant/membership ở backend rồi quay lại tab để xác nhận dữ liệu được kiểm tra lại.

Không cần migration, env hoặc package mới. [Bước 5 — Tạo Ticket](frontend-create-ticket.md) đã bổ sung category lookup phân trang và luồng tạo; [bước 6 — thảo luận](frontend-ticket-discussion.md) thêm capability riêng và comment/internal note; [bước 9 — workflow](frontend-ticket-workflow.md) và [bước 10 — file](frontend-attachments.md) bổ sung thao tác theo scope Ticket/comment. Realtime theo các bước sau trong [lộ trình](frontend-roadmap.md).
