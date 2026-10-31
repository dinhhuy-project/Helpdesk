# Review SQL — PublicCommunity

Migration: `20261031095438_PublicCommunity`, sau `20261028122927_SlaMvp`.

- [SQL forward idempotent](../be/Infrastructure/Persistence/Migrations/Sql/PublicCommunity.sql)
- [SQL rollback](../be/Infrastructure/Persistence/Migrations/Sql/PublicCommunity.Rollback.sql)
- [Chức năng, phân quyền và vận hành](public-community.md)

## Thay đổi schema

Tạo bốn bảng trong schema `helpdesk`, không sửa bảng Ticket, user, refresh token hoặc metadata MinIO:

| Bảng | Mục đích / ràng buộc |
| --- | --- |
| `community_entries` | Bài tri thức hoặc sự cố; bản soạn và snapshot công khai riêng; `Version` ánh xạ `xmin`; trạng thái/kind/snapshot có CHECK. |
| `incident_updates` | Timeline đã công khai, unique `(entry_id, revision)`. |
| `incident_affected_users` | Xác nhận ảnh hưởng, PK `(entry_id,user_id)` chống đếm trùng. |
| `knowledge_feedback` | Hữu ích/tự giải quyết, PK `(entry_id,user_id)`; solved yêu cầu helpful. |

Tất cả FK `ON DELETE RESTRICT`; `source_ticket_id` nullable chỉ cho Knowledge và chỉ xuất hiện trong DTO quản trị. Bản công khai không có author, requester, email, TicketId, comment, attachment hoặc object key.

Indexes: kind/ngày xuất bản cho danh sách public, kind/ngày cập nhật cho quản trị, FK, timeline unique và GIN expression index **chỉ trên cột snapshot công khai, chỉ khi `is_published`**. Không index bản nháp vào corpus tìm kiếm public.

FTS dùng cấu hình PostgreSQL `simple`, query `plainto_tsquery` với tham số; nhiều từ khóa kết hợp AND. Không cài extension, không gọi AI hoặc dịch vụ ngoài. Cách mapping expression index theo [Npgsql documentation](https://www.npgsql.org/efcore/mapping/full-text-search.html). Có kiểm thử EXPLAIN SQL do EF sinh với `enable_seqscan=off` để kiểm tra index đủ điều kiện sử dụng; planner vẫn có thể chọn sequential scan trên bảng nhỏ.

## Review thao tác

Forward nằm trong transaction, có điều kiện migration history; không có DELETE/TRUNCATE/UPDATE dữ liệu cũ, không reset TicketNumber, không copy dữ liệu Ticket ra công khai và không tự cấp permission. `CREATE INDEX` chỉ áp dụng trên bảng mới, chưa có corpus lớn khi migration chạy.

Rollback xóa **bốn bảng mới và toàn bộ dữ liệu cộng đồng trong đó**. Chỉ dùng trên database thử nghiệm hoặc khi đã backup/xác nhận chấp nhận mất dữ liệu mới. Với dữ liệu đang vận hành, ưu tiên migration sửa tiếp và thao tác thu hồi nội dung, không rollback tùy tiện.

Không tự migrate khi API khởi động. Trước khi áp dụng lên môi trường khác: backup, kiểm tra migration history, review SQL, chạy forward đúng database rồi thực hiện seed/grant theo tài liệu chức năng.

## Kiểm tra cần đối chiếu

- Model snapshot và forward/rollback SQL khớp model trong `ModelTests`.
- Khách không đăng nhập chỉ đọc snapshot đang published; draft và nội dung bị thu hồi trả 404.
- Sửa bản soạn không thay đổi snapshot/search/timeline public.
- Publish yêu cầu `ReviewedForPublic=true`, đúng version và permission hiện tại.
- SignalR chỉ gửi `{kind,id}` sau commit; không có phương thức tham gia nhóm Ticket.
- Dữ liệu cũ vẫn nguyên vẹn; cấp bốn permission mới là lệnh riêng, giữ các grant đã chỉnh sửa.

## Áp dụng local ngày 31/10/2026

Đã review SQL, backup, chạy forward bằng `psql -v ON_ERROR_STOP=1` trong PostgreSQL Compose cục bộ. Migration đã có trong history. Tài khoản/Ticket và grant cũ được đối chiếu digest trước/sau, không thay đổi; bốn quyền mới được cấp riêng cho SystemAdmin. Corpus mẫu chỉ gồm nội dung biên soạn mới, không có liên kết nguồn Ticket cũ. Xem [bằng chứng kiểm tra chức năng](public-community.md#kích-hoạt-trên-workspace-cục-bộ-ngày-31102026).
