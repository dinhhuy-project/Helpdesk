# Review migration InitialCreate

Migration: `20261022150705_InitialCreate`, EF Core và dotnet-ef **8.0.32**, provider Npgsql **8.0.11**. Người dùng đã xác nhận áp dụng migration vào database Helpdesk sau đợt review này. Tài liệu giữ lại kết quả review và cách vận hành; không tạo lại InitialCreate hoặc sửa migration đã apply.

## Artifacts

- [Migration Up/Down](../be/Infrastructure/Persistence/Migrations/20261022150705_InitialCreate.cs).
- Designer và `HelpdeskDbContextModelSnapshot.cs` trong cùng thư mục.
- [SQL áp dụng idempotent](../be/Infrastructure/Persistence/Migrations/Sql/InitialCreate.sql).
- [SQL rollback](../be/Infrastructure/Persistence/Migrations/Sql/InitialCreate.Rollback.sql).
- [.config/dotnet-tools.json](../.config/dotnet-tools.json) pin dotnet-ef local 8.0.32, không thay đổi tool global.

## Kết quả review SQL

| Hạng mục | Kết quả |
| --- | --- |
| Bảng nghiệp vụ | 18 bảng thuộc identity, organization, helpdesk, communication |
| Migration history | `helpdesk.__ef_migrations_history`, ngoài 18 bảng nghiệp vụ |
| Khóa ngoại | 28 FK, toàn bộ `ON DELETE RESTRICT`, không cascade-delete |
| Check constraints | 9: enum, resolution/closed_at, team khi gán agent, kích thước file, SLA duration/deadlines |
| Index khai báo | 35 index, gồm unique và partial indexes; chưa bao gồm index backing PK/alternate key |
| Ticket number | bigint `GENERATED ALWAYS AS IDENTITY`, unique index |
| Concurrency | `xmin` dùng system column sẵn có; SQL không tạo cột xmin/version mới |
| Kiểu dữ liệu | UUID, timestamptz, enum dạng chuỗi, JSONB cho activity |
| Attachment → Comment | FK ghép `(comment_id, ticket_id)` chặn gắn comment khác ticket |
| SQL áp dụng | Không có DROP/TRUNCATE/DELETE/UPDATE; tạo schema/table/index và ghi history |
| Transaction | DDL nghiệp vụ và ghi migration history nằm trong một transaction |

Script idempotent kiểm tra migration ID trong history trước khi chạy từng bước. Nó không so sánh hoặc sửa schema thủ công đã tồn tại. Nếu database đã có bảng trùng tên nhưng không có history tương ứng, phải kiểm tra hiện trạng/baseline riêng; không xóa bảng/volume để ép chạy migration.

Schema `helpdesk` và bảng history được chuẩn bị trước transaction nghiệp vụ. Nếu migration lỗi, chúng có thể còn tồn tại; các bảng nghiệp vụ tạo trong transaction bị rollback. Không chạy nhiều tiến trình áp dụng migration đồng thời: EF Core 8 chưa có cơ chế khóa migration của các phiên bản mới hơn.

Các lựa chọn giữ nguyên theo mapping: chuỗi unique phân biệt hoa/thường (Application phải chuẩn hóa email), ACL/internal visibility và active team membership vẫn cần enforce trong use case. Chưa có seed role/permission, tài khoản admin hay dữ liệu mặc định. Các giá trị mặc định ở entity C# không trở thành SQL DEFAULT; thao tác ghi ngoài EF phải cung cấp các trường bắt buộc.

## Kiểm chứng

- Build solution với `-warnaserror`.
- `dotnet ef migrations has-pending-model-changes`: không có thay đổi chưa đưa vào migration.
- Model test so sánh cả hai SQL đã lưu với SQL sinh từ migration, tránh artifact lệch source/snapshot.
- Container PostgreSQL 16 riêng: apply bằng EF migration API, chạy SQL rollback khi chưa có dữ liệu nghiệp vụ, xác nhận chỉ còn bảng history, áp dụng lại bằng SQL đã review.
- Chạy lại SQL áp dụng hai lần trên schema đã có một ticket test, xác nhận ticket và migration history không thay đổi.
- Chạy lại các test persistence: UUID/identity, enum/UTC/JSONB, xmin concurrency, FK, unique email và check constraints.

