# Danh mục Ticket

Phần 9 triển khai cây TicketCategory, CRUD quản trị với deactivate, lookup cho form tạo Ticket và kiểm tra parent/vòng lặp. Phần 8 đã được người dùng xác nhận review/apply. Code mới cần [TicketCategoryAdministration](ticket-category-administration-review.md) trước khi F5; không có package hoặc secret mới.

## API và permission

| API | Permission | Thành công |
| --- | --- | --- |
| `GET /api/v1/ticket-categories` | `category.manage` | 200, danh sách quản trị có phân trang |
| `GET /api/v1/ticket-categories/{id}` | `category.manage` | 200, chi tiết/version; không có trả 404 |
| `POST /api/v1/ticket-categories` | `category.manage` | 201 + Location, mặc định active |
| `PATCH /api/v1/ticket-categories/{id}` | `category.manage` | 200, sửa tên/mô tả/parent với version hiện tại |
| `PUT /api/v1/ticket-categories/{id}/status` | `category.manage` | 200, activate/deactivate với version hiện tại |
| `GET /api/v1/ticket-categories/lookup` | `ticket.create` | 200, chỉ danh mục khả dụng cho Ticket mới |

Permission quản trị riêng `category.manage` không bao gồm user/role/department/team và cũng không tự bao gồm `ticket.create`. Có thể ủy quyền quản lý catalog toàn doanh nghiệp bằng grant này, không bắt buộc tên role SystemAdmin. Chỉ `ticket.create` cho phép lookup, không cho xem metadata quản trị hoặc sửa catalog. Requester mặc định có ticket.create. Thiếu/sai JWT, phiên hết hạn/thu hồi hoặc user inactive trả 401; thiếu grant trả 403. Thao tác ghi kiểm tra lại phiên/user/grant trong transaction; thay đổi grant có hiệu lực với cùng JWT ở lần kiểm tra tiếp theo.

Quản trị trả DTO: `id`, `parentId`, `name`, `description`, `isActive`, `isAvailable`, `hasChildren`, `createdAt`, `updatedAt`, `version`. Lookup chỉ trả `{id,parentId,name,hasChildren}`; không expose mô tả, version, normalizedName hoặc EF entity. API đặt no-store; Swagger có Bearer, 401/403 và request/response DTO.

## Đọc cây theo từng cấp

Query quản trị: `page=1`, `pageSize=20` (1–100), `search` tối đa 200 ký tự, `parentId`, `rootsOnly=false`, `isActive`, `sortBy=name|createdAt`, `descending=false`. Lookup nhận `page`, `pageSize`, `search`, `parentId`, `rootsOnly`; luôn lọc khả dụng và sort theo tên. Sort có tie-break ID, search tên không phân biệt hoa/thường và coi `%`/`_` là text thông thường. Response trang: `{items,page,pageSize,totalCount}`; count và items là query đọc thông thường, không phải snapshot chung.

- `rootsOnly=true`: lấy các node gốc (`parentId=null`).
- `parentId=<id>`: lấy con trực tiếp của node đó.
- Không gửi hai field trên: danh sách phẳng mọi cấp, vẫn có parentId để nhận diện cấu trúc.
- Gửi đồng thời rootsOnly=true và parentId, hoặc Guid.Empty, trả 400. Parent filter không tồn tại chỉ cho trang rỗng.

Ví dụ duyệt cây cho form tạo Ticket:

```http
GET /api/v1/ticket-categories/lookup?rootsOnly=true&pageSize=20
GET /api/v1/ticket-categories/lookup?parentId=<root-id>&page=1&pageSize=20
```

Client dùng `hasChildren` để mở từng node và lấy thêm trang khi cần. HasChildren của quản trị bao gồm con inactive; lookup chỉ tính con khả dụng. Không trả toàn bộ cây nested không giới hạn trong một response. Bước này cho phép chọn cả node cha và node lá nếu khả dụng; chưa có quy tắc chỉ chọn leaf.

## Tạo, đổi parent và tên trùng

```json
{ "name": "Máy tính", "description": "Thiết bị văn phòng", "parentId": null }
```

Tên bắt buộc, không toàn whitespace, tối đa 200 ký tự trước trim; API bỏ dấu cách U+0020 hai đầu. Mô tả tối đa 2.000 ký tự, trim, rỗng thành null. Parent vắng/null khi tạo là node gốc. Parent được chọn phải tồn tại và khả dụng: chính parent cùng **mọi ancestor** đều active.

Tên duy nhất giữa các node cùng parent, kể cả inactive. Các node gốc thuộc một phạm vi tên chung. Hai nhánh khác parent được dùng tên giống nhau; một node cha và con có thể cùng tên. Chuẩn hóa bằng database `lower(btrim(name))`, theo collation PostgreSQL; không bỏ dấu tiếng Việt, gộp dấu cách giữa từ, đổi tab thành dấu cách hoặc chuẩn hóa Unicode. Ví dụ ` Hardware ` và `HARDWARE` trùng trong cùng cấp. Hai partial unique index bảo vệ riêng node gốc và con có parent; không dựa vào uniqueness của NULL trong composite index.

```json
{ "name": "Máy tính", "description": null, "parentId": "<new-parent-id>", "version": 123 }
```

