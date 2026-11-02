# Đồng bộ chỉ mục tài liệu — bước 4

Giữ retrieval `lexical-v1` và Ollama local miễn phí của bước 3. Chỉ mục hiện tại là expression GIN `helpdesk.ix_community_knowledge_retrieval` trên snapshot trong PostgreSQL. PostgreSQL cập nhật index **cùng transaction** với dữ liệu; không có độ trễ chờ một worker indexing riêng.

## ID, revision và thu hồi

| Thao tác | Kết quả truy xuất sau commit |
| --- | --- |
| Tạo/sửa bản nháp | Chưa đưa vào index phục vụ public; snapshot đã xuất bản không đổi. |
| Publish lần đầu | `EntryId` cố định, `PublicRevision=1`; index chỉ nhận Knowledge Operational có public guide. |
| Publish bản sửa | Giữ ID, tăng revision; index dùng `public_*` mới. Không tạo bản sao bài cũ để tiếp tục trả lời. |
| Withdraw | Loại khỏi tập truy xuất; giữ snapshot, revision và lịch sử nghiệp vụ. |
| Publish sang Demo | Loại khỏi tập Operational; sửa classification của draft chưa đủ để thay snapshot. |
| Publish lại sau withdraw | Cùng ID, revision tăng tiếp; không khôi phục một revision thấp hơn. |
| Lỗi/rollback transaction | Cả snapshot và cập nhật GIN rollback; request khác vẫn thấy bản đã commit trước đó. |

Trợ lý kiểm tra lại ID/revision và điều kiện public sau inference; nguồn đổi trong lúc chờ AI trả `SourcesChanged`, không phát lại đoạn cũ. Không giữ transaction database trong khi chờ Ollama. Tại phía giao diện, `CommunityChanged` và polling/reset cache đối soát lại nguồn; không thể thu hồi văn bản người dùng đã xem hoặc lưu ngoài hệ thống.

## Công cụ đối soát và manifest

CLI riêng cho người vận hành có quyền kết nối database; không mở endpoint public để chạy DDL. Các lệnh không start HTTP server/hosted workers, không seed, migrate, import Wiki hoặc publish tài liệu.

