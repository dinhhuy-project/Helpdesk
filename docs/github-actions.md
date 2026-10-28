# GitHub Actions

Workflow [CI](../.github/workflows/ci.yml) chạy khi push lên bất kỳ branch, mở/cập nhật pull request và khi chọn **Run workflow** trong tab **Actions**. Một lượt chạy mới trên cùng branch/PR hủy lượt cũ đang chạy. Không lọc đường dẫn để các status check luôn xuất hiện, kể cả PR chỉ sửa tài liệu.

## Các kiểm tra

| Job / status check | Nội dung |
| --- | --- |
| `Backend build and tests` | Restore, build Release với warning là lỗi; chạy toàn bộ xUnit với PostgreSQL 16 và MinIO tạm; smoke test CLI `seed-identity`; kiểm tra publish API |
| `Frontend lint and build` | Node.js 24, `npm ci` theo lockfile, ESLint, Vitest unit/component tests, TypeScript và Vite build |
| `Docker and Compose validation` | Kiểm tra Compose bằng `.env.example` và giá trị giả dành cho CI; build image từ `be/Dockerfile` với context `be/` |

Backend dùng SDK .NET 8 được chọn bằng `global.json` tạo riêng trên runner. File này không được thêm vào repository và không thay cách chọn SDK trên máy phát triển. Frontend dùng cache npm theo `fe/package-lock.json`, đã có `npm test` chạy Vitest/Testing Library với Happy DOM, không cần API hoặc credential thật. Giữ tên status check hiện tại; xem [nền tảng frontend](frontend-foundation.md).

PostgreSQL là service container mới cho từng job, không mount volume và dùng database `helpdesk_mapping_test_ci`, phù hợp điều kiện an toàn của bộ test. Suite kiểm tra migration/rollback, constraint, Identity/auth/session/permission, quản trị tài khoản (scope, version, audit, last-admin race, password revoke), Department, SupportTeam/membership (leader scope, version, deactivate/remove giữ lịch sử), TicketCategory (active ancestors, tên sibling/root, reparent/cycle race) và ACL Ticket (list/detail/checker cùng scope, negative writes/destination, quyền thay đổi, version race). Migration tests tạo thêm database con tạm trong chính service PostgreSQL để kiểm tra dữ liệu legacy; dùng tài khoản postgres của service test. CLI seed chạy sau test trên cùng database để kiểm tra tính idempotent. CI chạy dotnet test trực tiếp, không gọi script PowerShell; Redis chưa cần cho suite.

Phần 11 thêm test tạo Ticket (danh tính từ phiên, từ chối field server, category/ancestor, số database sinh khi tạo đồng thời, activity và rollback), filter UTC/sort/phân trang trong read scope và input không hợp lệ. Workflow tự chạy các test mới, không cần secret hoặc job mới.

Phần 12 thêm unit tests bảng transition và integration tests route assign/transition: permission/resource scope, current assignment inactive, resolution/confirm/reopen, cùng version cạnh tranh, activity/SLA rollback sau SQL save và grant workflow chủ động/idempotent. Catalog hiện có 17 policy. Script PowerShell local còn smoke test grant-ticket-workflow trên PostgreSQL tạm; CI chạy các integration tests tương ứng cùng smoke seed hiện có.

Phần 13 thêm integration tests comment/internal note/timeline: author từ phiên, strict body, public/internal grants, requester nhiều role vẫn bị chặn internal, visibility trước count/page, nested ID sai Ticket, quyền/membership bị thu hồi, history terminal, append đồng thời, rollback sau SQL save, audit whitelist và unknown action không xuất API. Không đổi migration/permission catalog hoặc workflow CI.

Seed dữ liệu Development có tests riêng trên database con tạm: startup Identity/admin trước mẫu, login đọc Ticket, seed đồng thời, giữ dữ liệu/grant đã sửa, bỏ qua bảng nghiệp vụ đã dùng, từ chối Production/admin không hợp lệ/migration pending và rollback cả bộ mẫu sau SQL save. Test factory mặc định tắt DevelopmentData:SeedOnStartup để các suite khác không tự nhận dữ liệu mẫu; chỉ bài kiểm tra seed chủ động bật cờ.

## Secrets và artifact

