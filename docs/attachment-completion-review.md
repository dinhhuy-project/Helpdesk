# Kiểm tra hoàn thành phần 14 — File đính kèm MinIO

Đã rà soát lại source và chạy nhóm kiểm thử attachment/MinIO/model khi bắt đầu phần 15: **36/36 đạt, 0 skipped**, dùng PostgreSQL và MinIO thật trong containers tạm.

| Yêu cầu | Kết quả |
| --- | --- |
| Bucket private | Bootstrap/SDK kiểm tra private và unversioned, anonymous đọc object bị chặn, API dùng app user theo prefix |
| Giới hạn file | Tối đa 10 MiB, allowlist extension/MIME/signature, UTF-8 text và filename/path; quota public/internal riêng, tính cả upload pending |
| Metadata và upload/download ACL | Ticket scope và grant hiện tại, session/user active, linked comment cùng Ticket/author/visibility; recheck sau storage I/O |
| Internal visibility | Requester không đọc được internal file qua list/detail/download/activity; metadata không lộ bucket/object key/credential |
| Object mồ côi | Journal upload durable, cleanup claim/lease, finalize bị chặn khi GC claim; xóa lỗi được retry, metadata commit được bảo toàn |
| Transaction và rollback | Metadata/activity cùng transaction, lỗi SQL rollback; rollback migration chặn khi journal còn intent |

Nhóm kiểm thử bao gồm SDK MinIO, HTTP upload/download, validation, quota, mất quyền giữa upload và finalize/download, GC race và retry. Khi rà soát có phát hiện helper test expiry dùng clock PostgreSQL trong expression nhưng cleanup dùng clock host; helper đã được chỉnh lấy UTC trước query để không phụ thuộc lệch đồng hồ container. Không cần sửa luồng storage nghiệp vụ.

Phần 14 đáp ứng phạm vi backend MVP và có thể chuyển sang [phần 15](backend-notifications.md). Kiểm tra read-only migration history PostgreSQL local cũng thấy `20261026171234_AttachmentStorage`; migration mới NotificationDelivery chưa được apply. [Review/apply AttachmentStorage](attachment-storage-review.md) vẫn là thao tác riêng với database khác; suite không thay thế kiểm tra cấu hình của môi trường chạy. Không reset database/volumes ứng dụng trong lượt kiểm tra này.

AV, TLS, readiness/giám sát và backup/restore MinIO vẫn thuộc phần chuẩn bị vận hành, chưa được coi là đã hoàn thiện production. Cấu hình chi tiết xem [backend-attachments.md](backend-attachments.md).
