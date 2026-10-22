# Domain backend

`be/Domain` khởi tạo mô hình C# thuần cho 18 entity trong `guides.md`, không tham chiếu EF Core, ASP.NET Core, Redis hay MinIO.

| Thư mục | Nội dung |
| --- | --- |
| `Entities` | 7 entity Identity, 3 Organization, 7 Helpdesk, 1 Notification |
| `Enums` | `TicketStatus` (7 trạng thái), `TicketPriority` (4 mức ưu tiên) |
| `Constants` | `RoleNames` (4 role), `PermissionCodes` (11 permission theo README) |

## Quy ước và lựa chọn khởi tạo

- Namespace `be.Domain.*`, giữ project `be.csproj` và target `net8.0` hiện có.
- Entity độc lập có `Guid Id` khởi tạo bằng `Guid.NewGuid()`. `UserRole`, `RolePermission`, `SupportTeamMember` dùng cặp khóa ngoại làm khóa ghép ở bước mapping. `TicketSla` dùng `TicketId` làm khóa chính đồng thời là khóa ngoại để biểu diễn quan hệ 1–0..1.
- Các quan hệ hiện biểu diễn bằng ID, chưa thêm navigation properties. Fluent API sẽ khai báo schema, tên snake_case, FK và DeleteBehavior tường minh; không dựa vào convention để suy ra các quan hệ User của Ticket.
- User có thể chưa thuộc phòng ban (`DepartmentId` nullable); SupportTeam bắt buộc thuộc phòng ban. Ticket bắt buộc có category nhưng có thể chưa được giao team/agent.
- `required string` giúp phát hiện bỏ sót thuộc tính khi khởi tạo bằng C#, **không thay thế** validation chuỗi rỗng/độ dài/định dạng ở backend hoặc database.
- Các mốc thời gian dùng `DateTime` UTC; mặc định thời điểm tạo là `DateTime.UtcNow`, caller có thể truyền thời gian UTC cụ thể. Cần validate thời gian đầu vào và ánh xạ `timestamptz` ở bước persistence.
- Ticket mới mặc định `New` và `Normal`. `TicketNumber` chờ database sinh; `Version` chờ ánh xạ `xmin`. Chưa có cơ chế sinh mã hoặc xử lý concurrency ở bước Domain này.
- Chỉ giữ `PasswordHash`/`TokenHash`, không lưu raw password/token. PasswordHash có thể null cho tài khoản chỉ đăng nhập OIDC. `ReplacedByTokenId` chuẩn bị liên kết token sau rotation; logic rotate/revoke chưa triển khai.
- SLA dùng thời lượng phút và snapshot deadline UTC trên TicketSla, theo mô hình 24/7. Chưa gán thời lượng minh họa trong README thành giá trị mặc định. Mapping/validation sẽ cần kiểm tra thời lượng dương và deadline hợp lệ.
- Comment/Attachment có `IsInternal`; attachment có `TicketId`, `CommentId` và người upload. Các field này chưa tự bảo đảm ACL hoặc comment thuộc cùng ticket: cần enforce trong use case và constraints phù hợp.
- Activity dùng `Action` dạng chuỗi vì danh sách action chưa được chốt; `OldValue`/`NewValue` là chuỗi JSON, chờ mapping `jsonb`. `ActorId` nullable cho hành động hệ thống.
- Notification dùng `ReadAt` nullable để xác định chưa đọc; không lưu thêm boolean trùng trạng thái.
- Role constants dùng tên máy `Requester`, `SupportAgent`, `TeamLeader`, `SystemAdmin`. Đây là mã role dự kiến để dùng thống nhất khi seed; hiện chưa có seed hoặc chính sách cấp quyền mặc định.

Đây là mô hình dữ liệu khởi tạo với properties có thể cập nhật. Chưa triển khai domain workflow, kiểm tra phân công, SLA calculation, authorization, audit transaction hay endpoint mới. Không được trả các entity này trực tiếp ra API. Khi triển khai use case, bổ sung validation/domain rules và tests cho chuyển trạng thái, quyền truy cập và các bất biến liên quan.

## Bước tiếp theo

Tạo `HelpdeskDbContext` và `IEntityTypeConfiguration<T>` với EF Core/Npgsql 8.x; cấu hình khóa, FK, unique indexes (email, role name, permission code, provider + subject, token hash, ticket number), giới hạn độ dài và check constraints. Review migration/SQL trước khi apply. Domain hiện chưa tạo hoặc thay đổi database và không cần env var mới.
