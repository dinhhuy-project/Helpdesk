# Tạo Ticket — frontend bước 5

Đã rà soát bước 4 bằng toàn bộ 177 frontend tests trước khi triển khai. Bước 5 mở `/tickets/new` và tích hợp API tạo Ticket/lookup danh mục hiện có. Không đổi package, contract backend, database hoặc biến môi trường.

## Luồng tạo yêu cầu

- Sidebar có liên kết **Tạo yêu cầu** cho `ticket.create`; danh sách có cùng CTA nếu được cấp quyền. Khi mở form, chỉ mục tạo active; khi mở detail, mục danh sách active.
- Route tạo nằm ngoài guard đọc Ticket. Không có phiên thì login với returnTo, thiếu `ticket.create` thì 403 trước khi tải lookup. Role SystemAdmin hay read.all không tự cấp quyền tạo.
- Form gồm title, description, priority và category. Không có field chọn requester/creator, TicketNumber, status, assignment hoặc version.
- Title bắt buộc/tối đa 300 ký tự, description bắt buộc/tối đa 20.000 ký tự; không chấp nhận toàn whitespace. Kiểm tra độ dài trước trim rồi gửi text đã trim. Priority JSON là số 1..4, mặc định Normal=2; CategoryId phải là UUID khác empty.
- Lỗi required hiển thị tại field và được xóa khi chỉnh sửa; lỗi ValidationProblemDetails từ server được map không phân biệt hoa/thường, hỗ trợ đường dẫn `$.field`, giữ traceId. Không render nội dung Ticket dưới dạng HTML.
- Khi đang gửi, khóa control và submit; có khóa đồng bộ để ngăn hai submit liên tiếp trước khi React render. Draft chỉ ở memory của form, không persist browser storage/Zustand hoặc mutation cache.

## Lookup danh mục theo từng cấp

Chỉ gọi `GET /api/v1/ticket-categories/lookup` bằng authenticated client và quyền `ticket.create`; không gọi list/detail quản trị `category.manage`.

| Thao tác | Query |
| --- | --- |
| Mở gốc | `rootsOnly=true&page=1&pageSize=20` |
| Mở nhánh có `hasChildren=true` | `parentId={id}&rootsOnly=false&page=1&pageSize=20` |
| Chuyển trang | Giữ parent/search, đổi page |
| Tìm trong cấp đang mở | Giữ parent/rootsOnly, thêm search, đưa về page 1 |
| Chọn breadcrumb | Quay về cấp đó, xóa search và về page 1 |

Picker dùng radio chọn danh mục, nút **Mở nhánh** và breadcrumb. Chọn node và mở node là hai thao tác riêng; được chọn cả node cha lẫn lá còn khả dụng theo backend. Không tải đệ quy cả cây, không suy `hasChildren` từ số item của trang hiện tại và không coi trang đầu là toàn bộ danh mục.

Lựa chọn hiện tại giữ ID/đường dẫn khi đổi trang hoặc duyệt nhánh khác. Bấm Enter tại ô search chỉ tìm danh mục, không submit Ticket. Có loading, empty, error/traceId và retry lookup; query key phân biệt scope phiên, parent, search và page, truyền AbortSignal để loại response nhánh cũ về muộn.

Lookup chỉ trả node khả dụng cùng ancestor khả dụng. Kết quả đã chọn có thể cũ: backend vẫn kiểm tra lại khi tạo. Nếu trả `400 InvalidCategory`, form giữ text/priority, bỏ lựa chọn danh mục, yêu cầu chọn lại và invalidate lookup đang dùng.

## Contract ghi và xác nhận thành công

`POST /api/v1/tickets`, JSON chỉ có bốn field:

```json
{
  "title": "Không kết nối được VPN",
  "description": "VPN báo lỗi sau khi nhập tài khoản.",
  "categoryId": "<UUID từ lookup>",
  "priority": 2
}
```

Typed API dựng body rõ ràng, không spread dữ liệu form hoặc profile vào request. RequesterId/CreatedByUserId do server lấy từ phiên; PostgreSQL sinh TicketNumber, backend ghi activity cùng transaction. Response 201 là `TicketCreated`: id, ticketNumber, requesterId, createdByUserId, categoryId, status, priority, createdAt, version.

Sau khi nhận receipt có ID hợp lệ, invalidate prefix danh sách Ticket của **scope phiên hiện tại** với `refetchType: 'none'`, rồi mở `/tickets/{id}` bằng replace navigation. Trang detail tiếp tục tự gọi access/detail/activity và kiểm tra ACL như bước 4; không đưa receipt vào detail cache để bỏ qua access. Lỗi GET sau 201 không được coi là lỗi tạo và không gửi lại POST.

