# Nền tảng frontend — bước 1

Đã triển khai nền tảng React trong `fe/`: cấu hình môi trường, router/provider, typed API transport, lỗi API, ngày giờ/enum, component dùng chung và bộ test. Trang chủ gọi **API thật** `GET /health/live`, có loading/error/retry, cùng trang 404 cho route chưa tồn tại. Chưa triển khai đăng nhập hoặc các màn hình nghiệp vụ của bước 2 trở đi.

## Chạy local

1. Khởi chạy backend bằng Visual Studio **Container (Dockerfile)**, port 5000.
2. Chạy trong `fe/`:

```powershell
npm ci
npm run dev
```

Mở `http://localhost:5173`. Vite cố định port 5173 và báo lỗi nếu port đã bị chiếm để tránh tự đổi origin ngoài CORS allowlist.

Mặc định không cần tạo file env: browser gọi cùng origin, Vite proxy `/api` và `/health` tới `http://localhost:5000`. Nếu muốn đổi cấu hình, copy [fe/.env.example](../fe/.env.example) thành `fe/.env.local`, sau đó restart Vite:

```dotenv
VITE_API_BASE_URL=/
API_PROXY_TARGET=http://localhost:5000
```

| Biến | Phạm vi | Ý nghĩa |
| --- | --- | --- |
| `VITE_API_BASE_URL` | Public, được bundle vào browser | `/` dùng cùng origin; hoặc origin HTTP(S) như `https://api.example.test`. Không chứa credentials, path `/api/v1`, query hoặc fragment. |
| `API_PROXY_TARGET` | Chỉ Vite dev server | Backend đích của proxy; mặc định port 5000. Nếu chạy profile HTTP khác, đổi giá trị tương ứng. |

API feature dùng path đầy đủ như `/api/v1/tickets`; liveness dùng `/health/live`. Không cấu hình MinIO/PostgreSQL/Redis/JWT signing key trong frontend. Không đọc hoặc tái sử dụng `.env` hạ tầng ở root làm cấu hình browser.

Dev proxy không phải reverse proxy production. Khi build production với `/`, server deploy cần chuyển tiếp `/api` và `/health` về backend, đồng thời fallback các route SPA về `index.html`. Nếu API khác origin, đặt `VITE_API_BASE_URL` trước build, dùng HTTPS và cập nhật CORS allowlist chính xác. `npm run preview` chỉ phục vụ bản build để kiểm tra, không thay thế cấu hình reverse proxy deploy. Hub SignalR chưa tích hợp/proxy ở bước này.

`/health/live` chỉ xác nhận tiến trình API đang phản hồi; không chứng minh database, Redis hoặc MinIO đã sẵn sàng.

## Phiên bản và tương thích

Đã đối chiếu npm registry và peer dependencies ngày 28/10/2026. Giữ React/ReactDOM **19.3.1**, types React/ReactDOM **19.3.0**; các thư viện React đang cài khai báo hỗ trợ React 19. Lockfile là nguồn phiên bản chính xác cho cài đặt bằng `npm ci`.

| Gói | Phiên bản sau kiểm tra | Quyết định |
| --- | --- | --- |
| `@tanstack/react-query` | 5.105.0 | Cập nhật từ 5.104.5 |
| `axios` | 1.21.0 | Cập nhật từ 1.20.0 |
| `react-router-dom` | 7.18.5 | Cập nhật từ 7.18.4 |
| `vite` | 8.3.5 | Đồng bộ manifest với bản đang được lockfile resolve |
| `typescript-eslint` | 8.72.1 | Cập nhật; peer TypeScript hiện `<6.1.0` |
| `postcss` / `autoprefixer` | 8.5.32 / 10.6.2 | Cập nhật patch |
| TypeScript | 6.0.3 | Giữ major 6; chưa nâng 7 ngoài phạm vi tương thích tooling |
| Zustand / Tailwind | 5.0.16 / 4.3.3 | Giữ bản hiện tại |
| SignalR JS | 10.0.11 | Giữ; không phụ thuộc React, kiểm thử giao thức với server 8 ở bước realtime riêng |
| Vitest / Happy DOM | 5.0.5 / 21.0.4 | Thêm môi trường unit/component tests, tương thích Node local |
| Testing Library React / DOM | 16.3.3 / 10.4.2 | Thêm; peer hỗ trợ React 19 |
| Testing Library user-event / jest-dom | 14.6.8 / 7.0.1 | Thêm cho tương tác và accessible assertions |

`package.json` khai báo Node `^22.12.0 || ^24.0.0 || >=26.0.0`, phù hợp Vitest/Vite đang dùng. Đã chạy trên Node **22.18.0** local; `.nvmrc` và CI chọn Node **24**. Giữ `@types/node` major 24 theo CI, không nâng types lên 26 chỉ vì registry có bản mới. Không dùng `--force`/`--legacy-peer-deps` để bỏ qua conflict.

