# guides.md — AI Coding Guidelines for Helpdesk

> Tài liệu quy tắc bắt buộc dành cho AI coding assistants (ChatGPT, GitHub Copilot, Codex, Cursor và các tác nhân tự động) khi đọc, tạo, sửa, review hoặc refactor code trong repository này.
>
> Phạm vi: toàn bộ monorepo. Đọc file này cùng README.md và code thực tế trước khi sửa. Khi README mô tả tính năng dự kiến nhưng source code chưa có, **source code hiện tại là sự thật về trạng thái triển khai**, README là định hướng nghiệp vụ.

## 0. Chỉ dẫn quan trọng nhất (MUST READ)

1. Đây là **MỘT repository chứa HAI ứng dụng**: **fe/** (React + TypeScript) và **be/** (ASP.NET Core .NET 8). Giữ ranh giới rõ ràng giữa chúng.
2. **KHÔNG tự động tạo một repository mới, chia microservices, thay framework, nâng major version, di chuyển toàn bộ cấu trúc, hoặc viết lại project** nếu không có yêu cầu rõ ràng.
3. **KHÔNG giả định những module được mô tả trong README đã tồn tại.** Luôn đọc source code, package.json, .csproj, migrations và cấu hình hiện tại trước khi code.
4. **KHÔNG đặt business logic, truy vấn database, mã JWT secret, thông tin Redis/MinIO/PostgreSQL hoặc quyền truy cập dữ liệu trong frontend.**
5. **KHÔNG trả trực tiếp EF Core Entities ra API** và không để Controller chứa toàn bộ nghiệp vụ.
6. **Mỗi thay đổi phải nhỏ, có thể kiểm thử, không làm hỏng phần hiện có.** Chỉ sửa các file liên quan đến yêu cầu.
7. **Security là bắt buộc:** Backend kiểm tra cả RBAC lẫn quyền truy cập từng Ticket; không tin dữ liệu hay role do frontend gửi.
8. **AI là tính năng tùy chọn sau MVP**, không được tự đưa AI/LLM vào luồng cốt lõi hoặc thêm dependency trả phí.
9. Nếu thiếu thông tin nghiệp vụ quan trọng, xác định giả định tối thiểu, ghi rõ; không tự chế API, bảng hoặc chức năng như thể đã tồn tại.
10. Trước khi hoàn thành, chạy kiểm tra phù hợp; **không được tuyên bố build/test thành công nếu chưa thực sự chạy**.

## 1. Thông tin dự án và trạng thái thực tế

- Tên: Helpdesk Management System — Hệ thống tiếp nhận và xử lý yêu cầu hỗ trợ.
- Mô hình nghiệp vụ: một doanh nghiệp, nhiều phòng ban và nhóm hỗ trợ; multi-tenant ngoài phạm vi MVP.
- Vai trò: Requester, Support Agent, Team Leader, System Admin; một User có thể có nhiều Role.
- Frontend: React **^19.3.1**, TypeScript, Vite, React Router, TanStack Query, Zustand, Axios, Tailwind CSS 4, @microsoft/signalr.
- Backend: **net8.0**, ASP.NET Core Web API, C#; EF Core **8.x** + Npgsql **8.x** là hướng tích hợp database, không phải dependency chắc chắn đã được cài.
- Hạ tầng mục tiêu: PostgreSQL, Redis, MinIO, SignalR, Docker; AI triển khai sau nếu có thời gian.
- API contract: REST JSON, OpenAPI/Swagger; SignalR chỉ dành cho sự kiện realtime.
- Ngôn ngữ: giải thích/tài liệu bằng tiếng Việt; tên class, method, property, route, biến và tên bảng bằng tiếng Anh dễ hiểu.

### Đã kiểm tra trên nhánh develop khi tạo hướng dẫn

~~~text
/
├── README.md
├── be/
│   ├── be.sln
│   ├── be.csproj
│   ├── Program.cs
│   ├── Controllers/WeatherForecastController.cs
│   └── Dockerfile
└── fe/
    ├── package.json
    ├── vite.config.ts
    ├── eslint.config.js
    └── src/
        ├── App.tsx
        ├── main.tsx
        └── App.css
~~~

Tại thời điểm viết file này, backend và frontend phần lớn còn là **starter templates**. Các thư mục và kiến trúc ở phần dưới là **mục tiêu phát triển**, không phải khẳng định đã tồn tại. Luôn bảo toàn cấu trúc thật và mở rộng dần khi triển khai tính năng.

## 2. Kiến trúc tổng thể bắt buộc

Chọn **Modular Monolith** cho backend và **Feature-Based Architecture** cho frontend.

~~~text
React SPA (fe/)
  ├── routes/pages/features/components
  ├── TanStack Query: server state
  ├── Zustand: client/UI state
  ├── Axios: API transport
  └── SignalR client: realtime events
        │ HTTPS + JWT/cookie (theo cơ chế Auth được chốt)
        ▼
ASP.NET Core Web API (be/)
  ├── Controllers: HTTP boundary + validation/authorization
  ├── Application/Features: use cases + business orchestration
  ├── Domain: entities, enums, domain rules
  ├── Infrastructure: EF Core, repositories/queries, external services
  └── SignalR Hubs: events, không làm nơi xử lý nghiệp vụ
        ├── PostgreSQL: source of truth
        ├── Redis: cache
        └── MinIO: private object storage
~~~

**Luồng dữ liệu chuẩn:**
User interaction → React Page/Feature → typed API client → Controller → Application service/use case → Domain/Infrastructure → PostgreSQL → response DTO → UI. Sau khi database transaction thành công, hệ thống phát sự kiện/notification realtime cho đúng người dùng.

**Không được làm:**
- Frontend truy cập trực tiếp PostgreSQL, Redis hoặc MinIO bằng quyền quản trị.
- UI quyết định quyền cuối cùng của người dùng hoặc trạng thái Ticket hợp lệ.
- Controller gọi EF Core trực tiếp trong mọi endpoint phức tạp rồi trộn nghiệp vụ và HTTP response.
- Hub SignalR thay thế các REST commands, hoặc phát internal notes vào public group.
- Repository layer trả IQueryable qua boundary API/Application.
- Tạo các service/shared package dùng chung C# và TypeScript ở runtime; chỉ chia sẻ **API contract/schema**.

## 3. Quy tắc tổ chức source code

### 3.1. Monorepo: không phá vỡ thư mục hiện tại

- Chỉ đặt code frontend trong **fe/**, code backend trong **be/**; file cấu hình môi trường và tài liệu chung ở root khi cần.
- Không để node_modules, dist, bin, obj, .env, secrets, data volumes hay generated artifacts vào git.
- Khi sửa một API contract, kiểm tra ảnh hưởng đến cả fe/ và be/.
- Không tạo hai định nghĩa khác nhau cho status, priority, permission mà không có ánh xạ rõ ràng.

### 3.2. Backend — đề xuất phát triển từ be/

Trong giai đoạn đầu, có thể giữ một project **be/be.csproj** với các thư mục sau; **không bắt buộc tách thành nhiều .csproj**:

~~~text
be/
├── Controllers/                  # HTTP endpoints (hoặc chuyển vào Features/*/Endpoints theo convention đã chọn)
├── Domain/
│   ├── Entities/
│   ├── Enums/
│   └── Constants/
├── Application/
│   ├── Common/
│   └── Features/
│       ├── Auth/
│       ├── Tickets/
│       ├── SupportTeams/
│       ├── Categories/
│       ├── SLA/
│       └── Notifications/
├── Infrastructure/
│   ├── Persistence/
│   │   ├── HelpdeskDbContext.cs
│   │   ├── Configurations/
│   │   └── Migrations/
│   ├── Authentication/
│   ├── Caching/
│   ├── Storage/
│   └── Realtime/
├── Contracts/
│   ├── Requests/
│   └── Responses/
├── Program.cs
├── appsettings.json
└── be.csproj
~~~

