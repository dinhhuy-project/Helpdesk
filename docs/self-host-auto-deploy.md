# Tự cập nhật bản demo sau khi push

Máy cá nhân đã chạy [bản demo Docker](self-host-demo.md). Auto deploy dùng **agent pull trên chính máy Windows**, không cần VPS, webhook public, GitHub token hoặc self-hosted GitHub Actions runner. Nhánh mặc định của repo hiện tại là **`develop`**.

```text
git push origin develop
       → GitHub Actions CI của đúng commit pass
       → agent local kiểm tra khoảng mỗi 2 phút
       → checkout riêng, build image gắn commit
       → kiểm tra DB/index và CI lần nữa
       → thay API + frontend, kiểm tra HTTP/API
       → thành công: ghi phiên bản; lỗi rollout: thử khôi phục image trước
```

Chỉ push lên nhánh được cấu hình mới đủ điều kiện. Pull request/fork, workflow khác, CI chưa xong/đỏ và commit khác SHA đều không deploy. Khi push liên tiếp, agent ưu tiên đầu nhánh hiện tại; bản build bị thay thế bởi push mới sẽ bị bỏ qua trước rollout. Việc deploy diễn ra **sau khi CI hoàn tất**, không phải ngay khi lệnh git push trả về.

Repository hiện public nên máy có thể đọc kết quả CI qua GitHub API không xác thực. GitHub giới hạn request không xác thực theo IP; agent không gọi API khi không có commit mới hoặc commit đã bị block, tối đa khoảng 30 lần/giờ khi chờ CI với chu kỳ 120 giây, cộng lần kiểm tra trước rollout. Nếu bị rate limit hoặc mất mạng, bản đang chạy được giữ lại và agent kiểm tra ở chu kỳ sau. [Workflow runs API](https://docs.github.com/en/rest/actions/workflow-runs), [rate limits](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api).

## Cài một lần

Tại root repository, cùng Windows user đang chạy Docker Desktop và có quyền đọc secrets demo:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Enable-SelfHostAutoDeploy.ps1
```

Cần Git, Docker Desktop Linux engine, bản demo đã khởi tạo và HEAD local khớp đầu nhánh remote để đặt baseline. Script không pull/reset thư mục IDE. Mặc định theo dõi `develop`, 120 giây; có thể chọn `-Branch` và `-PollSeconds` (tối thiểu 120).

Script tạo:

- `.local/self-host-auto-deploy/config.json`: repo/branch, đường dẫn state, checkout và hash Compose đã chấp nhận.
- `source/`: clone riêng, chỉ checkout commit nhánh đã pass CI. Không stash, reset, clean hoặc checkout tại thư mục bạn đang lập trình.
- `agent/`: bản copy riêng các script điều phối. Push code ứng dụng không tự thay chương trình điều phối đã cài; khi sửa các script điều phối cần chạy installer lại.
- `status.json`, `agent.log`, `last-build.log`: trạng thái/hoạt động/build gần nhất. Không in secrets hoặc đưa chúng lên GitHub.
- Shortcut **Helpdesk Demo Auto Deploy** trong Startup của user Windows, chạy agent ẩn sau khi đăng nhập. Không tạo service hệ thống hoặc yêu cầu mật khẩu Windows. Gỡ bằng `-Action Disable` bên dưới.

Root `.local/self-host-demo/.env` và volumes hiện có tiếp tục dùng nguyên. Bản build lấy source từ clone riêng nhưng đọc cùng state directory qua `HELPDESK_DEMO_STATE_DIRECTORY`. Image mới có tag `demo-<commit SHA>`, ghi `DEMO_IMAGE_TAG` vào file env sau khi kiểm tra thành công, giữ các giá trị secrets khác. Các lệnh Start demo thủ công tiếp tục dùng tag đã phát hành này.

**Cần commit/push các file triển khai mới cùng code ứng dụng** để checkout trên remote có Compose hỗ trợ tag image và state directory mới. Installer không tự commit hoặc push code thay bạn.

## Sử dụng hằng ngày

1. Bật Docker Desktop, bật demo bằng `Start-SelfHostDemo.ps1 -Public` nếu chưa chạy.
2. Viết code, commit rồi `git push origin develop` như bình thường.
3. Chờ CI xanh và chu kỳ kiểm tra tiếp theo. Refresh trang để nhận bundle mới; phiên SignalR sẽ reconnect khi API thay container.

Agent chỉ cập nhật khi web demo đang chạy. Nếu bạn đã dừng demo, trạng thái `WaitingForDemo` giữ yêu cầu cập nhật lại; agent không tự mở Internet/tunnel từ một push. Shortcut Startup chỉ bật agent, không thay việc khởi động Docker Desktop hoặc tự phát hành URL mới.

Khi cập nhật code, chỉ API/web được recreate; PostgreSQL, MinIO, volumes, thông tin đăng nhập và **container tunnel đang chạy được giữ nguyên**. Vì thế URL Quick Tunnel không đổi bởi chính thao tác cập nhật code. API/web có thể gián đoạn ngắn khi thay container; đây không phải deploy zero downtime. Windows/Docker/network dừng hoặc tunnel chết vẫn có thể làm URL mất hiệu lực.

## Xem trạng thái, tạm dừng, thử lại

```powershell
# Agent có đang chạy? Commit nào đã phát hành? Có đang chờ CI/review?
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-SelfHostAutoDeploy.ps1 -Action Status

# Tạm dừng agent; không dừng demo, không ngắt rollout đang chạy giữa chừng.
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-SelfHostAutoDeploy.ps1 -Action Stop

# Bật lại agent.
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-SelfHostAutoDeploy.ps1

# Sau khi đã dừng và sửa nguyên nhân, cho phép thử lại commit đang bị block.
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-SelfHostAutoDeploy.ps1 -Action Retry

# Dừng agent và gỡ shortcut tự chạy; demo/data vẫn giữ nguyên.
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start-SelfHostAutoDeploy.ps1 -Action Disable
```

`Stop` chờ tối đa 15 giây để phản hồi; nếu đang build/rollout thì agent hoàn tất lần đó rồi mới thoát. Không dùng Task Manager kill agent giữa rollout. Không chạy initializer, sửa secrets hoặc deploy thủ công cùng lúc agent đang phát hành: dừng agent và chờ `Agent running: False` trước.

Một file lock ngăn hai agent chạy đồng thời. `process.json` được đối chiếu với command line process, không tự kill một PID có thể đã được Windows tái sử dụng.

## Migration, cấu hình hạ tầng và rollback

| Trạng thái | Hành vi và cách xử lý |
| --- | --- |
| `UpToDate` | Commit nhánh đã trùng bản deploy, không build lại. |
| `WaitingForCi` | Chưa có CI push thành công của đúng SHA/branch/repo; bản đang chạy giữ nguyên. Xem tab Actions và sửa CI trước. |
| `WaitingForDemo` | Demo đang dừng hoặc Docker chưa sẵn sàng; bật demo để tiếp tục. |
| `Deployed` | Image API/web đã qua health/API probe và ghi phiên bản thành công. |
| `Blocked` | Lỗi build/preflight, migration mới hoặc Compose đổi. Xem `agent.log`/`last-build.log`; commit đó không bị build lại vô hạn. Sửa nguyên nhân rồi Stop/Retry, hoặc push bản sửa mới. |
| `RollbackFailed` | Thử khôi phục image trước cũng không qua probe. Agent chặn mọi release tiếp theo; kiểm tra API/web, cấu hình và Docker, phục hồi thủ công trước khi Retry. |

**Không tự migrate database.** Nếu có migration mới, agent phát hiện bằng history trước build và kiểm tra index bằng binary mới trước rollout. Không seed, reset admin hoặc đổi grants khi auto deploy. Thực hiện theo quy trình SQL review/backup đã có:

1. Dừng agent, chờ thoát; dừng demo/tunnel trước bảo trì schema.
2. Chuẩn bị/review SQL, backup/apply bằng initializer của bản demo, khởi động lại và kiểm thử.
3. Chạy `Enable-SelfHostAutoDeploy.ps1` lại để cập nhật chương trình điều phối/hash Compose khi cần, hoặc Stop/Retry nếu chỉ gỡ block.

Mọi thay đổi `docker-compose.demo.yml` bị chặn để review trước, kể cả cấu hình hạ tầng không đổi nhưng sửa comment. Installer chạy lại xác nhận hash Compose hiện tại và bỏ block của lần trước; không tự biến một branch force-push thành lịch sử đã tin cậy. Nếu viết lại lịch sử nhánh, cần đối soát baseline với bản đang chạy trước khi tiếp tục.

Build/preflight lỗi xảy ra trước khi thay container thì bản cũ giữ nguyên. Rollout/probe lỗi thì agent thử chạy tag trước và kiểm tra lại; database schema không thay đổi nên không có tự rollback migration. Rollback vẫn có thể thất bại nếu Docker/disk/network lỗi, cần operator xử lý. Image cũ được giữ để phục hồi, không tự prune toàn bộ Docker; theo dõi dung lượng và chỉ dọn image không còn cần sau khi xác nhận phiên bản ổn định.

## Kiểm chứng

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File be/tests/Run-SelfHostAutoDeployTests.ps1
```

17 trường hợp kiểm tra: CI sai event/repo/branch/SHA/path/status, migration thiếu, build/preflight lỗi không chạm live container, rollout lỗi khôi phục tag trước, môi trường/secrets được giữ, Compose đổi bị chặn, commit mới chen vào hoặc CI đổi trong lúc build thì bỏ candidate. Git và Docker trong các tình huống rollout là fixture riêng, không phải thao tác sửa database thật. CI đã bổ sung chạy bộ này.

Đã cài agent, xác nhận background process/Startup shortcut, clone đúng repo/branch và thử graceful stop trên máy hiện tại. Chưa tạo một push từ tài khoản của bạn để nghiệm thu tự động end-to-end; cần commit/push các file mới và CI xanh để thực hiện lần phát hành thực tế đầu tiên. Việc có agent không có nghĩa CI gần nhất đã đạt.

Kết quả local ngày **03/11/2026**: 17/17 tình huống auto deploy pass; ESLint, production build và YAML parse đạt. Lần chạy browser đầu tái hiện 2 lỗi: link trong đoạn mô tả login thiếu underline (axe `link-in-text-block`) và dispatcher của test realtime chạy trước commit mark-read. Đã thêm underline và chờ commit bằng API trước khi dispatch, giữ nguyên kiểm tra đồng bộ giữa hai trình duyệt. Sau sửa, chạy lại toàn bộ **10/10 browser journeys pass, 0 skip**, gồm kiểm tra accessibility. Đây là kết quả local trên PostgreSQL/MinIO test riêng; workflow GitHub chỉ chạy lại sau khi bạn push, không tự đánh dấu một run GitHub cũ thành xanh.

Không mở repo public cho người không tin cậy quyền push/merge nhánh deploy. Code từ nhánh này sau CI sẽ được build/chạy trên máy có Docker và dữ liệu demo; CI không thay thế code review. Không dùng token admin hoặc self-hosted runner để cho workflow pull request chạy trên máy cá nhân. [GitHub hướng dẫn bảo mật runner/workflow](https://docs.github.com/en/actions/reference/security/secure-use).
