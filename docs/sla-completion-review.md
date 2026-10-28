# Kiểm tra hoàn thành SLA MVP — phần 16

Đã đọc lại các điểm nối snapshot → public response → workflow → overdue worker → notification, và chạy lại **21/21 tests SLA/rules/model, 0 skipped** trước khi triển khai phần 17.

| Yêu cầu | Kết quả kiểm tra |
| --- | --- |
| Policy theo priority | Một active policy/priority, quyền ticket.manage.sla, targets được validate, xmin/audit và unique race được test |
| Tính 24/7 và snapshot | UTC elapsed minutes, snapshot tên/priority/targets/deadline cùng transaction tạo Ticket; không gán hồi tố |
| First public Agent response | Requester/internal/bystander không đủ scope không được tính; chỉ public processor hợp lệ, ghi một lần và rollback atomic |
| Resolve/reopen | Dừng clock khi resolve, resume deadline gốc khi reopen; giữ first response và breach lịch sử |
| Worker overdue | Batch, lock DB, idempotent, không ghi lại marker/activity/notification; Closed/Cancelled được bỏ qua |
| SQL và bảo toàn dữ liệu | SlaMvp snapshot/SQL khớp EF Core 8; apply/replay/rollback trong DB test; rollback dữ liệu mới bị chặn |

Read-only migration history PostgreSQL local xác nhận đã có `20261028122927_SlaMvp`, đúng thông tin người dùng đã migrate. **Hiện có 0 policy active**: đây là cấu hình nghiệp vụ chưa chọn, không phải lỗi migration. Muốn Ticket mới có SLA cần quản trị tạo policy active theo [backend-sla.md](backend-sla.md); không tự dùng các mức minh họa trong README hoặc ghi lại SLA Ticket cũ.

Phần 16 đáp ứng phạm vi backend MVP, đủ làm nguồn cho [dashboard/báo cáo phần 17](backend-reporting.md). Các metric báo cáo đã tách không có SLA/cancelled/pending/stopped/legacy unknown để không gọi tất cả là “đạt SLA”. Không nâng cấp/reset database, tạo policy hoặc chỉnh deadline thật trong lượt rà soát này.

Business-hour calendar, pause, backfill/reapply, escalation, recurring reminder và UI SLA nằm ngoài MVP đã chốt; tài liệu phần 16 tiếp tục mô tả những giới hạn này.
