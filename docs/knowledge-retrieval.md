# Truy xuất đoạn hướng dẫn — bước 2

Tra cứu trước khi đăng nhập tại `/knowledge`, mục **Tìm đúng đoạn hướng dẫn**. Đây là baseline từ khóa có mở rộng từ vựng (`lexical-v1`), chưa phải chatbot hay mô hình embedding. Không cần API key, GPU, Redis, pgvector hoặc dịch vụ AI bên ngoài.

Sau bước này đã có [API trợ lý public bằng Ollama local](public-assistant.md), dùng các đoạn retrieval làm nguồn; cấu hình optional riêng và không thêm migration bước 3.

## Kiểm tra bước 1 sau migration

Đã kiểm tra read-only database local trong phiên triển khai bước 2: lịch sử có `20261031160913_KnowledgeStandardization`; 8 snapshot demo đã xuất bản, 1 bản nháp chưa phân loại và **0 hướng dẫn Operational đã xuất bản**. Đây là trạng thái tại thời điểm kiểm tra, không phải ràng buộc số lượng. Không tự phân loại hoặc publish thay người duyệt.

Kiểm tra hồi quy bước 1 bao phủ metadata bắt buộc khi publish, bước đánh số liên tiếp, ngày/nguồn hợp lệ, snapshot độc lập với bản soạn, tách Operational/Demo/Unclassified, CLI tạo 8 ứng viên chưa xuất bản, migration/backfill và rollback bảo vệ nội dung. Xem [chuẩn biên tập](knowledge-authoring.md).

**Hoàn thành nền tảng chuẩn hóa không đồng nghĩa đã có corpus thực tế.** Khi chưa có bài Operational đã duyệt, truy xuất trả danh sách rỗng. Có thể chuẩn bị 8 bản nháp bằng CLI của bước 1, hoặc nhập BookStack rồi bổ sung metadata. Người có quyền cần đọc, điều chỉnh theo đơn vị, chọn Phục vụ thực tế, lưu và duyệt xuất bản. Không đổi các bài demo thành tài liệu thật chỉ để làm đầy kết quả.

## Luồng truy xuất

1. Backend kiểm tra câu hỏi 2–200 ký tự, không có ký tự điều khiển/Unicode lỗi; `limit` 1–8, mặc định 5.
2. Chuẩn hóa chữ thường, Unicode NFD, bỏ dấu tiếng Việt, ánh xạ `đ → d`, tách ký tự đặc biệt. Nội dung gốc không bị sửa.
3. Nhận diện cụm dài trước và mở rộng nhóm từ vựng đã review: `mk/password/mật khẩu`, `MFA/2FA/OTP/xác thực hai lớp`, `VPN/mạng riêng ảo`, file/đính kèm, upload/download, Ticket/yêu cầu hỗ trợ, thông báo, bình luận… Từ chưa biết giữ dưới dạng literal; bỏ từ hỏi phổ thông. Gom OTP/MFA để tìm tài liệu liên quan, không khẳng định chúng là cùng một cơ chế bảo mật.
4. PostgreSQL full-text `simple` lọc **Knowledge + is_published + PublicClassification=Operational + public_guide**. Các nhóm ý phải cùng xuất hiện trong tài liệu; cách gọi trong một nhóm là lựa chọn thay thế. SQL bind tham số; không nhận cú pháp tsquery từ người dùng. Title được ưu tiên hơn summary/topic/metadata và nội dung.
5. Lấy tối đa 61 ứng viên; xếp hạng tối đa 60 bài. Tách theo từng bước đánh số và các phần phạm vi/điều kiện/kết quả/escalation. Đoạn dài chia tại khoảng trắng, tối đa 1.000 ký tự/đoạn, giữ nguyên văn bản. Không tạo câu trả lời hoặc bịa bước hướng dẫn.
6. Xếp hạng theo số nhóm ý khớp ngay trong đoạn, ngữ cảnh tiêu đề/tóm tắt, ngày xuất bản và khóa ổn định. Tối đa 2 đoạn/bài; tối đa `limit` đoạn. Điểm xếp hạng không phải xác suất đúng. Nếu hơn 60 ứng viên, trả `candidateLimitReached=true` để người dùng bổ sung từ khóa.