Cấu trúc này là **gợi ý đích**, không được tạo tất cả folder/file rỗng cùng lúc. Một feature chỉ tạo các file thật sự cần thiết.

Quy tắc phụ thuộc logic:
- Domain: không phụ thuộc ASP.NET Core, EF Core, MinIO, Redis, SignalR.
- Application: điều phối use cases, phụ thuộc abstractions, không biết HTTP hoặc lưu trữ cụ thể.
- Infrastructure: triển khai EF Core, Redis, MinIO, token, gửi sự kiện và tích hợp ngoài.
- API/Controllers: chuyển request → use case → response; không viết logic workflow phức tạp.
- Khi tất cả layer ở một assembly, vẫn phải giữ ranh giới bằng namespace, tổ chức folder và code review. Nếu sau này tách Clean Architecture thành nhiều projects, chỉ làm theo yêu cầu riêng.

### 3.3. Frontend — đề xuất phát triển từ fe/src/

~~~text
fe/src/
├── app/
│   ├── router/
│   ├── providers/
│   └── layouts/
├── features/
│   ├── auth/
│   ├── tickets/
│   │   ├── api/
│   │   ├── components/
│   │   ├── hooks/
│   │   ├── pages/
│   │   ├── schemas/
│   │   └── types/
│   ├── teams/
│   ├── notifications/
│   └── dashboard/
├── shared/
│   ├── api/
│   ├── components/
│   ├── hooks/
│   ├── lib/
│   ├── types/
│   └── utils/
├── assets/
├── main.tsx
└── App.tsx
~~~

