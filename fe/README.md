# Helpdesk frontend

React 19.3.1 + TypeScript + Vite. Đã có nền tảng router/provider, typed API client, lỗi API, component dùng chung và unit/component tests. Chưa triển khai auth hoặc các màn hình nghiệp vụ.

```powershell
npm ci
npm run dev
```

Mở `http://localhost:5173`; backend Visual Studio **Container (Dockerfile)** chạy tại port 5000. Vite proxy `/api` và `/health` về backend. Có thể copy `.env.example` thành `.env.local` để đổi cấu hình; restart Vite sau khi đổi. `VITE_*` là cấu hình public, không chứa secrets.

```powershell
npm run lint
npm test
npm run build
```

Node 22.12+ trong major 22, Node 24 hoặc 26+ theo `package.json`; `.nvmrc`/CI chọn Node 24. `npm run test:watch` dùng khi phát triển.

- [Cấu trúc, phiên bản, API/cache và cách cấu hình](../docs/frontend-foundation.md)
- [Lộ trình và tiêu chí nghiệm thu](../docs/frontend-roadmap.md)
- [Quy tắc repository](../guides.md)

Production cần SPA fallback và reverse proxy cho `/api`, `/health`, hoặc build với origin API riêng và CORS phù hợp. Dev proxy không thay thế reverse proxy production. Test enum đối chiếu source `be/Domain/Enums` trong cùng monorepo; chạy từ thư mục `fe/`.