Chạy từ **root repository**, dùng connection string Development đã cấu hình theo [hướng dẫn cài đặt](installation-and-running.md#33-cấu-hình-backend-development):

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:Database__HostOverride = 'localhost'
$manifestPath = Join-Path (Get-Location) '.local/knowledge-index/manifest.json'

dotnet run --project be --configuration Release --no-launch-profile -- knowledge-index check --manifest "$manifestPath"
```

Truyền đường dẫn tuyệt đối như trên vì `dotnet run --project be` đặt working directory của backend tại `be/`; đường dẫn tương đối `.local/...` sẽ nằm dưới `be/.local/`.

`check` chỉ đọc PostgreSQL, ghi một file checkpoint local sau khi kiểm tra thành công:

- Đảm bảo không còn migration pending; kiểm tra index tồn tại, là GIN trên đúng bảng, expression/predicate đúng bản đã review, các cờ `valid/ready/live` hợp lệ.
- Đọc nhất quán bằng Repeatable Read: tập ứng viên index gồm `EntryId`, `PublicRevision`, SHA-256 của title/summary/content/topic/guide công khai. Hash dùng đối soát thay đổi, không thay cho chữ ký xác thực.
- So với manifest lần trước: báo số bài mới (`added`), sửa (`updated`), rời tập truy xuất (`removed`). `removed` có thể do withdraw, chuyển Demo hoặc mất điều kiện nguồn; không xóa bài trong database.
- Phát hiện nội dung thay đổi mà revision không tăng (`unversioned`) hoặc revision giảm (`regressions`); báo lỗi và giữ baseline để điều tra. Các bài đã rời manifest vẫn giữ revision trong bảng nguồn.
- Manifest có phiên bản format/pipeline, tên DB và thời điểm quan sát UTC. Không lưu nội dung bài, câu hỏi, thông tin user/Ticket hoặc credentials. Tối đa 100.000 bài; vượt hạn mức sẽ thất bại thay vì lưu checkpoint bị cắt.

Mỗi môi trường cần **đường dẫn manifest riêng**, kể cả khi các database trùng tên. Không đặt trong thư mục frontend public hoặc commit file local. Manifest là ảnh chụp phục vụ vận hành, không phải hàng đợi công việc và không tham gia quyết định public ACL. Mất manifest không làm mất index: chạy check để lập baseline mới, khi đó không còn so được chênh lệch với lần đã mất.

Điều kiện index là tập ứng viên SQL; retrieval vẫn kiểm tra cấu trúc guide/bước và bỏ qua snapshot legacy hỏng. `healthy=true` là kiểm tra cấu trúc/cờ catalog, **không chứng minh mọi posting GIN hay nội dung hướng dẫn đều đúng**. Các hàm normalize/vector do migration quản lý; không sửa hàm SQL hoặc revision bằng tay. Đổi hàm/pipeline phải review migration, dựng lại index và chạy lại bộ đánh giá retrieval.

## Dựng lại và chạy lại sau lỗi

Index được đồng bộ tự động khi publish/withdraw; không cần rebuild định kỳ hoặc chạy lệnh sau mỗi bài. Khi thực sự cần dựng lại index hiện có:

```powershell
dotnet run --project be --configuration Release --no-launch-profile -- knowledge-index rebuild --manifest "$manifestPath"
```

Lệnh lấy advisory transaction lock quản trị dùng chung `734205001`, kiểm tra định nghĩa rồi chạy **`REINDEX INDEX helpdesk.ix_community_knowledge_retrieval`** trong transaction. Không drop/create lại schema, không sửa snapshot hoặc revision. Kiểm tra lại catalog sau REINDEX và chỉ lưu manifest sau commit. Trong khoảng giữ khóa, các tác vụ dùng cùng khóa như quản trị tài khoản, SLA/notification worker cũng phải chờ; đây là thêm một lý do chỉ rebuild trong khoảng bảo trì.

**Chạy rebuild trong khoảng bảo trì:** REINDEX thường có thể chặn đọc/ghi kho tri thức. Chờ database lock tối đa 5 giây, mỗi SQL statement tối đa 120 giây, client command 130 giây và cả lệnh có deadline 3 phút. Lệnh từ chối khi advisory lock đang bận; không xếp hàng hay tự retry vô hạn. Corpus lớn cần kế hoạch DBA riêng cho `REINDEX CONCURRENTLY`, vốn có cơ chế lỗi và giới hạn transaction khác.

| Tình huống | Xử lý |
| --- | --- |
| Mất kết nối, timeout, lock bận hoặc hủy giữa REINDEX | Transaction rollback khi kết nối đóng; manifest cũ giữ nguyên. Sửa nguyên nhân rồi chạy lại cùng lệnh. |
| REINDEX commit nhưng ghi manifest lỗi/process dừng | Index đã hợp lệ vẫn dùng được. Sửa quyền/dung lượng ổ đĩa rồi chạy lại; REINDEX lặp không sửa tài liệu hay nhân đôi dữ liệu. |
| Hai lệnh cùng manifest | File `.lock` giữ handle độc quyền suốt lần chạy; lệnh thứ hai thất bại, không ghi đè baseline. File lock được giữ trên disk, handle tự giải phóng khi process kết thúc; không xóa file lock của process khác. |
| Manifest hỏng/khác DB hoặc pipeline | Không ghi đè. Giữ bản cũ để điều tra; chọn file riêng cho đúng môi trường. |
| Index thiếu hoặc định nghĩa lệch | Check báo unhealthy, rebuild từ chối. Review/khôi phục schema theo migration; công cụ không tự sáng tác DDL thay thế. |
| Sửa nội dung không tăng revision hoặc revision lùi | Điều tra thao tác SQL ngoài quy trình/restore DB; không dùng rebuild để hợp thức hóa sai lệch. Sau restore đã review, lưu baseline mới ở đường dẫn khác. |

File mới được serialize vào file tạm cùng thư mục, flush xuống disk rồi rename thay thế; checkpoint cũ không bị truncate trước. Dùng filesystem local có hỗ trợ rename thay thế, tránh network share. File tạm còn lại khi máy/process bị tắt đột ngột không được dùng như checkpoint. Exit code `0` thành công; `1` lỗi vận hành/đối soát; `2` cú pháp sai. Không có loop tự động khi exit code khác 0.

## Nghiệm thu

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File be/tests/Run-PersistenceTests.ps1 -Configuration Release -Filter 'FullyQualifiedName~KnowledgeIndex'

# Hồi quy retrieval, public assistant và nguồn; model thật chạy opt-in riêng.
powershell -NoProfile -ExecutionPolicy Bypass -File be/tests/Run-PersistenceTests.ps1 -Configuration Release -Filter 'FullyQualifiedName~Knowledge|FullyQualifiedName~OllamaKnowledgeModelTests|FullyQualifiedName~CommunityTests|FullyQualifiedName~ModelTests|FullyQualifiedName~ApiFoundationTests'
powershell -NoProfile -ExecutionPolicy Bypass -File be/tests/Run-LocalAssistantTests.ps1
```

Tests dùng PostgreSQL/database riêng: draft không đổi manifest; publish cùng ID/tăng revision; withdraw/republish/reclassification; rollback không lưu bài/index lỗi; rebuild lặp giữ nội dung/xmin/audit; database lock timeout giữ checkpoint và lần chạy lại thành công; index thiếu/định nghĩa sai bị từ chối. Tests file bao phủ checkpoint lặp, hủy, lock cạnh tranh, JSON hỏng, revision lùi và nội dung bị sửa không tăng revision. CI hiện có `dotnet test` tự nhận các tests mới; không cần tải model AI để chạy tests đồng bộ chỉ mục.

Kết quả kiểm tra ngày **02/11/2026**: build Release với warn-as-error **0 warning/error**; **146 tests liên quan pass, 0 skip**, gồm 13 tests mới và test Ollama thật + API + PostgreSQL riêng. Model trả Answered có nguồn đúng, thiếu nguồn trả InsufficientInformation và abstain với evidence không liên quan. Lần chạy đồng thời bộ hồi quy trên CPU mất khoảng 85 giây cho câu có nguồn với timeout test 120 giây; không phải cam kết latency. Cấu hình sử dụng mặc định 60 giây có thể trả Unavailable khi máy bận; điều chỉnh `LocalAssistant:TimeoutSeconds` trong giới hạn 5–120 nếu cần và đo lại.

Đã chạy CLI `check` read-only trên database đang dùng: index healthy, 11 migrations, 0 bài Knowledge Operational đủ điều kiện; số dữ liệu vẫn 31 users/149 Tickets/9 community entries. Không rebuild/migrate/seed/publish trên database nghiệp vụ. Những số đếm này là quan sát tại lần kiểm tra, không phải ràng buộc hệ thống. Muốn trợ lý có nguồn thực tế cần người có quyền rà soát và publish bài Operational.

**Không có migration, package hoặc biến môi trường mới cho bước 4.** Không thêm bản sao corpus hoặc worker eventual consistency. Nếu về sau đưa chunk/embedding sang hệ thống khác, lúc đó cần outbox trong transaction publish, upsert theo `(EntryId, PublicRevision, PipelineVersion)`, tombstone khi withdraw, retry có giới hạn và đối soát định kỳ; vẫn phải kiểm tra nguồn live trước khi trả lời. Manifest hiện tại không được coi là bằng chứng đã đồng bộ một index bên ngoài.

Tham khảo: [PostgreSQL 16 REINDEX và khóa/transaction](https://www.postgresql.org/docs/16/sql-reindex.html), [các cờ pg_index](https://www.postgresql.org/docs/16/catalog-pg-index.html).