- **Feature first**: code phục vụ riêng Tickets nằm dưới features/tickets; chỉ đưa vào shared/ khi thật sự dùng chung.
- Tránh tạo những folder components/, hooks/, services/ toàn cục chứa hàng chục module không liên quan.
- UI component nhận props và render; API calls và business orchestration đặt vào hooks/services của feature.
- App.tsx chủ yếu lắp providers/router/layout, không chứa cả ứng dụng.
- Sử dụng naming nhất quán với các file đã có; không đổi tất cả file hiện tại chỉ để thống nhất style.

## 4. Quy tắc ASP.NET Core / C# (.NET 8)

### 4.1. C# coding style

- Sử dụng nullable reference types; không lạm dụng toán tử ! để che lỗi null.
- Class, enum, public method/property dùng PascalCase; local/parameter dùng camelCase; private field dùng _camelCase.
- Prefer async/await cho database, storage, I/O; đặt hậu tố Async cho method bất đồng bộ nếu convention project sử dụng.
- Truyền CancellationToken qua Controller → Application → EF Core/external calls.
- Sử dụng dependency injection qua constructor; tránh service locator và static mutable global state.
- Một method có một trách nhiệm rõ ràng, không tạo lớp GodService.
- Không swallow exception; log có ngữ cảnh nhưng không lộ token, mật khẩu hay dữ liệu nhạy cảm.
- Không đặt logic thao tác database trong Program.cs.
- Dùng DateTime.UtcNow hoặc TimeProvider được inject theo convention; lưu UTC trong PostgreSQL.
- Tránh dùng Task.Run để chạy fire-and-forget cho nghiệp vụ yêu cầu độ tin cậy.

### 4.2. Controllers và API

- Controller chỉ tiếp nhận input, kiểm tra auth, gọi use case/service, map HTTP response.
- Request/Response DTO tách biệt Entities. Không expose PasswordHash, RefreshToken hash, internal notes trái phép hoặc storage object keys.
- Validate dữ liệu trên backend; không trông chờ validation ở FE.
- Dùng chuẩn status codes: 200/201/204, 400, 401, 403, 404, 409, 422 khi phù hợp và có định nghĩa nhất quán.
- Dùng ProblemDetails/ValidationProblemDetails cho lỗi có cấu trúc; không trả exception stack trace trong production.
- Pagination phía server với page/pageSize hoặc cursor theo quy ước được chọn; giới hạn page size.
- Tên route dùng resource, rõ ràng, có version nếu dự án quyết định áp dụng (ví dụ /api/tickets).
- Nếu sửa contract, cập nhật OpenAPI và client DTO/hook ở frontend.
- Chỉ thêm endpoint sau khi phân tích authorization, validation, audit, transaction và tác động realtime.

### 4.3. Service/use case và transaction

- Tác vụ nhiều bước (đổi trạng thái + log activity + SLA + notification) phải có ranh giới transaction rõ ràng.
- Commit thay đổi PostgreSQL trước khi phát SignalR; khi yêu cầu delivery tin cậy, dùng outbox thay vì giả định event không bao giờ mất.
- Business rule phải tồn tại ở service/domain và được unit-test, không lặp phân tán ở nhiều Controllers.
- Sử dụng optimistic concurrency; khi cập nhật xung đột, trả lỗi 409 với thông báo có thể retry/refetch.
- Hạn chế transaction kéo dài qua việc upload file hoặc gọi dịch vụ ngoài.
- Khi nghiệp vụ chấp nhận retry, cân nhắc idempotency để tránh tạo Ticket/bình luận trùng.

## 5. EF Core 8 / PostgreSQL / dữ liệu

### 5.1. Mô hình schema đã thống nhất

Bốn PostgreSQL schemas và 18 Entities mục tiêu:

| PostgreSQL schema | Entities |
| --- | --- |
| identity | User, ExternalIdentity, Role, Permission, UserRole, RolePermission, RefreshToken |
| organization | Department, SupportTeam, SupportTeamMember |
| helpdesk | TicketCategory, Ticket, TicketComment, TicketAttachment, TicketActivity, SlaPolicy, TicketSla |
| communication | Notification |

Đây là **thiết kế mục tiêu**, không khẳng định migrations/entities hiện đã có trong be/.

### 5.2. Quy tắc Entity và Fluent API

- Target **net8.0 / EF Core 8.x / Npgsql 8.x**. **Không sao chép package EF Core 10 / .NET 10** từ prototype trước vào backend hiện tại.
- Entity dùng PascalCase; bảng/cột PostgreSQL dùng snake_case; schema phải khai báo tường minh.
- Ưu tiên UUID cho PK; TicketNumber là số duy nhất được database sinh bằng sequence/identity, hiển thị như HD-000001.
- Cấu hình quan hệ bằng IEntityTypeConfiguration<T>; đặt composite keys, indexes, nullability, max length, FK/DeleteBehavior tường minh.
- Role ↔ Permission, User ↔ Role, User ↔ SupportTeam là N–N qua bảng liên kết.
- Ticket có **ba quan hệ User riêng**: RequesterId, CreatedByUserId, AssignedAgentId.
- Quan hệ lịch sử (Ticket, Activity, Comment, SLA) không cascade-delete chỉ vì xóa User.
- Không tự tạo bảng status/priority nếu trạng thái còn cố định theo nghiệp vụ; enum được lưu string và validate ở DB khi thích hợp.
- JSONB dùng cho dữ liệu biến thiên như activity old/new values; không thay toàn bộ relational columns bằng JSONB.
- DateTime UTC ánh xạ timestamptz; không lưu giờ local làm mốc chuẩn.
- Concurrency trên PostgreSQL có thể dùng uint Version ánh xạ xmin; xác nhận cấu hình của Npgsql 8 và kiểm thử conflict, không tự tăng xmin thủ công.
- Chỉ sử dụng migrations được review; **không gọi EnsureCreated** chung với migrations.
- Không chỉnh sửa migrations đã apply trong môi trường dùng chung. Tạo migration mới, kiểm tra SQL và tác động dữ liệu.
- Không để code tự động chạy migrations hay xóa schema production khi ứng dụng start nếu chưa được chấp thuận.
- Dùng AsNoTracking cho read-only; projection trực tiếp DTO, tránh Include dư thừa và N+1.
- Thêm indexes dựa trên query pattern, kiểm chứng bằng explain/test thay vì tạo index mọi cột.
- Không thêm generic repository abstraction chỉ để bọc lại toàn bộ DbSet/DbContext mà không tạo giá trị.

### 5.3. Quy tắc nghiệp vụ dữ liệu

- TicketStatus: New, InProgress, WaitingForRequester, Resolved, Closed, Cancelled, Reopened.
- TicketPriority: Low, Normal, High, Urgent.
- Status transitions phải được backend kiểm tra; Resolved yêu cầu ResolutionSummary; Closed không được tùy ý sửa.
- Mọi chuyển trạng thái, ưu tiên, nhóm và agent phải ghi TicketActivity trong transaction tương ứng.
- Phân công agent: kiểm tra user còn active, thuộc assigned team và có quyền phù hợp.
- Public comments và Internal notes phải có phân quyền xem khác nhau.
- File gắn comment phải thuộc cùng ticket; kiểm tra cả TicketId, CommentId và internal visibility.
- Ticket SLA snapshot deadline khi áp dụng; MVP tính 24/7. Không tự thay đổi deadline ticket cũ khi policy được sửa.
- First response chỉ tính phản hồi công khai hợp lệ của Agent, không tính internal note hoặc comment từ Requester.
- Không xóa cứng các bản ghi nghiệp vụ có giá trị lịch sử; ưu tiên deactivate/archival theo rule đã thống nhất.
- Không tin requesterId, assignedAgentId, role hoặc createdBy từ body nếu danh tính cần được suy ra từ Claims.

## 6. Quy tắc Authentication và Authorization

