# Chuẩn hóa tài liệu trước khi tích hợp AI

Đây là bước chuẩn bị nội dung và quy trình duyệt, chưa thêm mô hình AI, embedding hoặc vector database. Bài tri thức dùng cấu trúc thống nhất, dữ liệu minh họa tách khỏi nguồn phục vụ thực tế. Cần [review/apply KnowledgeStandardization](knowledge-standardization-review.md) trước khi chạy API mới.

Sau chuẩn hóa, [bước 2 truy xuất đoạn hướng dẫn](knowledge-retrieval.md) chỉ dùng snapshot Operational đã xuất bản; cần review/apply migration tìm kiếm riêng. Việc đã migrate bước 1 không tự biến bản nháp hoặc nội dung demo thành corpus thực tế.

## Một bài hướng dẫn cần có gì?

| Trường | Quy tắc |
| --- | --- |
| Tiêu đề, tóm tắt, chủ đề | Nêu vấn đề và cách giải quyết, tránh đặt tiêu đề theo người gửi hoặc thông tin riêng tư. Giữ giới hạn 200/1.000/100 ký tự hiện có. |
| Phân loại | `Unclassified` — chưa phân loại; `Operational` — đã chọn để phục vụ thực tế; `Demo` — minh họa. Mặc định bản mới/nhập từ nguồn là chưa phân loại. |
| Phạm vi áp dụng | Đối tượng, dịch vụ, hệ điều hành/phiên bản, trường hợp không áp dụng. Tối đa 2.000 ký tự. |
| Điều kiện trước khi thực hiện | Quyền truy cập, thiết bị, dữ liệu cần chuẩn bị; ghi rõ nếu không có điều kiện đặc biệt. Tối đa 2.000 ký tự. |
| Nội dung hướng dẫn | 1–20 bước, đánh số liên tục `1.`, `2.`, ... hoặc `1)`, `2)`. Mỗi bước có thao tác/kết quả dễ kiểm tra; có thể xuống dòng bổ sung chi tiết cho cùng bước. Tối đa 4.000 ký tự/bước và 30.000 toàn nội dung. |
| Kết quả cần đạt | Người đọc nhận biết khi nào đã xử lý thành công. Tối đa 2.000 ký tự. |
| Khi nào cần gửi Ticket | Khi nào phải dừng, thông tin an toàn cần cung cấp và kênh hỗ trợ đúng phạm vi. Tối đa 2.000 ký tự. |
| Nguồn tài liệu/đơn vị biên soạn | Bắt buộc trước khi xuất bản, tối đa 500 ký tự. Không tự xem giấy phép phần mềm wiki là giấy phép tài liệu. |
| Liên kết nguồn | Không bắt buộc, tối đa 1.000 ký tự; chỉ HTTPS và không có user/password trong URL. Chỉ nhập URL được phép công khai, không dùng token hoặc URL riêng của Ticket/wiki nội bộ. |
| Ngày cập nhật tại nguồn | Không bắt buộc; để trống nếu không biết. Định dạng API `yyyy-MM-dd`, từ 1900 và không lớn hơn ngày UTC hiện tại. Không tự bịa ngày nguồn. |
| Ngày rà soát/xuất bản | Backend lấy UTC khi publisher duyệt. Đây là `publishedAt` của snapshot và đi cùng `revision`, khác ngày cập nhật của nguồn. |

Bản nháp có thể chưa đầy đủ; mọi trường vẫn phải đúng kiểu và giới hạn. Khi publish, backend bắt buộc chọn Operational/Demo; với Knowledge cần đủ các trường nội dung và bước hợp lệ. Incident cần phân loại nhưng không nhận object `guide`.

Mẫu nội dung phần bước:

```text
1. Mở dịch vụ chính thức của đơn vị và kiểm tra trạng thái đăng nhập.
2. Thực hiện thao tác kiểm tra được phép; ghi lại thông báo lỗi nếu có.
   Không đưa mật khẩu hoặc mã xác thực vào ảnh chụp.
3. Đối chiếu kết quả với mục Kết quả cần đạt; dừng nếu gặp cảnh báo.
```

## Quy trình biên tập và xuất bản