Index GIN riêng cho snapshot Operational được PostgreSQL cập nhật cùng transaction publish/withdraw/reclassification. Không có bản sao corpus, hàng đợi indexing hoặc cache kết quả phía server. Không nạp toàn bộ thư viện vào bộ nhớ mỗi request. Hai SQL function/index do migration quản lý, không thêm entity EF.

[Bước 4: đồng bộ chỉ mục](knowledge-index-synchronization.md) bổ sung CLI đối soát ID/revision/fingerprint, manifest local và dựng lại index có transaction khi cần phục hồi; không đổi contract retrieval.

API chỉ dùng `public_*` và metadata snapshot; không đọc Ticket, comment, attachment, tài khoản hoặc wiki riêng. Draft sửa chưa publish không đổi kết quả. Bài withdraw hoặc đổi snapshot sang Demo không còn trong truy xuất ở request sau commit. Snapshot legacy không đủ cấu trúc bị bỏ qua tại application.

## Contract

```http
GET /api/v1/public/knowledge/retrieve?query=doi%20mk&limit=5
```

Anonymous, `Cache-Control: no-store`, rate limit `CommunityPublic` hiện có. Không có lựa chọn dataset Demo cho endpoint này. Không ghi dữ liệu/audit/notification hoặc gửi câu hỏi đến Wikipedia/BookStack/AI.

```json
{
  "query": "doi mk",
  "items": [{
    "entryId": "72000000-0000-0000-0000-000000000001",
    "revision": 3,
    "title": "Đổi mật khẩu và đăng nhập lại an toàn",
    "topic": "Tài khoản",
    "section": "Các bước thực hiện",
    "step": 2,
    "text": "2. Chọn đổi mật khẩu, nhập mật khẩu hiện tại và mật khẩu mới theo yêu cầu của form.",
    "sourceUrl": "/knowledge/72000000-0000-0000-0000-000000000001",
    "applicability": "Người đã đăng nhập với tài khoản đang active.",
    "sourceName": "Hướng dẫn đã được đơn vị duyệt",
    "referenceUrl": null,
    "sourceUpdatedOn": null,
    "publishedAt": "2026-10-31T00:00:00Z"
  }],
  "candidateLimitReached": false,
  "method": "lexical-v1"
}
```

Ví dụ contract, không phải bài tự chèn vào database. `step=null` cho phần metadata; `sourceUpdatedOn=null` khi chưa biết ngày tại nguồn. `publishedAt` là thời điểm xuất bản snapshot, không thay cho ngày cập nhật nguồn. Không khớp trả `200` với `items=[]`; input sai trả `400` ProblemDetails/traceId; vượt hạn mức trả `429`.

Frontend lưu câu hỏi trên `?question=`, submit chủ động, có loading/error/retry/empty; hiển thị đoạn nguyên văn, phạm vi, liên kết bài nguồn, phiên bản và ngày. Public client không Bearer. Không giữ đoạn cũ khi đổi query. Query key dùng prefix `public-community`, reset khi CommunityChanged/reconnect; polling 30 giây dự phòng, không giữ cache sau unmount. Thu hồi bài không xóa văn bản đã được người dùng xem/chụp; giao diện đối soát lại bằng event/polling.

## Nghiệm thu và giới hạn

