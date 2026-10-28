# Dữ liệu nghiệp vụ mẫu cho Development

Profile Visual Studio **Container (Dockerfile)** đã bật `DevelopmentData__SeedOnStartup=true` trong launchSettings. Sau khi các containers sẵn sàng và đã áp dụng đầy đủ sáu migrations hiện tại, F5 sẽ chạy lần lượt:

1. Seed role/permission Identity còn thiếu.
2. Bootstrap admin đầu tiên từ user-secrets nếu chưa có admin.
3. Seed bộ dữ liệu nghiệp vụ mẫu nếu database nghiệp vụ còn trống.

Không cần migration mới hoặc chạy lệnh seed riêng. Cấu hình JWT, BootstrapAdmin và connection string tiếp tục dùng user-secrets/environment như [Identity foundation](backend-identity-foundation.md). Bootstrap không tự migrate database.

## Bộ mẫu

| Dữ liệu | Nội dung |
| --- | --- |
| 3 phòng ban | `[Mẫu] Công nghệ thông tin`, `[Mẫu] Nhân sự`, `[Mẫu] Hành chính` |
| 3 team | IT Helpdesk, Hỗ trợ nhân sự, Hỗ trợ văn phòng; mỗi team thuộc phòng ban mẫu tương ứng |
| 3 membership | Admin ban đầu là thành viên active và leader của cả ba team |
| 7 danh mục | 3 root theo lĩnh vực; 4 node con: Mạng và VPN, Tài khoản và email, Chấm công, Thiết bị văn phòng |
| 5 Ticket | Wi-Fi (`New`), cấu hình email (`InProgress`), máy in (`WaitingForRequester`), VPN (`Resolved`), tra cứu chấm công (`Closed`) |
| 17 activity | Created cho cả 5 Ticket; assignment và các bước chuyển trạng thái cho 4 Ticket đã xử lý |

RequesterId và CreatedByUserId của tất cả Ticket là admin active được chọn theo CreatedAt rồi Id. Không tạo thêm user, role hoặc mật khẩu demo; không sửa DepartmentId, password, grant hoặc trường khác của admin. Admin cần có `ticket.read.all` và `ticket.update.status` để là agent hợp lệ trên Ticket mẫu. Nếu quyền đã được chỉnh thiếu hai quyền này, seed dừng và giữ nguyên grants.

Ticket mới New chưa phân công; bốn Ticket còn lại được gán cho team tương ứng và admin làm agent. TicketNumber do PostgreSQL sinh, không gán số cố định hoặc reset sequence. Các bước trạng thái dùng TicketWorkflow; Resolved/Closed có summary và timestamps hợp lệ. Activity dùng action/JSON contract hiện có; ActorId=null để thể hiện khởi tạo bởi hệ thống. Không giả lập user session hoặc thay ACL của API. Có thể login bằng admin đã cấu hình để xem dữ liệu.

Không seed comment/internal note, file MinIO, SLA snapshot, notification hoặc tài khoản requester/agent riêng. Admin cũng là requester của Ticket mẫu nên quy tắc requester không được xem nội bộ vẫn giữ nguyên. Muốn thử ACL giữa nhiều người, tạo user/role/membership theo các API quản trị hiện có.

## Chạy lặp và giữ dữ liệu

Initializer chỉ tạo **toàn bộ bộ mẫu** khi cả bốn bảng Department, SupportTeam, TicketCategory và Ticket đều chưa có bản ghi. Bất kỳ bảng nào đã có dữ liệu thì bỏ qua toàn bộ bộ mẫu, không bù lại các node đã xóa, không kích hoạt lại phòng ban/team, không sửa tên hoặc khôi phục status Ticket đã đổi. Không chỉ kiểm tra số Ticket rồi ghi thêm vào tổ chức đã dùng.

Seed dùng transaction và advisory lock 734205001 chung với Identity/tổ chức/Ticket. Hai API khởi động đồng thời chỉ một bên tạo bộ mẫu; bên sau kiểm tra lại bên trong lock và bỏ qua. Cả tổ chức, danh mục, Ticket và activity cùng transaction; lỗi sau save SQL vẫn rollback toàn bộ bộ mẫu. Identity/admin đã seed trước đó giữ nguyên khi transaction dữ liệu mẫu lỗi. Sequence có thể có khoảng trống sau rollback như thao tác Ticket bình thường.

Log khởi động báo đã tạo bộ mẫu hoặc đã bỏ qua vì có dữ liệu nghiệp vụ; không ghi password, JWT hoặc cấu hình bí mật. Database phải có ít nhất một SystemAdmin active khi khởi tạo mẫu; không tự nâng quyền user sẵn có hoặc mở lại admin inactive.

## Bật/tắt

Để tắt riêng dữ liệu mẫu, đổi trong profile Container của `be/Properties/launchSettings.json`:

```json
"DevelopmentData__SeedOnStartup": "false"
```

Hai cờ seed Identity/bootstrap admin vẫn có thể giữ true. Tắt cờ không xóa dữ liệu mẫu đã tạo.

Key `DevelopmentData:SeedOnStartup` mặc định false khi không cấu hình, chỉ profile Container bật sẵn. Profile http/https và API Compose không tự bật. Nếu muốn bật ở luồng Development khác, dùng environment `DevelopmentData__SeedOnStartup=true`, chuẩn bị Identity/bootstrap hoặc admin có sẵn. Mọi đường gọi initializer đều từ chối môi trường khác Development, kể cả gọi trực tiếp qua DI.

Sau khi xóa volume PostgreSQL: giữ user-secrets, khởi động lại hạ tầng → apply các SQL migrations đã review → F5 Container. Identity, admin và bộ mẫu được tạo lại trên database trống. Nếu giữ volume dữ liệu cũ, seed không reset hoặc nhân đôi bộ mẫu. Không cần xóa/reset database để bật tính năng này trên môi trường đang dùng.

## Kiểm chứng

```powershell
dotnet build be/be.sln --configuration Release --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Tests dùng database con tên `helpdesk_mapping_test_samples_<GUID>` trong PostgreSQL test, không dùng database Helpdesk thật. Kiểm tra startup Identity → admin → sample, login đọc list Ticket theo ACL, số/quan hệ/trạng thái/activity, seed đồng thời, giữ sửa đổi và grants, bỏ qua database đã có nghiệp vụ, từ chối admin không hợp lệ/Production/migration pending và rollback sau SQL trước commit.

Kết quả lượt triển khai: build Release với -warnaserror đạt 0 warning/error; toàn bộ 443/443 tests đạt, 0 skipped, cùng smoke test CLI seed/grant trên containers tạm. Chưa chạy seed trên database Helpdesk hiện có; profile Container sẽ thực hiện ở lần F5 tiếp theo theo các điều kiện trên.