Nếu tài khoản chỉ có `ticket.create` và chưa có read.own/team/all, giữ trang xác nhận **Tạo yêu cầu thành công** với mã server cấp, giải thích thiếu quyền đọc và có nút tạo yêu cầu khác. Không tự thêm permission hoặc đưa người dùng vào 403 như thể việc tạo thất bại.

Receipt/draft được gắn với scope phiên bằng component key. Request bị hủy khi unmount; response cũ sau logout/đổi tài khoản không điều hướng, không hiện receipt hoặc đổ dữ liệu vào cache của người mới. Hủy request không cam kết hủy transaction đã commit trên server.

## Kết quả chưa rõ và retry

Command không dùng retry tự động của Query/mutation, không retry network/timeout/5xx. Auth transport vẫn chỉ được replay một lần khi nhận **401 xác định** theo cơ chế refresh ở bước 2; không replay khi response bị mất.

- Với timeout, lỗi mạng, 5xx, cancellation còn trong phiên hoặc receipt không có ID hợp lệ: giữ draft, báo **Chưa xác định được kết quả tạo** và khóa submit. Ticket có thể đã được commit.
- Người có quyền đọc có liên kết kiểm tra danh sách; tài khoản chỉ có quyền tạo được hướng dẫn kiểm tra với hỗ trợ. Nút **Tôi đã kiểm tra, cho phép gửi lại** chỉ mở lại submit, không tự gửi POST. Người dùng cần chủ động bấm Gửi yêu cầu lần nữa.
- 400/403/409 hiển thị lỗi và giữ draft, không tự refresh/retry. 429 giữ Retry-After, đếm thời gian chờ và chỉ cho phép retry thủ công khi hết hạn.
- Backend chưa có idempotency key cho create; kiểm tra thủ công không bảo đảm tuyệt đối chống trùng nếu response đã mất. Không tự tìm bản ghi theo title rồi coi đó là xác nhận duy nhất.

## Kiểm chứng

Từ `fe/`:

```powershell
npm run lint
npm test
npm run build
```

Bộ frontend: **211 tests**. Kiểm thử mới gồm validation/giới hạn DTO, session/permission guards, roots/children/pagination/search, giữ lựa chọn, response nhánh cũ, parent selection, body/danh tính, scope invalidation, double submit, 201/detail, create-only receipt, server validation, InvalidCategory, timeout/network/503 không retry, Retry-After, logout/late receipt và profile revalidation không làm mất draft.

Đã chạy **55 integration tests** backend trên PostgreSQL thật trong container test riêng, không đụng database Helpdesk:

```powershell
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~TicketCreationTests|FullyQualifiedName~TicketCategoryAdministrationTests'
```

Các test server xác minh danh tính từ phiên, number database, activity/transaction, category/ancestor availability, quyền tạo độc lập quyền đọc và lookup/admin ACL. Frontend fixtures kiểm tra cách client sử dụng contract, không thay thế kiểm chứng ACL server.

Edge headless smoke ở desktop 1440px/mobile 390px với API fixtures đạt: login/deep link, validation, phân trang gốc, mở nhánh, giữ lựa chọn, chọn cha/lá, body chính xác, 201 → detail, create-only receipt, lỗi mạng không tự POST lại, draft còn nguyên và không tràn ngang. Không có lỗi JavaScript. Chưa đăng nhập tài khoản nghiệp vụ để nghiệm thu trực tiếp trên database Helpdesk thực tế.

## Chạy thử local

1. Chạy API Container port 5000 với database đã migrate, sau đó `npm run dev` từ `fe/`.
2. Login bằng tài khoản có `ticket.create`, mở `/tickets/new`, chọn danh mục và gửi. Nếu có read.own thì mở detail; chỉ create thì hiển thị receipt.
3. Thử danh mục có nhiều hơn 20 node cùng cấp, danh mục con và danh mục cha. Thử deactivate node/ancestor sau lúc chọn để xác nhận InvalidCategory giữ draft và yêu cầu chọn lại.
4. Dùng Network để kiểm tra POST chỉ có bốn field, không gửi lại tự động khi mất kết nối. Nếu kết quả chưa rõ, kiểm tra danh sách trước khi chủ động cho phép gửi lại.

Không cần migration, package hoặc env mới. [Bước 6 — Comment, internal note và activity](frontend-ticket-discussion.md) đã bổ sung trao đổi và nghiệm thu hai tài khoản; các bước tiếp theo theo [lộ trình frontend](frontend-roadmap.md).
