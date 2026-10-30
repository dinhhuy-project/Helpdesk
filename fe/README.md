# Helpdesk frontend

React 19.3.1 + TypeScript + Vite. Đã có nền tảng, auth/session, shell/permission, Ticket list/detail/create/discussion/workflow/attachments/SLA, quản trị tài khoản/role/grant/audit, phòng ban/team/membership/category và SLA policy theo scope/permission server. Có notification/SignalR và dashboard/báo cáo dùng aggregation server theo cùng cohort/ACL/filter. File upload/download qua Bearer API, giữ ACL Ticket/comment và visibility.

```powershell
npm ci
npm run dev
```

Mở `http://localhost:5173`; backend Visual Studio **Container (Dockerfile)** chạy tại port 5000. Vite proxy `/api`, `/health` và `/hubs` (hỗ trợ WebSocket) về backend. SignalR tự nối theo phiên, refresh/reconnect/rewatch và refetch cache qua REST/ACL. Có thể copy `.env.example` thành `.env.local` để đổi cấu hình; restart Vite sau khi đổi. `VITE_*` là cấu hình public, không chứa secrets.

```powershell
npm run lint
npm test
npm run build
```

Node 22.12+ trong major 22, Node 24 hoặc 26+ theo `package.json`; `.nvmrc`/CI chọn Node 24. `npm run test:watch` dùng khi phát triển.

- [Cấu trúc, phiên bản, API/cache và cách cấu hình](../docs/frontend-foundation.md)
- [Đăng nhập, phiên, tài khoản và đổi mật khẩu](../docs/frontend-authentication.md)
- [Khung ứng dụng, permission guards và kiểm tra SignalR JS 10/server 8](../docs/frontend-shell.md)
- [Danh sách/chi tiết Ticket, filter trên URL và activity](../docs/frontend-tickets.md)
- [Tạo Ticket, cây danh mục phân trang và xử lý kết quả chưa rõ](../docs/frontend-create-ticket.md)
- [Comment/internal note/activity, cache theo visibility và nghiệm thu hai tài khoản](../docs/frontend-ticket-discussion.md)
- [Quản trị tài khoản/role/grant/audit, phòng ban và phiên bị thu hồi](../docs/frontend-accounts.md)
- [Phòng ban/team/membership/category, leadership và version](../docs/frontend-catalogs.md)
- [Phân công/workflow, lookup theo Ticket và xử lý conflict](../docs/frontend-ticket-workflow.md)
- [File đính kèm Ticket/comment, multipart, visibility và download Blob](../docs/frontend-attachments.md)
- [SLA snapshot/deadline, timezone và quản trị policy/version](../docs/frontend-sla.md)
- [Chuông/inbox/chi tiết notification và đồng bộ trạng thái đọc qua REST](../docs/frontend-notifications.md)
- [SignalR theo phiên, refresh/reconnect/rewatch và cache/ACL](../docs/frontend-realtime.md)
- [Dashboard/báo cáo theo cohort và ACL, phân trang group, SLA và daily UTC](../docs/frontend-reporting.md)
- [Lộ trình và tiêu chí nghiệm thu](../docs/frontend-roadmap.md)
- [Quy tắc repository](../guides.md)

Production cần SPA fallback và reverse proxy cho `/api`, `/health`, hoặc build với origin API riêng và CORS phù hợp. Dev proxy không thay thế reverse proxy production. Test enum đối chiếu source `be/Domain/Enums` trong cùng monorepo; chạy từ thư mục `fe/`.