- Corpus: 8 hướng dẫn ứng viên được duyệt **chỉ trong database test**, cộng 3 hướng dẫn VPN/MFA/máy in ghi rõ fixture. Không publish corpus này vào database thực tế.
- [retrieval-evaluation.json](../be/tests/Helpdesk.Persistence.Tests/Knowledge/retrieval-evaluation.json): 28 positive + 4 negative; có dấu/không dấu, acronym và cách diễn đạt khác. Unicode tổ hợp có thêm test chuẩn hóa C#/PostgreSQL.
- Kết quả đã chạy: **Top1 28/28, Recall@5 28/28, negative 4/4**, đủ bước mong đợi. Gate: Top1 ≥85%, Recall@5=100%, negative rỗng. Artifact chi tiết: `be/tests/Helpdesk.Persistence.Tests/bin/Release/net8.0/Knowledge/retrieval-evaluation-result.json` (build artifact, không commit).
- `EXPLAIN (ANALYZE, BUFFERS)` trên 1.200 bài test dùng `ix_community_knowledge_retrieval`, không ép `enable_seqscan`. Lần đo focused: execution 0,871 ms, planning 2,371 ms với từ khóa chọn lọc. Artifact `.../Knowledge/retrieval-query-plan.txt`. Đây không phải benchmark tải production; thời gian thay đổi theo máy/lần chạy.
- Negative tests: draft/demo/unclassified, Ticket riêng/internal note không đi vào kết quả; sửa draft không đổi snapshot; reclassification/withdraw mất kết quả; no-store/input lỗi; rollback/reapply giữ nội dung; giới hạn ứng viên/đoạn.
- Frontend: explicit submit/direct link, cancellation/public transport, XSS dạng text, 429, empty, invalidation xóa đoạn cũ. Browser journey có tra cứu bundle thật + API riêng, viewport 390px và axe.

Đã chạy hồi quy liên quan backend: 78 pass, 0 skip; frontend toàn bộ 517 pass/38 file, lint và production build pass; browser Community 1 pass với API/PostgreSQL thật, kiểm tra retrieval mobile 390px và 0 vi phạm axe trong nhóm rule đã chọn. Sau đó có thêm kiểm tra unit cho từ viết tắt lặp và snapshot metadata hỏng. Đây là nghiệm thu tính năng liên quan, không phải tuyên bố chạy lại toàn bộ test backend hoặc tất cả browser journey. CI hiện có đã chạy `dotnet test` và `npm test`, tự nhận các test mới.

```powershell
# Từ root, Docker đang chạy; container/database test riêng tự được dọn.
powershell -NoProfile -ExecutionPolicy Bypass -File be/tests/Run-PersistenceTests.ps1 -Configuration Release -Filter 'FullyQualifiedName~Knowledge|FullyQualifiedName~CommunityTests|FullyQualifiedName~ModelTests|FullyQualifiedName~PostgresTests'

cd fe
npm run test
npm run lint
npm run build
```

Mở rộng từ vựng hữu hạn, không hiểu mọi cách diễn đạt, không sửa lỗi chính tả hay suy luận đa bước. Bộ hồi quy nhỏ không chứng minh chất lượng với mọi tài liệu của đơn vị. Cần bổ sung câu hỏi thực tế đã loại dữ liệu nhạy cảm và chấm relevance theo đoạn; dùng kết quả làm baseline trước khi so embedding/hybrid retrieval. Wikipedia vẫn là panel tham khảo riêng, không phải quy trình đã duyệt của đơn vị.

## Kích hoạt

Review/apply [migration KnowledgeRetrieval](knowledge-retrieval-review.md), restart backend/frontend, xuất bản ít nhất một bài Operational đã rà soát. Thử `/knowledge?question=doi%20mat%20khau` với bài phù hợp, câu không dấu/từ viết tắt và câu ngoài phạm vi. Không cần env var hoặc package mới.

Tham chiếu thiết kế SQL: [Unicode normalization PostgreSQL 16](https://www.postgresql.org/docs/16/functions-string.html), [full-text parsing/ranking](https://www.postgresql.org/docs/16/textsearch-controls.html), [GIN text-search indexes](https://www.postgresql.org/docs/16/textsearch-indexes.html).
