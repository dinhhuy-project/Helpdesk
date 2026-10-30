# Frontend bước 9 — Phân công và workflow

Đã thêm panel **Phân công và xử lý** tại `/tickets/:id`. Giao diện dùng capability và lookup do backend lọc theo Ticket, gửi các lệnh assign/transition hiện có cùng `version` server. Không có migration, permission, package hoặc biến môi trường mới.

## Contract đọc bổ sung

| API | Dữ liệu và phạm vi |
| --- | --- |
| `GET /api/v1/tickets/{id}/workflow` | `version`, `status`, `canAssign`, `assignmentIsValid`, `transitions` (enum số), ID/tên nhóm và người xử lý hiện tại. Cần phiên còn hiệu lực và read scope của Ticket. |
| `GET /api/v1/tickets/{id}/assignment-teams` | `PagedResult<{id,name,departmentName}>`, cần `ticket.assign` và scope nguồn. Chỉ team/department active trong phạm vi đích được phân công. |
| `GET /api/v1/tickets/{id}/assignment-teams/{teamId}/agents` | `PagedResult<{id,displayName}>`, kiểm tra Ticket nguồn trước team đích. Chỉ user active, membership active trong team, có `ticket.update.status` và `ticket.read.team` hoặc `ticket.read.all`. |

Hai lookup hỗ trợ `page` (1–1.000.000), `pageSize` (1–100, mặc định 20), `search` (tối đa 200). Team tìm theo tên nhóm/phòng ban; agent tìm theo tên hiển thị. Scope và eligibility được áp dụng trước count/sort/page; tên rồi ID là thứ tự ổn định. Không trả email, grant hoặc dữ liệu quản trị user. Team đích ngoài phạm vi/inactive trả 404; nguồn không đọc được trả 404, nguồn đọc được nhưng thiếu scope phân công trả 403. Policy vẫn có thể trả 403 trước nếu không có `ticket.assign`.

Các API mới là projection đọc riêng cho workflow, không sử dụng API quản trị team/user và không đòi `team.manage`/`user.manage`. Tên assignment lịch sử vẫn đọc được nếu Ticket còn trong read scope. Không đưa team/user ngoài lookup vào form bằng UUID tự nhập.

`TicketScope.AssignmentTeams` và `AssignmentAgents` được dùng chung với validation ghi. `TicketWorkflow.CanTransition`, `TicketTransitionOperation.For` và các operation scope được dùng cho cả context và command. Context chỉ cho thao tác xử lý khi assignment hiện tại có cả team và agent hợp lệ. Các bước requester/global (confirm/reopen/cancel) không đòi assignment còn active. Read dùng một transaction snapshot; command tiếp tục kiểm tra lại grant/session/membership/version trong transaction ghi hiện có.

## Thao tác giao diện

- Chọn team qua dialog có tìm kiếm/phân trang. Chọn agent đủ điều kiện hoặc chọn rõ **Chỉ giao nhóm, bỏ người xử lý**. Agent kèm mã tham chiếu rút gọn để phân biệt người trùng tên, tooltip có ID đầy đủ. Đổi team xóa lựa chọn agent để người dùng quyết định lại. Lưu có xác nhận, chỉ gửi `{teamId,agentId,version}`; không tự chuyển trạng thái.
- Danh sách thao tác lấy từ `transitions` của server, không suy từ tên role, ID assignee hoặc ba cờ access cũ. Resolve cần summary không trắng, tối đa 4.000 ký tự và có cảnh báo nội dung công khai. Lệnh chỉ gửi `{status,resolutionSummary,version}`; summary là null cho thao tác khác dù bản nhập đang được giữ.
- Confirm đóng Ticket; reopen xóa kết quả hiện tại nhưng giữ activity; cancel kết thúc Ticket. Dialog giải thích tác động trước khi xác nhận. Closed/Cancelled không có thao tác tiếp.
- Không thêm form sửa title/priority, nút tự nhận việc, endpoint hoặc quy tắc chuyển trạng thái mới. Backend [workflow](backend-ticket-workflow.md) vẫn là nơi quyết định cuối cùng.

## Version, lỗi và cache

Draft team/agent/status/summary nằm ở boundary Ticket, giữ qua revalidation access/detail/context. Background read không tự thay version của draft. Khi version detail/context/draft không khớp, khóa gửi và yêu cầu tải lại.

`409 VersionConflict` hoặc `InvalidTransition` giữ toàn bộ draft, không replay. **Tải lại để xem xét** đọc access → context → detail; version hai projection phải khớp. Người dùng tiếp tục bằng **Dùng phiên bản mới để tiếp tục**, sau đó chủ động xác nhận lại thao tác. Nếu bước đã chọn không còn được phép, vẫn hiển thị bản nhập nhưng khóa gửi; chọn lại bước hợp lệ. Phân công cần chọn lại từ lookup mới sau review, draft tên cũ vẫn hiện cho đến khi chọn lại. Server tiếp tục kiểm tra đích tại thời điểm ghi.