PATCH bắt buộc **gửi parentId rõ ràng**, null để đưa node về gốc. Thiếu field này trả 400 để tránh vô tình chuyển cả nhánh khi chỉ định sửa metadata. Bỏ description có nghĩa xóa mô tả. Parent không đổi được giữ lại khi chỉnh metadata lịch sử, kể cả ancestor đã inactive; parent mới phải khả dụng. Đổi parent chuyển cả nhánh theo cấu trúc liên kết hiện có, không đổi ID hay CategoryId của Ticket.

Không chọn chính node hoặc bất kỳ descendant nào, kể cả descendant inactive, làm parent: trả 400 `CategoryCycle`. Không có/mất khả dụng của parent trả 400 `InvalidParent`; tên trùng sau rename/reparent trả 409 `NameInUse` và rollback cả metadata/audit. Thuật toán kiểm tra chuỗi dùng recursive SQL với tập ID distinct, không đệ quy C# theo từng cấp hoặc một query cho mỗi node.

## Deactivate và lịch sử

```json
{ "isActive": false, "version": 123 }
```

`isActive` phải gửi rõ ràng. Deactivate chỉ đổi cờ node được chọn, không cascade sửa cờ các con. Lookup chỉ duyệt từ root active qua những cạnh có node con active; vì vậy node active dưới một ancestor inactive vẫn bị loại. Quản trị vẫn thấy toàn bộ dữ liệu và phân biệt `isActive` (cờ riêng) với `isAvailable` (cả đường lên gốc active).

Activate yêu cầu parent/ancestors khả dụng. Activate lại ancestor làm những descendant vốn giữ cờ active xuất hiện trở lại; node đã bị deactivate riêng vẫn không xuất hiện. Có thể chuyển một nhánh active khỏi ancestor inactive sang parent active hoặc về gốc để dùng lại. Không auto sửa các cờ descendant hoặc phiên đăng nhập.

Giữ nguyên Ticket.CategoryId, dữ liệu và version Ticket; không tự chuyển Ticket cũ sang danh mục khác. Không có DELETE danh mục, FK vẫn Restrict. Bước 11 tạo Ticket phải kiểm tra lại khả dụng trong transaction, dùng cùng quy tắc đồng bộ với thay đổi danh mục; kết quả lookup trước đó không thay thế kiểm tra lúc ghi. Bước này chưa mở API tạo Ticket.

## Concurrency và audit

Version của mỗi danh mục dùng xmin PostgreSQL. Thiếu/0 version trả 400, stale trả 409 `VersionConflict`. No-op với version hiện tại không đổi timestamp/version hoặc ghi audit. Version chỉ phản ánh field của chính node: việc đổi ancestor có thể đổi IsAvailable/đường dẫn của descendant mà version descendant không đổi; client tải lại danh sách/lookup sau thay đổi cây.

Các thay đổi cây, status và quản trị quyền cùng dùng advisory transaction lock `734205001`, rồi khóa user người gọi và row danh mục. Hai thao tác đối nghịch `A.parent=B` và `B.parent=A` được kiểm tra nối tiếp; chỉ một thao tác có thể thành công. Khóa ở PostgreSQL có hiệu lực giữa API instance, không dùng khóa process. Thêm node dưới parent vừa bị deactivate qua transaction khác bị từ chối sau khi đọc trạng thái mới.

FK/check database bảo vệ parent tồn tại và không tự trỏ chính mình; vòng lặp nhiều node được chặn bởi use case cùng transaction lock. SQL writer trực tiếp phải tuân thủ khóa và kiểm tra cây. Lookup vẫn kết thúc và loại những thành phần không nối về root nếu writer ngoài ứng dụng tạo cycle; đây không phải cơ chế sửa dữ liệu tự động.

Audit `TicketCategoryCreated`, `TicketCategoryUpdated`, `TicketCategoryStatusChanged` ghi trong identity.identity_audit_logs cùng transaction. Data whitelist chứa CategoryId, parent ID trước/sau, tên field hoặc cờ trạng thái; không copy tên/mô tả/body tự do. Đọc qua API audit phần 6 với scope quản trị hiện có. Chưa có cache/realtime hoặc UI danh mục; frontend hiện còn starter.

## Bắt đầu sử dụng

Sau review/apply migration mới:

```powershell
dotnet run --project be/be.csproj --no-launch-profile -- seed-identity
dotnet run --project be/be.csproj --no-launch-profile -- grant-category-administration
```

Catalog ở phần 9 có 14 mã; hiện phần 12 bổ sung [ba quyền workflow](backend-ticket-workflow.md), tổng 17. SystemAdmin mới nhận đủ mặc định; role đã tồn tại giữ grant cũ. Lệnh grant riêng bổ sung đúng category.manage vào SystemAdmin, bump version role và audit CategoryAdministrationEnabled nếu thực sự thêm; lặp an toàn, không đổi user/password/grant khác. Operator có thể dùng API quản trị grant phần 6 thay lệnh này. Startup chỉ seed/bootstrap theo cấu hình hiện có, không tự migrate hoặc gọi lệnh grant. Profile Container (Dockerfile) giữ port 5000.

## Kiểm tra

```powershell
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Suite PostgreSQL bao gồm tree browsing, lookup theo ancestor active, permission âm và mutation recheck, tên trùng root/sibling, rename/reparent rollback, parent sai/cycle, hai move đối nghịch, stale version, deactivate bảo toàn Ticket, dữ liệu SQL có cycle và migration apply/replay/rollback/upgrade dữ liệu cũ. Script dùng PostgreSQL riêng, có smoke test CLI seed/grant và không mount volume Helpdesk.