- Backend xác minh JWT: issuer, audience, signature, lifetime và các claims bắt buộc.
- Access token có thời gian sống ngắn, refresh token được lưu dạng **hash**, có thời hạn và cơ chế revoke/rotate.
- Nếu đăng nhập Google/Microsoft, dùng **OpenID Connect (OIDC) trên OAuth 2.0**, kiểm tra state, nonce, issuer, audience và provider subject.
- Quyết định lưu token theo threat model: ưu tiên HttpOnly + Secure + SameSite cookie nếu phù hợp; khi dùng cookie, xử lý CSRF. Nếu dùng Bearer token trong FE, tránh lưu token nhạy cảm dài hạn ở localStorage.
- **RBAC kiểm tra permission**, sau đó **resource-based authorization kiểm tra từng Ticket**: requester/assignee/support team/admin theo business rule.
- FE dùng permission để tối ưu UX (ẩn/hiện thao tác), **không phải nguồn bảo mật**.
- Chỉ admin được quản lý role/permissions theo chính sách; không cho phép người dùng tự nâng quyền.
- Login, refresh, logout, ban/unban phải được kiểm thử các trường hợp expired/revoked/disabled.
- Không hardcode JWT secret, OAuth secret hoặc connection string chứa password trong source.

## 7. Quy tắc React 19 / TypeScript / Vite

### 7.1. Frontend coding style

- Dùng **TypeScript** cho logic và components; không tạo file JavaScript mới cho nghiệp vụ nếu không có lý do.
- Functional components + React Hooks; không dùng class component cho tính năng mới.
- Định nghĩa props và response types cụ thể; tránh any, type assertion để né type checking, và non-null assertions bừa bãi.
- State cần derivation thì tính từ nguồn, không duplicate rồi sync bằng useEffect không cần thiết.
- Giữ components nhỏ, có tên rõ ràng; không nhồi API, form, chart, table vào một component khổng lồ.
- Hook chỉ gọi ở top-level; effect dependencies chính xác; cleanup timer/subscriptions/listeners.
- Chú trọng loading/empty/error/success states, accessibility (label, button, keyboard), responsive UI.
- Tuân thủ ESLint và React Hooks rules hiện có. Không disable lint rule toàn cục để chữa một warning.
- Tailwind CSS 4 đã có trong project; dùng thống nhất với CSS hiện tại, không cài thêm UI framework bất ngờ.
- Không hardcode base URL hay secrets; VITE_* chỉ dành cho cấu hình **public** vì được bundle vào browser.

### 7.2. Phân trách nhiệm các thư viện đang cài

| Thư viện | Trách nhiệm |
| --- | --- |
| React Router 7 | Routing, nested layouts, navigation và route guards cho UX |
| TanStack Query 5 | Fetch/cache server state, query keys, mutations, invalidation |
| Zustand 5 | UI/client-only state như sidebar, filters tạm, trạng thái phiên cần thiết |
| Axios | HTTP client chung, baseURL, auth handling, errors, cancellation |
| @microsoft/signalr | Connection lifecycle, subscribe/unsubscribe, reconnect |
| Tailwind CSS 4 | Styling, responsive UI, reusable design tokens khi cần |