Chạy đầy đủ kiểm thử từ root:

```powershell
dotnet restore be/be.sln
dotnet build be/be.sln --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Test chỉ sử dụng container/database riêng được script tạo ra; container tự dừng trong `finally`. Database Helpdesk local không bị thay đổi.

## Tái tạo SQL để review

Từ root, build trước khi dùng `--no-build`. Đoạn dưới dùng connection string design-only, không truy cập database, không chứa credentials thật:

```powershell
dotnet tool restore
dotnet ef --version
dotnet build be/be.csproj
$previousConnection = $env:ConnectionStrings__Helpdesk
try {
    $env:ConnectionStrings__Helpdesk = 'Host=127.0.0.1;Port=1;Database=helpdesk_design_only;Username=design_only'
    dotnet ef migrations script 0 InitialCreate --idempotent --no-build --project be/be.csproj --context HelpdeskDbContext --output be/Infrastructure/Persistence/Migrations/Sql/InitialCreate.sql
    dotnet ef migrations script InitialCreate 0 --no-build --project be/be.csproj --context HelpdeskDbContext --output be/Infrastructure/Persistence/Migrations/Sql/InitialCreate.Rollback.sql
    dotnet ef migrations has-pending-model-changes --no-build --project be/be.csproj --context HelpdeskDbContext
}
finally {
    $env:ConnectionStrings__Helpdesk = $previousConnection
}
```

Không tạo lại `InitialCreate` bằng `migrations add`: migration đã có. Nếu model thay đổi sau khi migration được apply vào môi trường dùng chung, tạo migration mới.

## Áp dụng sau bước review

Chỉ chạy sau khi xác định đúng database đích và chuẩn bị sao lưu nếu có dữ liệu. Cấu hình `ConnectionStrings:Helpdesk` qua user-secrets hoặc environment của máy chạy CLI; CLI trên host dùng `localhost` và port publish của PostgreSQL. Compose `.env` không tự được dotnet CLI đọc. Không dùng connection design-only ở phần tái tạo SQL để apply.

Chọn **một** cách:

- Dùng EF CLI: `dotnet ef database update InitialCreate --project be/be.csproj --context HelpdeskDbContext`.
- Dùng psql kết nối đúng database: `psql -X --set ON_ERROR_STOP=1 --file be/Infrastructure/Persistence/Migrations/Sql/InitialCreate.sql` (cấp host/database/user/password qua cấu hình psql an toàn).

Sau đó kiểm tra `SELECT migration_id, product_version FROM helpdesk.__ef_migrations_history;`: dự kiến có `20261022150705_InitialCreate` và `8.0.32`. Migration chỉ tạo schema; role/admin được chuẩn bị ở [bước Identity](backend-identity-foundation.md) để dùng [API login](backend-authentication.md). API startup vẫn không tự chạy migration.

## Rollback

`InitialCreate.Rollback.sql` **xóa toàn bộ 18 bảng nghiệp vụ và dữ liệu trong đó**, rồi xóa dòng migration history tương ứng. Nó giữ lại các schema và bảng history. Script rollback không idempotent, chỉ dùng khi database đang ở đúng migration InitialCreate và đã chấp thuận mất dữ liệu/đã sao lưu.

Trong môi trường có dữ liệu thật, ưu tiên migration sửa tiếp hoặc phục hồi theo kế hoạch đã review. Không chạy rollback, `database update 0`, drop database hay xóa Docker volume như thao tác dừng hệ thống thông thường.

Tham khảo chính thức: [review và quản lý migration](https://learn.microsoft.com/ef/core/managing-schemas/migrations/managing), [áp dụng SQL idempotent](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying).
