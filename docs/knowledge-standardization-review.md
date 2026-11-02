# Review migration KnowledgeStandardization

Migration mới: `20261031160913_KnowledgeStandardization`, sau `20261031095438_PublicCommunity`. Model vẫn 24 entity, source có 10 migration. **Chưa áp dụng lên database Helpdesk đang dùng trong lần triển khai bước 1.**

Đối soát read-only khi bắt đầu bước 2 ngày 31/10/2026 xác nhận database local đã có migration này sau khi người dùng áp dụng; có 8 snapshot Demo, 1 draft Unclassified và chưa có hướng dẫn Operational được xuất bản. Các regression test chuẩn hóa được chạy lại cùng [truy xuất tài liệu bước 2](knowledge-retrieval.md). Source bước 2 thêm migration thứ 11 riêng, không sửa migration bước 1.

- [SQL forward idempotent](../be/Infrastructure/Persistence/Migrations/Sql/KnowledgeStandardization.sql).
- [SQL rollback](../be/Infrastructure/Persistence/Migrations/Sql/KnowledgeStandardization.Rollback.sql).
- [Nghiệp vụ và contract](knowledge-authoring.md).

## Thay đổi

Chỉ thêm vào `helpdesk.community_entries`:

| Cột | Kiểu | Dữ liệu cũ |
| --- | --- | --- |
| `classification` | varchar(20), not null | Unclassified. |
| `public_classification` | varchar(20), not null | Unclassified. |
| `guide` | jsonb, nullable | Metadata draft; null nếu chưa có. |
| `public_guide` | jsonb, nullable | Snapshot metadata; null nếu chưa có. |

Thêm check giá trị classification, kiểu object JSON, giới hạn guide cho Knowledge và yêu cầu guide có mặt khi public Knowledge đã phân loại. Validation từng trường/bước được thực hiện ở application khi save/publish.

Backfill nhận đúng 6 GUID article và 2 GUID incident từ SHA256 namespace `community-demo-v1`, gắn Demo cho draft/snapshot. Bài mẫu được bổ sung metadata nêu rõ phạm vi minh họa; không tự bịa ngày cập nhật nguồn. Không thay title, content, public content, revision, publishedAt, actor, participation hay lịch sử. Nội dung khác vẫn Unclassified, không tự trở thành Operational.

Không thêm/sửa bảng Ticket, User, role/grant, file, SLA hoặc notification. Không thêm index vì bước này dùng bộ lọc dataset trên truy vấn public hiện có; cần đo lại query/index khi corpus tăng trong bước retrieval.

## Trước khi áp dụng

1. Dừng API cũ (Stop Debugging hoặc `docker compose stop api`) trong thời gian nâng cấp. Code cũ chưa có bộ lọc dataset; không để nó phục vụ trong lúc chuyển trạng thái dữ liệu.
2. Backup PostgreSQL và lưu bản code/image tương ứng; xem [hướng dẫn backup](installation-and-running.md#9-dừng-dịch-vụ-backup-và-dựng-lại). Rà soát SQL và database đích.
3. Chuẩn bị frontend/backend mới cùng contract. Sau nâng cấp, bài cũ chưa phân loại sẽ ẩn khỏi public; giữ nội dung trong quản trị để biên tập lại. Không áp dụng rồi bật bản frontend/backend cũ phục vụ người dùng.

## Áp dụng sau khi review

Terminal root, Development để đọc user-secrets và connection đúng tới PostgreSQL:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef migrations list --project be --startup-project be
dotnet ef database update KnowledgeStandardization --project be --startup-project be
```

Hoặc chạy SQL forward đã review bằng công cụ quản trị phù hợp. Không chạy `database update` theo thói quen vào database chưa kiểm tra; API startup không tự migrate.

Restart backend mới, build/restart frontend, kiểm tra `/knowledge` và `/incidents` mặc định chỉ có nội dung Operational; `?dataset=demo` mới hiện các mẫu. Trong quản trị, kiểm tra nhóm Unclassified rồi chuẩn hóa và duyệt lại. Tùy chọn chuẩn bị 8 bản nháp bằng `seed-knowledge-drafts` theo [hướng dẫn biên tập](knowledge-authoring.md).

## Rollback

Rollback **thu hồi publication của Demo/Unclassified trước khi bỏ cột**, vì API cũ không biết dataset; giữ snapshot Operational đang published. Nó bỏ metadata/phân loại mới và không thể phục hồi thông tin đó nếu thiếu backup. Không tự xuất bản lại những bài đã bị thu hồi khi quay về code cũ.

Chỉ thực hiện sau khi đánh giá dữ liệu mới, dừng API và backup; dùng SQL rollback đã review hoặc `dotnet ef database update PublicCommunity --project be --startup-project be` với configuration đúng. Đây là thao tác thay đổi dữ liệu/schema, không nằm trong quy trình chạy hằng ngày.

## Kiểm chứng

Migration/model/SQL và các luồng được kiểm tra trên PostgreSQL test riêng. Có test nâng từ PublicCommunity với cả GUID mẫu và bài cũ bất kỳ, xác nhận giữ content/revision, không tự gắn Operational. Có test rollback để tránh code cũ làm lộ Demo. Kết quả kiểm thử cập nhật tại [hướng dẫn biên tập](knowledge-authoring.md); không dùng kết quả test làm bằng chứng đã nâng cấp database thật.
