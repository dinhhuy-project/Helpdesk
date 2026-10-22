# Persistence với EF Core / PostgreSQL

`be/Infrastructure/Persistence` chứa `HelpdeskDbContext`, đăng ký DI và 18 lớp `IEntityTypeConfiguration<T>`. Domain giữ nguyên C# thuần. Không gọi `EnsureCreated` hoặc `Migrate` khi API khởi động. Profile Container (Dockerfile) có seed catalog Identity tùy chọn như [hướng dẫn Identity](backend-identity-foundation.md).

## Packages và cấu hình

- Microsoft.EntityFrameworkCore, Relational, Design và Tools: **8.0.32**; Design/Tools là dependency phát triển (`PrivateAssets=all`).
- Npgsql.EntityFrameworkCore.PostgreSQL: **8.0.11**.
- EFCore.NamingConventions: **8.0.3**, chuyển tên cột/khóa/index sang snake_case.

DbContext ưu tiên `ConnectionStrings:Helpdesk`, fallback `ConnectionStrings:DefaultConnection` nếu key chính trống. Compose đã truyền `ConnectionStrings__Helpdesk` với host `postgres`. Khi chạy trực tiếp trên máy, cấu hình bằng user-secrets, thay placeholder bằng mật khẩu trong `.env`:

```powershell
dotnet user-secrets set 'ConnectionStrings:Helpdesk' 'Host=localhost;Port=5432;Database=helpdesk;Username=helpdesk;Password=<local-password>' --project be/be.csproj
```

Khi debug bằng **Container (Dockerfile)** của Visual Studio, container chạy độc lập với Compose: dùng `Host=host.docker.internal` và port PostgreSQL publish trên host, thay vì `localhost` hoặc `postgres`. Cấp connection string bằng user-secrets được Visual Studio mount vào container development hoặc environment variables của phiên debug. Không commit mật khẩu vào launchSettings/appsettings. Nếu dùng chung user-secrets cho hai chế độ chạy, cần đổi host tương ứng.

Đăng ký DbContext không mở kết nối ngay. Thiếu connection string sẽ báo lỗi khi resolve DbContext; Swagger và `/health/live` vẫn hoạt động. Health này chưa kiểm tra database.

## Mapping

| Schema | Tables |
| --- | --- |
| `identity` | users, external_identities, roles, permissions, user_roles, role_permissions, refresh_tokens |
| `organization` | departments, support_teams, support_team_members |
| `helpdesk` | ticket_categories, tickets, ticket_comments, ticket_attachments, ticket_activities, sla_policies, ticket_slas |
| `communication` | notifications |

- UUID tạo ở Domain; các join entity có khóa ghép. `TicketSla.TicketId` là shared primary key cho quan hệ 1–0..1.
- Các FK khai báo tường minh, `DeleteBehavior.Restrict` để giữ lịch sử. Không cascade-delete kể cả các bảng liên kết.
- `ticket_number` là bigint identity **GENERATED ALWAYS**, có unique index. `Ticket.Version` dùng `IsRowVersion()` ánh xạ system column `xmin`, không tạo cột version/xmin mới trong DDL.
- Enum lưu chuỗi có check constraint; mọi DateTime ánh xạ `timestamp with time zone`. Caller phải cung cấp UTC.
- `old_value`/`new_value` lưu jsonb; chuỗi không phải JSON hợp lệ bị PostgreSQL từ chối.
- Unique indexes cho email, role name, permission code, provider + subject, token hash, bucket + object key và ticket number. Uniqueness của chuỗi hiện phân biệt hoa/thường; use case Identity phải chuẩn hóa email trước khi lưu và truy vấn.
- Index phục vụ danh sách ticket theo requester/team/agent/status + thời gian; comment/activity theo ticket + thời gian; notification theo user + thời gian và index riêng cho chưa đọc; SLA có partial indexes cho deadline chưa hoàn tất.
- FK ghép `(comment_id, ticket_id)` bảo đảm file gắn vào comment thuộc cùng ticket. Quyền xem và `IsInternal` vẫn cần kiểm tra ở Application; FK không thay thế ACL.
- Check constraints: enum hợp lệ, agent phải có team, Resolved/Closed có summary và resolved_at, Closed có closed_at, kích thước file không âm, thời lượng SLA dương, deadline SLA không trước thời điểm áp dụng.
- Giới hạn độ dài khởi tạo: email 320; tên người/phòng ban/team/category/SLA 200; role/action/provider 100; permission code 150; subject 255; title 300; object key 1024; bucket 63; tên file và MIME 255. Nội dung ticket/comment/notification và resolution summary dùng text. Đây là giới hạn kỹ thuật ban đầu để đồng bộ với DTO validation khi triển khai API.

Mapping không thực thi toàn bộ workflow: membership/active Agent, chuyển trạng thái hợp lệ, SLA calculation, refresh token rotation, audit transaction và resource authorization vẫn thuộc bước Application/Domain rules tiếp theo.

## Kiểm thử

```powershell
dotnet restore be/be.sln
dotnet build be/be.sln --no-restore -warnaserror
dotnet test be/be.sln --no-build --no-restore
```

Lệnh test thông thường chạy model/migration, API/configuration tests; PostgreSQL integration tests được đánh dấu skip khi chưa có `HELPDESK_TEST_CONNECTION`. Xem thêm [nền tảng Identity/API](backend-identity-foundation.md).

Để chạy đầy đủ trên PostgreSQL 16 thật:

```powershell
& ./be/tests/Run-PersistenceTests.ps1
```

Script tạo container PostgreSQL riêng với mật khẩu ngẫu nhiên, port host tự cấp, không mount volume, chạy test rồi dừng container trong `finally`. Không dùng database/volume Helpdesk hiện có. Fixture yêu cầu database rỗng có tên bắt đầu `helpdesk_mapping_test_`, kiểm tra apply/rollback/reapply InitialCreate trên schema test trước khi có dữ liệu nghiệp vụ. Không drop/reset database nào.

Test kiểm tra đủ schema/khóa/FK, snake_case, UTC, identity, enum/jsonb round-trip, concurrency xmin, unique email, chặn xóa User có lịch sử, file gắn nhầm comment, enum không hợp lệ, Resolved thiếu summary/time và SLA duration không dương.

## Migration InitialCreate

Đã tạo `20261022150705_InitialCreate`, snapshot và SQL áp dụng/rollback trong `Infrastructure/Persistence/Migrations`. Tool manifest root pin dotnet-ef **8.0.32**; chạy `dotnet tool restore` để dùng đúng tool local. Xem [kết quả review và hướng dẫn migration](initial-create-review.md). Người dùng đã xác nhận áp dụng migration vào database Helpdesk; không chạy lại bước tạo InitialCreate.

Migration history đã cấu hình là `helpdesk.__ef_migrations_history`. Chỉ apply vào database đã xác định; không tự chạy migration lúc startup và không xóa volume để xử lý lỗi schema.