1. Người có `knowledge.manage` tạo bài hoặc nhập bản nháp từ BookStack/Ticket được phép đọc.
2. Chọn phân loại, điền phạm vi/điều kiện/kết quả/escalation/nguồn. Biên tập các bước rõ ràng trong “Nội dung hướng dẫn”. Nút đánh số chỉ định dạng từng dòng, không xác nhận tính đúng đắn; đọc lại sau khi dùng.
3. Lưu bản nháp. Nội dung và metadata đã public vẫn giữ nguyên cho đến lần duyệt tiếp theo.
4. Người có `knowledge.publish` xem preview, kiểm tra phạm vi, từng thao tác, ghi công/quyền sử dụng và thông tin nhạy cảm. Xác nhận được phép công khai rồi publish với version hiện tại.
5. Mở đúng cổng thực tế hoặc demo để nghiệm thu. Nếu thay đổi classification/guide/content trong draft, phải lưu và duyệt lại để thay snapshot. Xung đột giữ nội dung đang nhập và yêu cầu đối soát version mới.

Thông tin nguồn trong `guide` là metadata có thể biên tập, không phải chứng thư xác minh độc lập. Dấu thời gian publisher duyệt cũng không tự chứng minh thao tác đã được thử trên mọi hệ thống. Cần người chịu trách nhiệm nội dung của đơn vị kiểm tra trước khi dùng thực tế.

## Tách dữ liệu minh họa

- Mặc định `/knowledge`, `/incidents` và API public chỉ lấy `PublicClassification=Operational`.
- Để xem mẫu, chọn “Dữ liệu minh họa” hoặc dùng `?dataset=demo`. Chi tiết/timeline demo cũng phải có query này; gọi cùng ID qua dataset thực tế trả 404 và ngược lại.
- Màn hình demo có banner/badge, không trình bày tình huống mô phỏng như sự cố đang xảy ra thật. Phân trang, tìm kiếm và count đều lọc dataset ở backend trước khi thực hiện.
- Query key frontend chứa dataset, link bài giữ dataset; realtime reset/refetch cả các query public khi publish/withdraw. Dữ liệu demo không dùng làm nguồn AI phục vụ thực tế.
- `Unclassified` chỉ xuất hiện trong quản trị, kể cả bản ghi cũ còn có `isPublished=true`. Quản trị lọc `classification=Unclassified` để lập danh sách cần rà soát.
- Migration chỉ tự gắn Demo cho 8 GUID xác định của bộ `community-demo-v1`. Không suy loại từ từ “demo” trong tiêu đề và không tự cấp nhãn Operational cho nội dung cũ.

Sau migration, cổng thực tế có thể chưa có bài. Đây là trạng thái cần biên tập/duyệt lại; không bật demo mặc định để lấp chỗ trống. Các Ticket/dashboard/demo tuần không bị đổi trong bước này.

## Nhập từ nguồn ngoài hoặc Ticket

BookStack tiếp tục nhập một lần, không ghi đè bài đã nhập. Bản mới giữ phân loại Unclassified và có sẵn tên nguồn/revision/ngày nguồn nếu API cung cấp; phạm vi và quy trình vẫn cần người biên tập bổ sung. Footer ghi công/quyền sử dụng còn nằm trong nội dung để người duyệt xem, nhưng nên biên tập lại thành các bước và metadata phù hợp, tránh lặp nội dung không cần thiết.

Bản nháp từ Ticket chỉ lấy thông tin đã được phép theo ACL hiện có; SourceTicketId vẫn riêng tư, không trở thành URL nguồn public. Không sao chép mô tả, internal note, file hoặc thông tin cá nhân vào tài liệu công khai. Wikipedia hiện chỉ là liên kết tham khảo bên ngoài, không được tự nhập làm tài liệu Operational.

## Chuẩn bị 8 bản nháp hướng dẫn sử dụng ứng dụng