Receipt 200 chỉ xác nhận command đã commit, không được dùng như Ticket detail. Sau commit đánh dấu query stale, hủy read cũ, ẩn/xóa detail/comment/activity/lookup, rồi kiểm tra lại quyền trước khi đọc dữ liệu. Nếu read sau phân công trả 404, vẫn báo **Đã phân công yêu cầu**, hiển thị trang không truy cập và không giữ nội dung Ticket cũ trong cache. Lỗi read sau commit không biến thành lỗi command hoặc retry POST. Nội dung nội bộ chỉ tải lại sau discussion capability hiện tại.

400 giữ bản nhập, hiển thị validation/traceId; `InvalidAssignment` yêu cầu chọn đích lại. 403/404 ẩn tài nguyên và bỏ cache riêng của Ticket, 403 kiểm tra lại profile. 429 khóa theo Retry-After. Network/timeout/5xx hoặc receipt không xác định khóa gửi trong workspace kể cả sau read/review: thao tác có thể đã commit, cần đối chiếu trạng thái/activity trước khi chủ động mở lại workspace. Không tự retry mutation. Auth client tiếp tục xử lý 401 bằng refresh single-flight hiện có; response về muộn sau logout không cập nhật private cache hoặc phát success.

## Chạy và nghiệm thu

Restart backend Visual Studio **Container (Dockerfile)** để nạp ba endpoint đọc mới. Chạy `npm run dev` trong `fe/`, mở Ticket detail. Giữ migrations và grants hiện có; không cần seed lại hoặc sửa `.env`. Người xử lý cần membership active và grant xử lý/đọc, người phân công cần scope thích hợp. `ticket.confirm`, `ticket.reopen`, `ticket.cancel` dùng quyền đã được cấp từ bước backend 12.

```powershell
cd fe
npm run lint
npm test
npm run build
cd ..
./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~TicketWorkflowRead|FullyQualifiedName~TicketAssignmentScope|FullyQualifiedName~TicketMutation|FullyQualifiedName~TicketWorkflowTests|FullyQualifiedName~FrontendWorkflowJourney'
dotnet build be/be.sln --no-restore -c Release -warnaserror
```

`TicketWorkflowReadTests` dùng PostgreSQL tạm kiểm tra nguồn trước đích, lookup không cần quyền quản trị, pagination/search, agent eligibility, grant/membership bị thay đổi, requester actions và lịch sử tổ chức inactive. `FrontendWorkflowJourneyTests` chạy chính TypeScript API/auth qua HTTP thật tới application/PostgreSQL tạm: requester tạo → giao nhóm → chọn agent → xử lý/chờ/tiếp tục → resolve → reopen → resolve → confirm; Ticket khác cancel. Có stale version, permission âm và activity thật. CI backend hiện có tự chạy các test mới.

Browser mode tùy chọn dùng `HELPDESK_PLAYWRIGHT_MODULE`, `HELPDESK_FRONTEND_URL=http://localhost:5173`, `HELPDESK_BROWSER_CHANNEL`, `HELPDESK_BROWSER_ARTIFACTS` như [bước 7](frontend-accounts.md). Chạy Vite rồi filter `FrontendWorkflowJourney`. Browser forward API tới backend test, dùng các context requester/coordinator/leader/agent và hai phiên agent để thử 409; credential fixture truyền stdin. Không thử trên database Helpdesk đang dùng. CI mặc định chạy nhánh client/API, không cần cài browser hoặc thêm secret.

Nghiệm thu local ngày 29/10/2026: ESLint, TypeScript/Vite build và toàn bộ frontend **333 tests/26 files** đạt, gồm 22 bài workflow mới. Suite backend được chọn **161 tests** đạt, không skip, bao gồm hồi quy phòng ban/team/category bước 8, assignment/mutations/Domain workflow, lookup/context mới và client TypeScript thật; CLI seed/grant smoke checks đạt trên container tạm. Browser mode Edge đạt với coordinator/leader/requester và hai phiên agent, có 409 giữ draft và buộc chọn lại thao tác, resolve/reopen/confirm/cancel, viewport 390px không tràn ngang và không có lỗi browser ngoài HTTP 409 được thử có chủ đích. Không chạy toàn bộ suite backend hoặc thay dữ liệu Helpdesk cá nhân trong lần này.

[Bước 10 — file đính kèm](frontend-attachments.md) đã bổ sung upload/download qua ACL Ticket/comment. Workflow và comment bị khóa trong lúc upload để tránh thay đổi context của file đang gửi.
