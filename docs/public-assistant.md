# API trợ lý public dùng AI local — bước 3

Endpoint `POST /api/v1/public/assistant` dùng **Ollama + qwen3:1.7b chạy local**, không cần API key hoặc dịch vụ AI trả phí. Phần mềm/model miễn phí; máy của bạn cung cấp CPU/RAM và dung lượng lưu model. Lần đầu cần Internet để tải image/weights, suy luận không cần gửi câu hỏi ra cloud. Image Ollama 0.40.13 được ghim digest; model mặc định khoảng 1,4 GB, license Apache 2.0 theo [trang model](https://ollama.com/library/qwen3:1.7b).

## Kiểm tra bước 2

Đã kiểm tra read-only database local sau khi người dùng migrate: có `20261031163221_KnowledgeRetrieval`, index `ix_community_knowledge_retrieval`, function trả `doi mat khau` cho chữ tiếng Việt có dấu và server encoding UTF8. Khi kiểm tra, vẫn có 8 snapshot Demo và 1 draft Unclassified, **chưa có bài Knowledge Operational được xuất bản**.

Nền tảng truy xuất đã sẵn sàng; nguồn thực tế cần người có quyền rà soát/publish theo [chuẩn biên tập](knowledge-authoring.md). Có thể tạo 8 bản nháp ứng viên bằng CLI bước 1, hoặc import BookStack rồi bổ sung metadata. Trợ lý không tự publish hoặc dùng demo thay nguồn thật. Bước 3 **không thêm migration**, permission hoặc seed nghiệp vụ.

## Cài và chạy local

Từ thư mục gốc, Docker Desktop đang chạy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File be/scripts/Initialize-LocalAssistant.ps1
```

Script khởi tạo stack riêng `helpdesk-ai` bằng [docker-compose.ai.yml](../docker-compose.ai.yml), pull model, xác minh model có mặt, rồi cấu hình các khóa LocalAssistant trong `.env` và backend user-secrets. Không in hoặc sửa các secret Identity/JWT/BookStack hiện có. Không migrate, không thay nội dung database, không restart API đang Debug.

Default CPU: 4 CPU, container tối đa 6 GB RAM, context 8192, một model/một inference tại một thời điểm. Chuẩn bị đủ dung lượng cho image Ollama và model trong Docker; Docker Desktop cần đủ RAM ngoài phần dành cho PostgreSQL/MinIO/API. Các lần chạy lại dùng model đã có; xóa volume AI thì cần pull lại. GPU không bắt buộc, cấu hình này chưa yêu cầu NVIDIA passthrough.

**Restart backend sau setup.** Visual Studio `Container (Dockerfile)` dùng `LocalAssistant__BaseUrl=http://host.docker.internal:11434/` trong launch profile; backend CLI trên host dùng user-secrets `http://localhost:11434/`. Compose API lấy `.env`. Container API không dùng localhost để gọi container Ollama.

Các lệnh riêng:

```powershell
docker compose -p helpdesk-ai -f docker-compose.ai.yml up -d --wait ollama
docker compose -p helpdesk-ai -f docker-compose.ai.yml exec -T ollama ollama pull qwen3:1.7b
docker compose -p helpdesk-ai -f docker-compose.ai.yml exec -T ollama ollama list
docker compose -p helpdesk-ai -f docker-compose.ai.yml logs --tail 30 ollama
docker compose -p helpdesk-ai -f docker-compose.ai.yml stop ollama
```

Healthcheck chỉ xác nhận runtime đang hoạt động, không chứng minh model đã tải hay chất lượng trả lời. Dùng setup script để tải model và API để kiểm tra luồng. Named volume `helpdesk-ai_ollama-models` giữ weights; `stop` không xóa volume. Các lệnh hạ tầng `postgres redis minio` trước đó vẫn độc lập với AI.

Các tùy chọn backend (mặc định Enabled=false):

| Khóa | Mặc định / quy tắc |
| --- | --- |
| `LocalAssistant:Enabled` | `false`; setup đổi thành true. |
| `LocalAssistant:BaseUrl` | `http://localhost:11434/`; chỉ loopback, host.docker.internal hoặc hostname ollama, path `/`, không credentials/query/fragment. |
| `LocalAssistant:Model` | `qwen3:1.7b`; tag local, không tag cloud hoặc tên registry tùy ý. |
| `LocalAssistant:TimeoutSeconds` | 60; cho phép 5–120, áp dụng cả headers và đọc response body. |

Compose tương ứng `LOCAL_ASSISTANT_ENABLED`, `LOCAL_ASSISTANT_BASE_URL`, `LOCAL_ASSISTANT_MODEL`, `LOCAL_ASSISTANT_TIMEOUT_SECONDS` trong `.env`. Không có cấu hình VITE cho Ollama. Stack dùng `OLLAMA_NO_CLOUD=1`, port chỉ bind `127.0.0.1:11434`, không bật origins `*`. Mặc định kiểm thử hướng Windows Docker Desktop; trên Linux cần đặt API/Ollama vào network riêng chung và dùng `http://ollama:11434/` thay vì giả định loopback host truy cập được từ container.

## Contract và cách thử

```http
POST /api/v1/public/assistant
Content-Type: application/json

{"question":"doi mat khau"}
```

Anonymous, body tối đa 4 KiB, câu hỏi 2–200 ký tự, không ký tự điều khiển/Unicode lỗi. API không nhận conversation history, nguồn, model, URL hoặc system prompt từ client; các field dư không được dùng. Nên hỏi thao tác cụ thể, không nhập mật khẩu, OTP hoặc thông tin riêng tư vào câu hỏi.

```powershell
Invoke-RestMethod -Method Post -Uri 'http://localhost:5000/api/v1/public/assistant' `
  -ContentType 'application/json' -Body '{"question":"doi mat khau"}'
```

Swagger có endpoint này. Frontend typed client: `publicCommunityApi.askAssistant(question, signal)`; request POST không Bearer, timeout 125 giây, không tự retry. Ở bước này thử API qua Swagger/PowerShell; chưa cung cấp UI hội thoại.

Response luôn có `status`, `answer`, `sources`, `contact`, `method=local-extractive-v1`. Khi Answered, mỗi source có `number` và `passage` theo contract [retrieval](knowledge-retrieval.md): entryId, revision, section/step, text, sourceUrl, applicability, sourceName, referenceUrl, sourceUpdatedOn, publishedAt. Ký hiệu `[1]`, `[2]` trong answer khớp số nguồn do server gán.

| Status | Ý nghĩa |
| --- | --- |
| `Answered` | Có đoạn trích nguyên văn đã kiểm tra nguồn. |
| `InsufficientInformation` | Không tìm được nguồn, câu quá rộng hoặc model cho rằng đoạn chưa đủ để trả lời. |
| `Unavailable` | Tính năng chưa bật, Ollama/model thiếu, timeout hoặc output không hợp lệ. |
| `Busy` | Đang có inference khác trong API process, hoặc Ollama đang quá tải. |
| `SourcesChanged` | Bài vừa đổi public revision, chuyển Demo hoặc bị withdraw trong lúc inference. |

Các trạng thái trên là kết quả nghiệp vụ `200`, với `sources=[]` trừ Answered. `400` input sai và `429` quá 6 request/phút/IP dùng ProblemDetails nền tảng; 429 có Retry-After. API no-store. Không dùng trạng thái HTTP 200 để tự suy ra đã có câu trả lời: phải đọc `status`.

`contact.message` luôn giải thích cách đăng nhập tạo Ticket và liên hệ Helpdesk/quản trị của đơn vị nếu không đăng nhập được; `createTicketUrl=/login?returnTo=%2Ftickets%2Fnew`, `knowledgeUrl=/knowledge`. Không bịa hotline/email hoặc tự tạo Ticket thay người dùng. Nếu chưa có bài Operational, trường hợp InsufficientInformation là đúng.

## Câu trả lời có nguồn và giới hạn

1. Dùng retrieval bước 2, tối đa 3 đoạn từ snapshot Knowledge Operational đã publish. Không đọc Ticket/comment/file/tài khoản hoặc trực tiếp tìm trong wiki riêng/Wikipedia. Không có nguồn thì không gọi model.
2. Ollama nhận câu hỏi và JSON của các đoạn đã lọc, có phạm vi/tiêu đề/nội dung; input là dữ liệu không đáng tin, không được coi là chỉ dẫn. Không có tools, browsing hoặc thực thi lệnh. Model trả JSON `{sufficient, passageIds}`, không trả nội dung hướng dẫn tự sinh.
3. Schema, kiểu dữ liệu, số lượng, phạm vi ID, trùng ID và tính nhất quán sufficient được kiểm tra lại tại server. Body provider giới hạn 32 KiB, tắt redirect/cookie/proxy cho suy luận local; output hỏng hoặc generation bị cắt không được dùng. JSON evidence giữ chữ tiếng Việt thay vì bắt model đọc chuỗi Unicode escape.
4. Sau inference, kiểm tra lại từng source trong DB còn public Operational và đúng public revision. Không giữ transaction/lock database trong thời gian chờ AI. Nếu nguồn đổi, bỏ toàn bộ answer, không tự retry inference.
5. Server ghép phần mở đầu cố định + đoạn trích nguyên văn + số nguồn, lấy metadata/link từ DB. Không phát nội dung model tự viết hoặc URL model bịa. Người dùng vẫn cần đọc đầy đủ quy trình/phạm vi trước khi thao tác; kết quả không được coi là hướng dẫn đầy đủ cho mọi tình huống.

Đây là trợ lý **trích dẫn có kiểm soát**: AI chọn evidence và quyết định abstain; không phải mô hình diễn giải tự do. Cách này hạn chế tạo lệnh/quy trình sai ngoài tài liệu. Nó không chứng minh ngữ nghĩa của mọi câu trả lời là đúng: model nhỏ có thể chọn nhầm đoạn, retrieval lexical còn hạn chế, tài liệu được duyệt vẫn cần đúng và cập nhật. Không dùng kết quả chấm điểm lexical như confidence.

Không lưu hội thoại/answer vào database, không cache answer dùng chung, không log câu hỏi/context/output provider. Model weights có thể được giữ trong RAM 5 phút theo keep_alive. Các request luôn dựng messages mới; API không nối lịch sử người dùng. Source được kiểm tra tại thời điểm query cuối trước trả response; bài có thể tiếp tục thay đổi sau đó, client cần mở lại nguồn để đối soát.

[Đồng bộ chỉ mục ở bước 4](knowledge-index-synchronization.md) giữ cơ chế GIN cùng transaction, bổ sung đối soát ID/revision và phục hồi bằng CLI. Không cần chạy index worker hoặc tải lại model khi sửa/thu hồi bài.

Global inference gate 1 request/API process, không queue; Ollama cấu hình 1 parallel/1 loaded model, max queue 2. Rate limit theo IP dùng nền tảng hiện có; khi triển khai sau reverse proxy cần cấu hình forwarded headers/IP đáng tin và giới hạn tổng theo hạ tầng. Không coi đây là bộ giới hạn phân tán cho nhiều replica.

## Nghiệm thu

```powershell
# Hồi quy không phụ thuộc AI; test model thật sẽ skip với lý do rõ ràng.
powershell -NoProfile -ExecutionPolicy Bypass -File be/tests/Run-PersistenceTests.ps1 -Configuration Release -Filter 'FullyQualifiedName~Knowledge|FullyQualifiedName~OllamaKnowledgeModelTests|FullyQualifiedName~CommunityTests|FullyQualifiedName~ModelTests|FullyQualifiedName~ApiFoundationTests'

# Sau setup Ollama/model: inference thật + API + PostgreSQL riêng, không sửa DB nghiệp vụ.
powershell -NoProfile -ExecutionPolicy Bypass -File be/tests/Run-LocalAssistantTests.ps1
```

Các lớp kiểm tra: unit service/validation, mocked HTTP protocol Ollama/schema/timeout/concurrency, anonymous API/rate limit, PostgreSQL public-source ACL và thay đổi nguồn trong lúc inference. Test thật opt-in dùng model local; CI không cần tải image/weights AI để kiểm tra các phần còn lại. Artifact test model thật ở `be/tests/Helpdesk.Persistence.Tests/bin/Release/net8.0/Knowledge/local-assistant-real-model-result.json`; không commit.

Kết quả đã chạy trong lần triển khai:

- Hồi quy backend liên quan: **131 pass**, 1 test model thật skip vì lúc đó chưa tải model. Sau cập nhật transport/Unicode, **65 unit/protocol/model checks pass**, build Release warn-as-error không warning/error.
- Sau setup, test **Ollama thật + API + PostgreSQL riêng: 1 pass, 0 skip**. Model `qwen3:1.7b`, ID `8f68893c685c`; trả Answered với nguồn đúng bước 2, thiếu tài liệu trả InsufficientInformation và model abstain khi evidence không liên quan. Cold request đo khoảng **16,8 giây** trên CPU cấu hình này; không phải cam kết latency cho mọi máy/câu hỏi.
- Chạy binary mới ở port riêng, chỉ đọc database đang dùng và tắt toàn bộ seed/worker: API trả InsufficientInformation, 0 source, có liên kết tạo Ticket; request quá 4 KiB trả **413**. Dừng API kiểm tra sau khi xong. Database nghiệp vụ không được seed/publish/migrate trong kiểm tra này.
- Container khác gọi được Ollama qua `host.docker.internal:11434`, đúng địa chỉ của profile Visual Studio Container. Runtime healthy, log xác nhận cloud disabled.
- Frontend typed public client: **5 API tests pass**, lint và production build pass. Chưa chạy lại toàn bộ frontend/browser journeys trong bước API này.

Tag model có thể được nhà phát hành cập nhật; khi đổi model/runtime hoặc pull lại một tag đã thay đổi cần chạy lại nghiệm thu nguồn/abstain. Corpus test không được chèn vào kho tri thức thực tế.

Kiểm tra lại ngày 02/11/2026 trong [bước đồng bộ chỉ mục](knowledge-index-synchronization.md): 146 tests liên quan pass, 0 skip, có model local thật. Câu có nguồn lần này mất khoảng 85 giây trong lúc chạy hồi quy trên CPU, với timeout test 120 giây; mặc định ứng dụng 60 giây có thể fallback Unavailable khi máy bận. Có thể đặt `LocalAssistant:TimeoutSeconds`/`LOCAL_ASSISTANT_TIMEOUT_SECONDS` lên 120 theo môi trường chạy, rồi đo lại; phản hồi thiếu tài liệu không gọi model.

Tham khảo chính thức: [Ollama chat API](https://docs.ollama.com/api/chat), [structured outputs](https://docs.ollama.com/capabilities/structured-outputs), [Docker CPU/GPU](https://docs.ollama.com/docker), [local-only/cloud và concurrency](https://docs.ollama.com/faq).
