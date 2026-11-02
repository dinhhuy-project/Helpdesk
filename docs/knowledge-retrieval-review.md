# Review migration KnowledgeRetrieval

Migration `20261031163221_KnowledgeRetrieval`, sau `20261031160913_KnowledgeStandardization`. Source có **11 migration**, vẫn 24 entity. Chưa áp dụng migration bước 2 vào database nghiệp vụ trong lần triển khai này.

- [Forward SQL idempotent](../be/Infrastructure/Persistence/Migrations/Sql/KnowledgeRetrieval.sql).
- [Rollback SQL](../be/Infrastructure/Persistence/Migrations/Sql/KnowledgeRetrieval.Rollback.sql).
- [Contract, thuật toán, nghiệm thu và giới hạn](knowledge-retrieval.md).

## Thay đổi

1. Function `helpdesk.knowledge_search_normalize(text)` dùng Unicode NFD, bỏ dấu/đ/Đ, chữ thường ASCII, bỏ ký tự đặc biệt. PostgreSQL phải dùng UTF8, phù hợp Compose PostgreSQL 16 hiện tại; không cần extension unaccent.
2. Function `helpdesk.knowledge_search_vector(text,text,text,text,jsonb)` tạo weighted tsvector với cấu hình simple và metadata hướng dẫn. Hai function IMMUTABLE, PARALLEL SAFE, cố định search_path=pg_catalog.
3. Partial expression index GIN `helpdesk.ix_community_knowledge_retrieval` chỉ gồm Knowledge + is_published + Operational + public_guide. Backfill trực tiếp bài đã duyệt hiện có; không tự publish hoặc đổi classification/content/source/ngày/revision.
4. Không thêm bảng/cột, permission, seed, job hoặc vector database. Function/index do migration SQL quản lý; không có entity tìm kiếm mới. Không sửa migration bước 1 đã áp dụng.
5. CREATE INDEX thông thường nằm trong transaction migration, có thể chặn ghi. Với corpus lớn cần cửa sổ bảo trì hoặc phương án concurrent được review riêng; không tuyên bố zero-downtime.

## Áp dụng sau review

Backup database theo quy trình hiện có, dừng API trong lúc nâng cấp, kiểm tra connection/secrets trỏ đúng database. Không reset migration hoặc xóa volume.

```powershell
# Từ root, tạo SQL để review; không sửa database.
dotnet ef migrations script KnowledgeStandardization KnowledgeRetrieval --idempotent --project be --startup-project be --output .local/migrations/KnowledgeRetrieval.sql

# Người vận hành áp dụng sau khi review/backup.
dotnet ef database update KnowledgeRetrieval --project be --startup-project be
```

Hoặc áp dụng file SQL forward đã review qua công cụ PostgreSQL. API/F5/Compose không tự migrate. Restart API sau khi áp dụng, kiểm tra với câu hỏi khớp một bài Operational đã xuất bản. Corpus rỗng trả `items=[]` hợp lệ.

Kiểm tra read-only:

```sql
SELECT migration_id FROM helpdesk.__ef_migrations_history ORDER BY migration_id DESC LIMIT 2;
SELECT indexname FROM pg_indexes WHERE schemaname='helpdesk' AND indexname='ix_community_knowledge_retrieval';
SELECT helpdesk.knowledge_search_normalize('ĐỔI MẬT KHẨU'); -- doi mat khau
SELECT count(*) FROM helpdesk.community_entries
WHERE kind='Knowledge' AND is_published AND public_classification='Operational' AND public_guide IS NOT NULL;
```

## Rollback

Dừng binary có endpoint retrieval, quay về phiên bản trước bước 2; review/chạy rollback hoặc EF update `KnowledgeStandardization`. Down chỉ gỡ index, hai function theo thứ tự phụ thuộc và migration history. Không sửa/xóa bài hoặc đổi public visibility. Endpoint của binary mới không hoạt động sau khi function bị rollback, cần phối hợp phiên bản ứng dụng.

Forward/rollback/reapply, model và file SQL trùng EF-generated script được kiểm tra trên PostgreSQL test riêng. Không tự áp dụng vào database Helpdesk đang dùng.
