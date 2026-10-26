# GitHub Actions

Workflow [CI](../.github/workflows/ci.yml) chạy khi push lên bất kỳ branch, mở/cập nhật pull request và khi chọn **Run workflow** trong tab **Actions**. Một lượt chạy mới trên cùng branch/PR hủy lượt cũ đang chạy. Không lọc đường dẫn để các status check luôn xuất hiện, kể cả PR chỉ sửa tài liệu.

## Các kiểm tra

| Job / status check | Nội dung |
| --- | --- |
| `Backend build and tests` | Restore, build Release với warning là lỗi; chạy toàn bộ xUnit với PostgreSQL 16; smoke test CLI `seed-identity`; kiểm tra publish API |
| `Frontend lint and build` | Node.js 24, `npm ci` theo lockfile, ESLint, TypeScript và Vite build |
| `Docker and Compose validation` | Kiểm tra Compose bằng `.env.example` và giá trị giả dành cho CI; build image từ `be/Dockerfile` với context `be/` |

Backend dùng SDK .NET 8 được chọn bằng `global.json` tạo riêng trên runner. File này không được thêm vào repository và không thay cách chọn SDK trên máy phát triển. Frontend dùng cache npm theo `fe/package-lock.json`. Hiện frontend chưa có test runner nên CI chỉ chạy các script lint/build đang tồn tại.

PostgreSQL là service container mới cho từng job, không mount volume và dùng database `helpdesk_mapping_test_ci`, phù hợp điều kiện an toàn của bộ test. Suite kiểm tra migration/rollback, constraint, Identity/auth/session/permission, quản trị tài khoản (scope, version, audit, last-admin race, password revoke), Department, SupportTeam/membership (leader scope, version, deactivate/remove giữ lịch sử), TicketCategory (active ancestors, tên sibling/root, reparent/cycle race) và ACL Ticket (list/detail/checker cùng scope, negative writes/destination, quyền thay đổi, version race). Migration tests tạo thêm database con tạm trong chính service PostgreSQL để kiểm tra dữ liệu legacy; dùng tài khoản postgres của service test. CLI seed chạy sau test trên cùng database để kiểm tra tính idempotent. Không gọi script PowerShell tạo container lồng nhau và không cần Redis/MinIO để chạy các test hiện có.

## Secrets và artifact

Không cần tạo GitHub Actions Secrets cho workflow CI hiện tại. Mật khẩu `ci-test-only` là dữ liệu công khai chỉ dùng cho PostgreSQL tạm và việc kiểm tra cú pháp Compose; JWT trong test do test suite cung cấp. Workflow không đọc `.env` hay user-secrets trên máy phát triển, không bootstrap admin thật và không kết nối database Helpdesk đang dùng.

Trong trang kết quả mỗi run có artifact giữ 7 ngày:

- `backend-test-results`: kết quả `.trx`, được upload cả khi test lỗi nếu đã tạo file.
- `frontend-dist`: bản build tĩnh của frontend khi job thành công. Đây là artifact để tải về kiểm tra, chưa phải một deployment.

Workflow chỉ cấp `contents: read`, checkout không giữ Git credentials, các action được cố định bằng commit SHA. [Dependabot](../.github/dependabot.yml) kiểm tra cập nhật action mỗi tháng và gom thành PR để review.

## Bật trên GitHub

1. Commit và push các thay đổi liên quan cùng thư mục `.github/` lên repository GitHub. Workflow dùng source đã commit; file còn untracked trên máy sẽ không có trên runner.
2. Mở **Actions → CI** để xem ba job. Nếu Actions đang bị tắt, bật tại **Settings → Actions → General**. Nếu tổ chức hạn chế action, cần cho phép các action chính thức `actions/checkout`, `actions/setup-dotnet`, `actions/setup-node`, `actions/upload-artifact`.
3. Có thể cấu hình ruleset/branch protection cho `main` hoặc `develop`, yêu cầu ba status check trong bảng trên pass trước khi merge. Thực hiện sau khi workflow đã chạy lần đầu để GitHub nhận diện tên check.
4. Muốn chạy thủ công, merge workflow vào default branch trước rồi chọn **Actions → CI → Run workflow**.

Workflow hiện kiểm tra và build, chưa push image, triển khai server hay tự apply migration vào database dùng chung. Khi xác định môi trường deploy/registry, bổ sung job deploy riêng với environment và secrets tương ứng.

## Kiểm tra tại máy phát triển

```powershell
dotnet restore be/be.sln
dotnet build be/be.sln --configuration Release --no-restore -warnaserror
& ./be/tests/Run-PersistenceTests.ps1
```

Script test local tạo PostgreSQL riêng bằng Docker và dọn container đó sau khi chạy; nó không dùng database ứng dụng. Trên CI, job chạy `dotnet test` trực tiếp với service container thay cho script này.

```powershell
cd fe
npm ci
npm run lint
npm run build
```

Kiểm tra cú pháp workflow nếu đã có [actionlint](https://github.com/rhysd/actionlint):

```powershell
actionlint .github/workflows/ci.yml
```

Tham khảo: [PostgreSQL service container](https://docs.github.com/en/actions/tutorials/use-containerized-services/create-postgresql-service-containers), [setup-dotnet và lựa chọn SDK](https://github.com/actions/setup-dotnet), [setup-node và cache npm](https://github.com/actions/setup-node).