Sau khi migrate, dùng terminal Development với connection/user-secrets đúng:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$previousDraftSeed = $env:KnowledgeDraftSeed__Enabled
try {
    $env:KnowledgeDraftSeed__Enabled = 'true'
    dotnet run --project be --no-launch-profile -- seed-knowledge-drafts
}
finally { $env:KnowledgeDraftSeed__Enabled = $previousDraftSeed }
```

Yêu cầu SystemAdmin active có `knowledge.manage`. Lệnh tạo một lần 8 bản nháp, chưa phân loại/chưa xuất bản, có cấu trúc đầy đủ:

1. Tra cứu hướng dẫn và sự cố trước khi tạo yêu cầu.
2. Tạo Ticket với thông tin đủ để nhóm hỗ trợ xử lý.
3. Theo dõi trạng thái và phản hồi kết quả Ticket.
4. Bổ sung thông tin an toàn trong trao đổi Ticket.
5. Đính kèm và tải tài liệu đúng phạm vi.
6. Đổi mật khẩu và đăng nhập lại an toàn.
7. Đọc thông báo và quay lại Ticket liên quan.
8. Đối soát khi gửi yêu cầu bị timeout/mất kết nối.

Đây là nội dung ứng viên do project biên soạn theo chức năng ứng dụng; không phải quy trình đã được đơn vị phê duyệt. Chạy lặp giữ chỉnh sửa và không tạo trùng, không reset mẫu đã bị xóa sau lần seed. CLI không chạy khi F5; không tự cấp quyền hoặc publish bài. Bộ 6 bài/2 sự cố demo cũ tiếp tục là dữ liệu Demo, tách khỏi 8 bản nháp này.

## Contract bổ sung

`SaveCommunityEntry` có thêm:

```json
{
  "classification": "Operational",
  "guide": {
    "applicability": "Người dùng Helpdesk có quyền tạo Ticket cho chính mình.",
    "prerequisites": "Phiên active và danh mục còn hoạt động.",
    "expectedOutcome": "Ticket được tạo và có mã để theo dõi.",
    "escalation": "Liên hệ đầu mối hỗ trợ nếu không có quyền hoặc kết quả gửi chưa rõ.",
    "sourceName": "Nhóm phụ trách Helpdesk biên soạn và rà soát",
    "sourceUrl": null,
    "sourceUpdatedOn": null
  }
}
```

Body đầy đủ vẫn có `title`, `summary`, `content`, `topic`, `status` và `version` khi cập nhật. Incident dùng `guide:null`.

- Public list/detail có `classification`. Detail có `guide`; list để guide null nhằm trả dữ liệu gọn.
- Managed DTO có draft `classification`, `guide` và `publicClassification` để phân biệt bản đang biên tập với snapshot.
- List public: `dataset=operational` mặc định hoặc `dataset=demo`. Detail cùng query; timeline incident cũng nhận dataset.
- List quản trị: `classification=Unclassified|Operational|Demo`, không truyền để lấy tất cả draft.
- Lỗi publish `ClassificationRequired` hoặc `IncompleteGuide` trả 400 với ProblemDetails/traceId; version/ACL/session tiếp tục do backend kiểm tra.

## Tiêu chí nghiệm thu bước 1

Tạo được draft chưa hoàn chỉnh; không publish được nếu thiếu phân loại, metadata hoặc bước đánh số sai. Nguồn có date sai tương lai hoặc URL không hợp lệ bị từ chối. Metadata public giữ snapshot cũ khi editor sửa draft. Demo không xuất hiện qua list/search/count/detail/timeline thực tế và ngược lại. Thu hồi bài tiếp tục làm public 404.

Các bước chuẩn bị RAG sau này phải lấy dữ liệu Operational còn published và revision hiện hành, không quét trực tiếp mọi `isPublished=true`. Index/vector cache không thay thế bộ lọc này. Hướng dẫn ở đây chưa cung cấp chatbot hoặc xác nhận chất lượng retrieval/LLM.

## Kết quả kiểm tra triển khai

- 61 kiểm thử backend liên quan đã pass, không skip, trên PostgreSQL/MinIO test riêng: migration/model, metadata/snapshot, dataset, ACL, import và seed draft/rollback.
- Sau khi siết thêm các trường hợp bước 0, bước 100, thiếu khoảng trắng, bước rỗng và số có zero đầu, đã build Release warn-as-error thành công và chạy lại 24 test rule/model, tất cả pass.
- Toàn bộ 510 frontend tests/37 file pass; lint, TypeScript và build pass. Có kiểm tra API response cũ/khác dataset phải báo lỗi thay vì hiển thị như nội dung thực tế.
- Browser Community trên production bundle pass: xem bài/incident demo theo dataset riêng, form metadata, lưu/duyệt, feedback và hub JS 10 với server 8. Trang public được kiểm tra ở 390px và axe không có vi phạm trong ngữ cảnh đó.
- Các lệnh migration/seed trong tài liệu chưa được chạy vào database Helpdesk hiện có. Cần review/apply migration rồi chủ động tạo/rà soát các bản nháp; không có bài Operational được tự xuất bản trong quá trình triển khai.