Phần 14 build MinIO từ `deploy/minio`, chạy container riêng không volume trên loopback 19000 và dừng bằng step `always()`. Backend job có timeout 35 phút cho lần build source đầu. SDK/API tests dùng bucket tên ngẫu nhiên: upload/download thật, anonymous bị chặn, bucket public bị từ chối; các tests khác kiểm tra ACL, quota, rollback metadata/activity và cleanup journal. Migration AttachmentStorage được apply/replay/rollback chỉ trong database tạm. Credentials MinIO CI là dữ liệu công khai của container tạm; không dùng bucket hay root credential của môi trường ứng dụng.

Phần 15 thêm HTTP/SignalR integration tests trong cùng suite, test client `Microsoft.AspNetCore.SignalR.Client` 8.0.32 qua TestServer LongPolling: owner/current ACL/internal visibility, pagination/mark-read, activity+notification rollback, lease/retry và dispatch sau commit, JWT/group/session revoke. NotificationDelivery được kiểm tra SQL forward/replay/rollback và guard khi có dữ liệu. Test factory tắt dispatch tự động để điều khiển thời điểm gửi; test vẫn dùng registry/dispatcher và hub thật. Không cần Redis/backplane, secrets mới hoặc job CI riêng. Chưa kiểm thử reverse proxy WebSocket production.

Bước 3 frontend thêm test JS SignalR 10.0.11 với hub ứng dụng .NET 8 qua loopback Kestrel bridge/LongPolling, JWT/login/refresh/logout và group ACL thật trên PostgreSQL test. Backend job cài Node 24 và `npm ci --prefix fe` để chạy test này trong suite; credential fixture chỉ qua stdin, không dùng secrets hoặc DB Helpdesk thật. Local chạy riêng `./be/tests/Run-SignalRCompatibilityTests.ps1` sau `npm ci` ở fe; xem [phạm vi đã xác minh và giới hạn](frontend-shell.md).

Phần 16 thêm SLA rules/integration tests: policy permission/version/active priority race, snapshot 24/7 bất biến và no backfill, first response public theo quyền/phạm vi, resolve/reopen, worker batch/idempotence/terminal skip và rollback sau SQL. SlaMvp forward/replay/rollback chạy trong DB test; rollback khi có snapshot mới bị chặn. Factory tắt worker tự động để kiểm soát clock/thời điểm xử lý; production mặc định bật. Không thêm package, container hoặc secret cho SLA.

Phần 17 thêm tests dashboard/report theo own/team/all, negative filters, role/membership/session thay đổi và scope đổi ngay giữa aggregate, group pagination/unassigned/inactive history, UTC/day/midnight, SLA denominator/average và parity Domain. Query-plan test tạo database con test GUID, seed 8.000 Ticket + snapshot, EXPLAIN ANALYZE/BUFFERS SQL thật trước/sau candidate index rồi drop đúng DB con. Không thay index hoặc schema database Helpdesk; CI vẫn dùng PostgreSQL test có quyền tạo DB con như các bài legacy/seed. Artifact JSON trong bin/ là dữ liệu test, không cần secrets hoặc workflow mới.

MinIO image chứa client và entrypoint bootstrap; Compose healthcheck chờ cấu hình attachment hoàn tất. Với .env.example, storage mặc định disabled và app credentials trống vẫn hợp lệ. Docker job có timeout 25 phút và chạy Run-ComposeBootstrapTests.ps1 trên project GUID: khởi tạo ba service với volume trống, restart và reset/tạo lại volumes test; kiểm tra private bucket, app I/O và quyền bị chặn. CI không đọc .env, không dùng MinIO local, không sinh credentials .env hoặc ghi development user-secrets. minio-init chỉ là công cụ tùy chọn của profile storage-setup.

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

Script test local tạo PostgreSQL và MinIO riêng bằng Docker, credential/port ngẫu nhiên, không mount volume và dọn cả hai trong finally; nó không dùng database/bucket ứng dụng. Nếu thiếu image `helpdesk-minio:latest`, script build từ `deploy/minio`. Trên CI, job chạy `dotnet test` trực tiếp với các container riêng thay cho script này.

```powershell
cd fe
npm ci
npm run lint
npm test
npm run build
```

Kiểm tra cú pháp workflow nếu đã có [actionlint](https://github.com/rhysd/actionlint):

```powershell
actionlint .github/workflows/ci.yml
```

Tham khảo: [PostgreSQL service container](https://docs.github.com/en/actions/tutorials/use-containerized-services/create-postgresql-service-containers), [setup-dotnet và lựa chọn SDK](https://github.com/actions/setup-dotnet), [setup-node và cache npm](https://github.com/actions/setup-node).