- **Không lưu cùng một danh sách Ticket trong Zustand và React Query** rồi tự đồng bộ thủ công.
- **Không gọi Axios trực tiếp trong presentational component**; đặt typed request trong features/*/api và wrap với Query hooks.
- Query keys phải ổn định; invalidate những keys chịu ảnh hưởng sau create/update/comment/assign.
- Khi nhận SignalR event: invalidate/refetch hoặc update query cache **có kiểm tra quyền và tính nhất quán**, không xem payload realtime là dữ liệu authoritative.
- React Router guards chỉ bảo vệ UX; Backend luôn kiểm tra auth.
- Mỗi feature có types tách khỏi Entities C# và ánh xạ đúng OpenAPI/DTO.

### 7.3. Quy tắc form và giao diện

- Validate required fields, length, file size/type, trạng thái button và feedback lỗi.
- Không hiển thị nút chuyển status không hợp lệ hoặc assign Agent ngoài nhóm; BE vẫn phải reject request sai.
- Requester **không thấy** internal notes và file riêng tư, kể cả trong cache, preview, notification hoặc SignalR payload.
- Khi truy vấn list lớn, pagination/filter/sort xử lý phía server.
- Hiển thị thời gian theo timezone của người dùng; API trao đổi timestamp UTC/ISO 8601.
- Không sử dụng dangerouslySetInnerHTML với dữ liệu từ người dùng nếu chưa sanitize bằng giải pháp đáng tin cậy.

## 8. API contract FE ↔ BE

Trước khi triển khai một tính năng end-to-end, xác định tối thiểu:
- HTTP method + route.
- Request DTO + validation rules.
- Response DTO + mã HTTP + dạng lỗi.
- Authentication, permission và resource access.
- Pagination/filter/sort nếu là list.
- Side effects: TicketActivity, Notification, SignalR event, cache invalidation.
- OpenAPI và các trường hợp test.

Các tên endpoint dưới đây **là đề xuất, chưa xác nhận đã triển khai**:

~~~http
POST   /api/auth/login
POST   /api/auth/refresh
POST   /api/auth/logout
GET    /api/tickets?page=1&pageSize=20
POST   /api/tickets
GET    /api/tickets/{id}
PATCH  /api/tickets/{id}/status
POST   /api/tickets/{id}/comments
POST   /api/tickets/{id}/attachments
GET    /api/notifications
~~~

- Các identifier trong route phải validate.
- Tạo mới: 201 + resource/Location khi phù hợp; delete/no body: 204.
- Validation errors: thống nhất ProblemDetails/ValidationProblemDetails; FE có mapping lỗi hiển thị được.
- 401 = không/chưa xác thực; 403 = đã xác thực nhưng thiếu quyền; 404 cho resource không hiện hữu hoặc cần che giấu sự tồn tại theo policy.
- Không thay đổi tên JSON field silently; nếu breaking change, thay cả contract phía FE và tài liệu.

## 9. Realtime với SignalR

- HTTP/REST xử lý command và trả kết quả; SignalR chỉ thông báo sự kiện đã xảy ra.
- Backend xác thực handshake và bảo đảm mỗi connection được join đúng user/team/ticket group theo quyền.
- **SignalR group không tự nó là cơ chế authorization.** Kiểm tra membership và quyền mỗi khi join group; kiểm soát lúc quyền thay đổi.
- Không phát internal notes, token, object keys hay thông tin nhạy cảm vào group rộng.
- Quản lý connect/reconnect/disconnect ở một nơi trong FE; cleanup handlers khi unmount.
- Không tạo connection trong mỗi component render.
- Nếu dùng SignalR JS client major 10 với ASP.NET Core server 8, **kiểm tra tương thích giao thức bằng integration test**; không tự suy ra server phải nâng lên .NET 10.
- Notification bền vững nằm ở PostgreSQL; SignalR không bảo đảm người dùng offline nhận được message.

## 10. Redis, MinIO và Docker

### Redis
- PostgreSQL là source of truth; Redis chỉ là cache dữ liệu có thể khôi phục.
- Cache key có namespace có ý nghĩa, TTL được xác định, không cache thông tin nhạy cảm theo key dùng chung.
- Cache phải invalidated khi data thay đổi; ưu tiên cache catalog/summary/read-heavy trước khi cache Ticket có phân quyền phức tạp.
- Không đẩy nguyên toàn bộ JWT, password, hoặc object bí mật vào log/cache.
- Không tối ưu dựa trên phỏng đoán; đo performance trước/sau.

### MinIO
- Bucket lưu file phải private theo mặc định; PostgreSQL lưu metadata (bucket, object key, filename, MIME, size, owner/ticket).
- Không dựa vào filename người dùng cung cấp để tạo path tin cậy; kiểm tra path traversal, extension, content type và size.
- Backend kiểm tra quyền trước download/preview hoặc cấp presigned URL thời hạn ngắn.
- Áp dụng hạn mức file và kiểm tra file độc hại theo khả năng triển khai.
- Nếu upload MinIO thành công nhưng DB transaction thất bại, có cơ chế cleanup/reconciliation; không giữ orphan vô hạn.
- Không ghi quyền truy cập MinIO admin hoặc presigned URL lâu dài vào bảng dữ liệu.

### Docker
- Một monorepo có thể dùng root docker-compose.yml cho hạ tầng và service frontend/backend; **chỉ thêm khi có yêu cầu triển khai**, không giả định file đã tồn tại.
- Build contexts phải đúng với be/ và fe/; dùng Docker multi-stage khi cần.
- Credentials/config nhạy cảm qua environment/secrets; commit .env.example (không có mật khẩu thực), không commit .env.
- PostgreSQL/MinIO cần persistent volumes; thêm healthcheck khi các service phụ thuộc lẫn nhau.
- Không dùng compose down -v, xóa volume hoặc drop database nếu chưa được người dùng cho phép.
- Phân biệt localhost (máy host) và service name (trong Docker network).

## 11. Security và bảo vệ dữ liệu

- Validate mọi input ở server; parameterized queries/EF Core, không nối chuỗi SQL từ user input.
- CORS allowlist theo origin thật; không dùng AllowAnyOrigin + credentials.
- Chống XSS và CSRF theo cách lưu token; cấu hình HTTPS và secure headers khi deploy.
- Rate limiting cho login, refresh, upload và các endpoint dễ bị abuse.
- Logs chứa correlation/request ID nhưng không chứa password, access token, refresh token, OAuth secret, nội dung file riêng tư.
- Password chỉ lưu dạng strong password hash, không encrypt hoặc hash SHA-256 đơn giản.
- Access control bao phủ REST API, SignalR groups, file access, caching, export/report.
- Không trả raw exception, connection string, environment values ra client.
- Nếu sửa RBAC, thêm negative tests (người không có quyền phải bị chặn).

## 12. Testing, quality gates và commands

### Frontend (chạy từ fe/)

~~~bash
npm ci
npm run lint
npm run build
~~~

- Dùng npm theo package-lock.json hiện tại, không tự chuyển sang yarn/pnpm.
- Build frontend bao gồm TypeScript check theo script trong package.json.
- Khi bổ sung test runner, tạo unit/component tests cho hành vi quan trọng, tránh snapshot vô nghĩa.
- Test các trường hợp unauthorized, loading, empty results, server error, ticket conflict, reconnect.

### Backend (chạy từ be/)

~~~bash
dotnet restore be.sln
dotnet build be.sln
dotnet test be.sln
~~~

- Hiện chưa có test project rõ ràng; khi test chưa tồn tại, thông báo trung thực, không tuyên bố test đã pass.
- Viết unit tests cho Ticket workflow, assignment rules, SLA calculation, authorization policies.
- Integration tests dùng PostgreSQL thật qua container/test environment cho EF constraints và transaction; không chỉ dựa vào EF InMemory để kiểm tra PostgreSQL semantics.
- Có migration thì chạy kiểm tra migrations trên database test và review SQL trước production.
- Không chạy lệnh phá dữ liệu nếu chưa được phê duyệt.

### Definition of Done cho mỗi tính năng

1. Code theo đúng boundary của fe/ và be/.
2. Business rules nằm ở backend; authorization không bỏ sót.
3. Request/response DTO, validation và error handling đồng bộ.
4. Database schema/migration/index phù hợp (nếu có thay đổi DB).
5. API + FE hooks + UI + SignalR/cache side effects cập nhật nhất quán.
6. Test được bổ sung theo rủi ro thay đổi; build/lint/test được chạy khi có thể.
7. README/guides/OpenAPI hoặc .env.example được cập nhật nếu contract/setup/kiến trúc đổi.
8. Báo cáo chính xác file đã sửa, test đã chạy, test chưa chạy và rủi ro còn lại.

## 13. Quy trình bắt buộc AI phải thực hiện

### Bước A — Khảo sát

- Đọc yêu cầu người dùng, README.md, guides.md và những file liên quan ở **cả fe/ và be/** nếu thay đổi đi qua API.
- Xác định feature nào đã tồn tại, feature nào chưa có; đọc package.json và .csproj trước khi thêm dependency.
- Ước lượng phạm vi thay đổi: UI-only, API-only, database, hay end-to-end.

### Bước B — Lập kế hoạch thay đổi nhỏ

- Xác định file cần tạo/sửa, request/response contract và quyền cần bảo vệ.
- Không triển khai thêm kiến trúc hoặc dependency không phục vụ task.
- Nếu gặp lựa chọn quan trọng chưa được xác định, trình bày trade-off, chọn phương án tối thiểu an toàn và ghi rõ.

### Bước C — Implement theo thứ tự hợp lý

1. Domain/entity/migration (chỉ nếu cần).
2. Application use case, validation, authorization, transaction.
3. API controller/DTO/OpenAPI.
4. FE typed API/hooks/components/routes.
5. Cache invalidation, notifications và realtime.
6. Tests và tài liệu.

Nếu task chỉ ảnh hưởng một phần, không tạo phần còn lại giả lập.

### Bước D — Tự review

- Có phá API contract, DB schema hoặc feature khác không?
- Có lộ dữ liệu internal hoặc bỏ kiểm tra quyền không?
- Có race condition khi phân công/đổi status không?
- Có unhandled error, unnecessary renders, N+1 query hoặc cache sai phạm vi không?
- Có hardcode secret, đưa file-generated vào git, hoặc tạo folder thừa không?

### Bước E — Báo cáo minh bạch

Trả lời ngắn gọn theo mẫu:
- Đã thay đổi gì, tại sao.
- Danh sách file quan trọng.
- Lệnh đã chạy và kết quả.
- Việc chưa xác minh/rủi ro còn lại.
- Có cần migration/env var/thao tác thủ công nào không.

## 14. Điều AI không được tự ý làm

- Không nâng project từ .NET 8 lên .NET 10 hoặc React lên phiên bản khác khi chưa được yêu cầu.
- Không thêm microservices, CQRS/MediatR, event bus, DDD quá mức hoặc nhiều project assemblies chỉ để "đúng chuẩn".
- Không đưa toàn bộ business logic vào controller hoặc trong React component.
- Không tự động chỉnh sửa database production, reset migrations hay xóa Docker volumes.
- Không lạm dụng Repository Pattern, Service Layer, Zustand, Redis, useMemo/useCallback hoặc abstraction không cần thiết.
- Không đổi toàn bộ naming conventions, formatting, UI hoặc package manager trong task nhỏ.
- Không tự thêm AI chatbot, vector database hoặc gọi API ngoài khi chưa được yêu cầu.
- Không tạo code giả, placeholder logic hoặc mock response để ngụy trang tính năng như đã hoàn thiện.
- Không commit secrets, credentials, token, dữ liệu cá nhân hoặc config nội bộ nhạy cảm.
- Không đẩy thẳng lên nhánh production, force-push hoặc overwrite công việc khác khi chưa có yêu cầu.
- Không kết luận đã hoàn thành nếu chưa đáp ứng acceptance criteria.

## 15. Checklist đặc biệt theo loại task

| Task | AI bắt buộc kiểm tra |
| --- | --- |
| Login / Refresh | Signature + claims + expiry + rotation + revoke + cookie/CSRF strategy |
| Tạo Ticket | Validate + requester identity + category + unique ticket number + activity |
| Phân công | Team membership + active Agent + authorization + concurrency + activity + notification |
| Đổi trạng thái | Legal transition + resolved summary + activity + SLA side effects |
| Comment | Public/internal visibility + permission + attachment ownership + realtime recipient |
| Attachment | Private bucket + file limits + ACL + metadata + orphan cleanup |
| SLA | Deadline snapshot + first public Agent response + overdue detection + tests |
| Notification | Persist DB + authorized SignalR event + offline recovery |
| Dashboard | Role-scoped aggregation + database indexes + pagination/filtering |
| Caching | Cache key isolation + invalidation + TTL + permission leakage review |
| Database migration | Existing data + indexes + FK/DeleteBehavior + rollback plan |
| UI form/list | Typed contract + loading/error/empty states + accessibility + unauthorized UX |

## 16. Hướng dẫn cho từng giai đoạn đồ án

**MVP ưu tiên:**
1. Identity + RBAC + cơ cấu phòng ban/nhóm.
2. Ticket category, Ticket CRUD với scope, assignment, status workflow.
3. Comment, internal note, TicketActivity.
4. MinIO attachment.
5. Notification + SignalR.
6. SLA + báo cáo cơ bản.
7. Tests, security hardening, Docker deployment.

**Sau MVP:** business-hour SLA, TicketWatcher, Feedback, Knowledge Base, AI phân loại/gợi ý giải pháp.

Không triển khai công nghệ mới vì "đẹp CV" nếu làm tăng rủi ro không hoàn thành MVP.

---

## 17. Ưu tiên khi có xung đột quy tắc

1. Yêu cầu trực tiếp và gần nhất của người dùng, trong phạm vi an toàn.
2. An toàn dữ liệu/bảo mật và constraints thực tế.
3. Source code, tests, package manifests và migrations đang áp dụng.
4. Tài liệu này (guides.md).
5. README.md về định hướng sản phẩm và chức năng mục tiêu.
6. Đề xuất kiến trúc mới từ AI.

Nếu hai nguồn mâu thuẫn nhau, **không âm thầm thay framework hoặc sửa dữ liệu**. Giải thích điểm mâu thuẫn trong báo cáo, chọn giải pháp an toàn và phù hợp nhất với code thực tế.

> **Nguyên tắc cốt lõi:** Đúng nghiệp vụ, đúng ranh giới giữa frontend/backend, bảo mật theo từng tài nguyên, dữ liệu nhất quán, code đơn giản có kiểm thử, và ưu tiên hoàn thành một sản phẩm Helpdesk vận hành được.
