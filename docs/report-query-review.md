# Rà soát query/index cho báo cáo

Phần 17 không cần migration mới. Đã chạy EXPLAIN ANALYZE + BUFFERS trên **SQL thực tế do ReportStore/Npgsql 8 tạo**, với tham số bind từ request API; không chỉ xem LINQ hoặc GenerateCreateScript.

## Dữ liệu và cách đo

`ReportingQueryPlanTests` tạo database con GUID trong PostgreSQL test rồi migrate model hiện tại. Seed 8.000 Ticket và 8.000 TicketSla; 7.900 Ticket cũ ngoài khoảng mặc định, 100 Ticket gần đây thuộc cohort. Có hai team và ba actor: read.all nhìn 100, read.own nhìn 25, read.team nhìn 50. Có response đã ghi và chưa phản hồi để SQL thực sự tính các nhánh SLA/AVG, thay vì chỉ benchmark bảng snapshot rỗng.

Test gọi API dashboard, group page và daily; interceptor lưu SQL/typed parameters trong memory rồi EXPLAIN chính các command đó. Group page có pageSize=1 để xác minh LIMIT/OFFSET sau GROUP BY. ANALYZE chạy trong DB test trước đo. Candidate `ix_tickets_created_at` chỉ được drop/create trong database con sở hữu bởi test để so sánh A/B; không chỉnh index hoặc dữ liệu DB Helpdesk thật.

Kết quả đầy đủ mỗi lần nằm ở `be/tests/Helpdesk.Persistence.Tests/bin/<Configuration>/net8.0/report-query-plan.json`. Artifact chỉ có số đo, tên index/node và row count, không chứa JWT, credentials, nội dung Ticket hoặc SQL parameter values; không commit bin/ vào git.

## Một lần đo trong lượt triển khai

Đơn vị thời gian dưới là execution time của EXPLAIN trong PostgreSQL, **không phải latency HTTP end-to-end**. Shared hit blocks tính cả các quan hệ phục vụ ACL và SLA, không chỉ bảng tickets. Các lần chạy có dao động do cache/scheduling.

| Actor/query | Index hiện có: ms / shared hits | Thêm candidate CreatedAt: ms / shared hits |
| --- | --- | --- |
| all / dashboard | 1.876 / 832 | 3.746 / 826 |
| all / group page | 4.127 / 665 | 2.305 / 832 |
| all / daily | 1.171 / 535 | 1.552 / 529 |
| own / dashboard | 3.924 / 638 | 2.448 / 632 |
| own / group page | 5.261 / 696 | 7.809 / 638 |
| own / daily | 2.061 / 566 | 2.655 / 560 |
| team / dashboard | 2.915 / 705 | 2.693 / 699 |
| team / group page | 3.973 / 688 | 3.401 / 705 |
| team / daily | 2.588 / 558 | 3.729 / 552 |

Dashboard trả một aggregate row; daily một row vì 100 Ticket gần đây cùng ngày; group page trả một row đúng pageSize, không tải 100/8.000 Ticket về application để đếm. SQL có COUNT/AVG/GROUP BY, group page có LIMIT. Thử nghiệm SLA outcome/ACL riêng kiểm tra độ đúng của dữ liệu, không dùng EXPLAIN thay cho negative tests.

## Quyết định

Giữ index hiện có: `ix_tickets_status_created_at`, `ix_tickets_assigned_team_id_status_created_at`, `ix_tickets_assigned_agent_id_status_created_at`, `ix_tickets_requester_id_created_at`, các PK/FK, index permission/session và TicketSla. Planner thực tế chọn các index hiện có cho cohort; candidate CreatedAt được sử dụng khi có, nhưng không giảm chi phí đều ở ba phạm vi/query. Dashboard/daily chỉ giảm 6 shared-hit blocks, group page có lần tăng hits hoặc latency.

Với tập đo này, chưa có bằng chứng đủ để thêm một index gần trùng prefix thời gian, tăng chi phí ghi và yêu cầu thêm DDL vào môi trường đang chạy. Vì vậy **model vẫn 20 entity/tám migration**, phần 17 không thêm index hoặc áp dụng SQL mới. Đây là quyết định từ phép đo hiện tại, không phải cam kết mọi dữ liệu production đều nhanh như vậy.

Khi dữ liệu/distribution lớn hơn, đo lại với số lượng user/role/team, tỷ lệ có SLA, khoảng thời gian và selectivity thật; xem kế hoạch từng relation, buffer reads và latency nhiều lần. Có thể đánh giá CreatedAt hoặc index khác khi bằng chứng mới cho thấy cần. Tránh ép enable_seqscan hoặc tạo thêm index cho mọi filter chỉ để kế hoạch trông đẹp.

## Tính đúng và an toàn

- ACL lấy từ TicketScope trước aggregation; joins membership/grant dạng điều kiện tồn tại không nhân đôi Ticket nhiều role.
- Dashboard chỉ một SQL aggregate cho KPI/status/priority/SLA; không cộng các tập đọc khác nhau trong application.
- Group count/page/label cùng RepeatableRead; chỉ lookup label của ID trên trang đã scope, không N+1 hoặc lộ roster trống.
- So lại session/read-scope ở cuối; thay quyền/membership trong khi truy vấn dẫn đến 401/403/409 thay vì trả payload đã đọc dưới phạm vi cũ.
- Chỉ zero-fill danh sách ngày đã group, tối đa 367 bucket cho khoảng 366 ngày; không materialize Ticket hoặc comment/file để tính thống kê.
- Không cache thống kê theo key dùng chung hoặc đưa nội dung internal vào report; response no-store.

Chạy lại qua `& ./be/tests/Run-PersistenceTests.ps1 -Filter 'FullyQualifiedName~Reporting'`; script tạo containers tạm, benchmark tự tạo/drop đúng database con có prefix test. Không chạy lệnh drop/create index của benchmark trực tiếp trên database ứng dụng.