Nguồn tham khảo: [React versions](https://react.dev/versions), [Vitest requirements](https://vitest.dev/guide/), [Testing Library setup](https://testing-library.com/docs/react-testing-library/intro/). Peer dependency cụ thể được đọc từ metadata npm của từng package; hỗ trợ phiên bản khai báo không thay thế kiểm thử tích hợp nghiệp vụ.

## Tổ chức source

```text
fe/src/
  app/
    layouts/        RootLayout tối thiểu và skip link
    providers/      QueryClient factory và provider
    router/         route tree, 404, route error boundary
  features/
    home/           health API → Query hook → page
    tickets/types/  mapping status/priority theo wire contract
  shared/
    api/            Axios transport, ApiError/ProblemDetails, pagination DTO
    config/         public environment validation
    components/     Button, FormField/Input/Select/Textarea, DataTable,
                    Dialog, Pagination, LoadingState/EmptyState/ErrorState
    lib/            date/time helpers
  test/             Vitest/Testing Library setup
```

Không tạo sẵn các module nghiệp vụ rỗng. `App.tsx` chỉ lắp provider/router. State server nằm trong TanStack Query; Zustand chưa cần dùng ở bước này. TypeScript strict và `noUncheckedIndexedAccess` được bật; cấu hình test tách khỏi browser app để Node types dùng cho contract tests không tràn vào mã browser.

## API, lỗi và cache

- `createHttpClient` trả `request/get/post/put/patch` có generic response/body, nhận query/signal/responseType qua options. Ví dụ `apiClient.get<PagedResult<TicketListItem>>('/api/v1/tickets', { params, signal })` khi module Ticket được triển khai.
- Request path phải là path bắt đầu bằng một `/` thuộc backend đã cấu hình; chặn URL ngoài origin, `//`, backslash hoặc whitespace. Request mặc định timeout 15 giây, `withCredentials: false`, Accept JSON. Không ép Content-Type JSON cho FormData.
- DTO generic chỉ kiểm tra TypeScript lúc phát triển, không phải runtime validation. Feature phải kiểm tra payload khi contract yêu cầu; health API kiểm tra plaintext `Healthy`, không nhận nhầm HTML fallback thành backend khỏe.
- Lỗi được chuẩn hóa thành `ApiError`: kind, HTTP status, business code, traceId, field errors và Retry-After nếu có. Lỗi HTTP dùng status response; không tin status trong JSON để thay thế transport status. Không giữ Axios config/request chứa credential/body trong error object.
- `getFieldError(error, 'displayName')` ánh xạ field ASP.NET không phân biệt hoa/thường và hỗ trợ tiền tố `$.`; có thể truyền đầy đủ path cho field lồng nhau. Không render HTML từ lỗi hoặc nội dung người dùng.
- Query staleTime mặc định 30 giây, gcTime 5 phút, refetch khi focus/reconnect. Query chỉ retry **một lần** với lỗi network/timeout hoặc HTTP 502/503/504; không retry tự động 401/403/404/409/429/cancelled. Mutation không retry.
- `createQueryClient` tạo instance riêng cho provider/test. Hiện chỉ cache health công khai. Khi làm auth ở bước 2, cần bổ sung scope user/session vào query keys nghiệp vụ, hủy request/xóa cache khi kết thúc phiên và điều phối refresh một lần; bước này chưa có interceptor refresh hoặc lưu token.

## Enum, ngày giờ và component

- Ticket status/priority dùng object `as const` với JSON number đúng backend, label tiếng Việt, fallback khi nhận giá trị mới. Contract test đọc enum C# thực tế để phát hiện drift; không sao chép workflow/ACL backend vào frontend.
- `toApiTimestamp` yêu cầu timestamp ISO có offset hoặc Date hợp lệ và chuyển thành UTC; không âm thầm coi `datetime-local` là UTC. Form tương lai cần chuyển input local sang Date/offset có chủ đích.
- `formatDateTime` hiển thị theo timezone browser hoặc timezone truyền vào, xử lý null/sai định dạng bằng `—`. `formatUtcDay` giữ ngày UTC của daily report, không dịch ngày theo timezone người xem.
- `FormField` liên kết label, hint, lỗi và required với control qua render props. Button pending ngăn submit trùng; nút mặc định `type="button"`, nút submit phải ghi rõ type.
- DataTable có header/caption, vùng scroll bàn phím, trạng thái loading/error/empty; không hiển thị row cũ khi đang lỗi/loading. Pagination nhận page/pageSize/totalCount của server và chỉ phát thay đổi page, không phân trang mảng dữ liệu ở browser.
- Dialog dùng native `showModal()`/`close()` theo controlled state, label/description, Escape và nút đóng; trình duyệt quản lý modal focus và trả focus về nút mở. Kiểm tra bàn phím trên trình duyệt thật trước khi thay đổi hành vi dialog.

## Kiểm tra và kết quả

Chạy từ `fe/`:

```powershell
npm ci
npm run lint
npm test
npm run build
```

`npm run test:watch` dùng khi phát triển. CI job **Frontend lint and build** đã bổ sung `npm test` giữa lint và build; giữ tên status check hiện tại. Test chạy độc lập, không ghi dữ liệu backend hoặc cần mật khẩu/token thật.

Kết quả lượt triển khai: **51 tests / 9 files đạt**, ESLint và TypeScript/Vite build đạt trên máy local. Bao phủ normalization/validation/traceId, chặn request ngoài origin, multipart, cancellation, mutation không retry, query retry policy, enum parity, UTC/date, form/pagination/dialog/table và router/API states. Kiểm tra npm không báo dependency conflict hoặc vulnerability tại lượt chạy này.

Đã kiểm tra local bằng Edge headless: API thật qua Vite proxy trả `200 Healthy`, trang chủ desktop/mobile không overflow ngang, skip link, deep-link/reload/404, dialog keyboard/Escape/focus restore, lỗi mạng và retry; không có page error. Browser smoke dùng công cụ tạm ngoài repo, chưa phải E2E suite chạy trên CI. Chưa chạy workflow GitHub từ xa hoặc nghiệm thu auth/SignalR/nghiệp vụ ở các bước sau.

Bước tiếp theo: [bước 2 — auth/session](frontend-roadmap.md), sau đó bổ sung layout/menu/permission theo bước 3. Không cần migration, seed lại database hoặc đổi secret cho nền tảng frontend này.
