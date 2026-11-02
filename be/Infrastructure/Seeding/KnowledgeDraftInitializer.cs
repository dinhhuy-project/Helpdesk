using System.Security.Cryptography;
using System.Text;
using be.Application.Features.Community;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Identity;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Seeding;

// Original application-use guides. They are candidates for review, never automatically published.
public sealed class KnowledgeDraftInitializer(HelpdeskDbContext database, TimeProvider clock)
{
    private static Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("knowledge-drafts-v1:" + key))[..16]);
    public async Task<bool> SeedAsync(CancellationToken ct = default)
    {
        await using var tx = await database.Database.BeginTransactionAsync(ct);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", ct);
        if ((await database.Database.GetPendingMigrationsAsync(ct)).Any()) throw new IdentityInitializationException("Review and apply KnowledgeStandardization before creating reference drafts.");
        if (await database.IdentityAuditLogs.AnyAsync(x => x.Id == Id("completed"), ct)) return false;
        var actor = await (from u in database.Users join ur in database.UserRoles on u.Id equals ur.UserId
            join role in database.Roles on ur.RoleId equals role.Id join rp in database.RolePermissions on role.Id equals rp.RoleId
            join p in database.Permissions on rp.PermissionId equals p.Id
            where u.IsActive && role.Name == RoleNames.SystemAdmin && p.Code == PermissionCodes.KnowledgeManage
            orderby u.CreatedAt, u.Id select (Guid?)u.Id).FirstOrDefaultAsync(ct)
            ?? throw new IdentityInitializationException("An active SystemAdmin with knowledge.manage is required to prepare drafts.");
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var a in Articles)
        {
            var id = Id(a.Key);
            if (await database.CommunityEntries.AnyAsync(x => x.Id == id, ct)) throw new IdentityInitializationException("Reference draft identifier already exists without a completion marker; review before continuing.");
            var guide = new KnowledgeGuide(a.Scope, a.Prerequisites, a.Outcome, a.Escalation,
                "Hướng dẫn sử dụng Helpdesk, biên soạn theo chức năng ứng dụng; cần đơn vị rà soát trước khi xuất bản", SourceUpdatedOn: DateOnly.FromDateTime(now));
            if (!KnowledgeGuideRules.Complete(guide, a.Steps)) throw new IdentityInitializationException("Reference draft catalog is incomplete.");
            database.CommunityEntries.Add(new CommunityEntry { Id = id, Kind = CommunityKind.Knowledge, Title = a.Title, Summary = a.Summary,
                Content = a.Steps, Topic = a.Topic, Guide = guide, Classification = CommunityClassification.Unclassified,
                CreatedById = actor, CreatedAt = now, UpdatedAt = now });
        }
        database.IdentityAuditLogs.Add(new IdentityAuditLog { Id = Id("completed"), ActorId = actor, Action = "KnowledgeDraftsPrepared", CreatedAt = now,
            Data = "{\"Batch\":\"knowledge-drafts-v1\",\"Drafts\":8,\"Published\":false}" });
        await database.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }
    private sealed record Article(string Key, string Title, string Topic, string Summary, string Scope, string Prerequisites, string Steps, string Outcome, string Escalation);
    private static readonly Article[] Articles =
    [
        new("lookup", "Tra cứu hướng dẫn và sự cố trước khi tạo yêu cầu", "Sử dụng Helpdesk",
            "Tìm nội dung đã duyệt và kiểm tra sự cố hiện tại để chọn cách tự xử lý phù hợp.",
            "Người đọc cổng Helpdesk; không cần đăng nhập để xem nội dung công khai. Không áp dụng nội dung minh họa vào hệ thống thực tế.",
            "Có kết nối Internet và địa chỉ cổng Helpdesk của đơn vị.",
            "1. Mở Kho tri thức và giữ nguồn nội dung Phục vụ thực tế.\n2. Nhập từ khóa ngắn mô tả dịch vụ hoặc lỗi, rồi mở bài phù hợp để kiểm tra phạm vi và điều kiện áp dụng.\n3. Thực hiện từng bước được phép; dừng nếu gặp cảnh báo hoặc không có quyền cần thiết.\n4. Xem Sự cố chung ở chế độ thực tế để biết có thông báo đang liên quan đến dịch vụ hay không.\n5. Nếu tham khảo Wikipedia, xem đó là kiến thức bên ngoài và đối chiếu với hướng dẫn đã duyệt của đơn vị.",
            "Xác định được hướng dẫn phù hợp hoặc biết cần gửi yêu cầu hỗ trợ riêng.",
            "Đăng nhập và tạo Ticket nếu không có hướng dẫn phù hợp hoặc các bước an toàn chưa giải quyết được; cung cấp mã lỗi và thời điểm xảy ra."),
        new("create-ticket", "Tạo Ticket với thông tin đủ để nhóm hỗ trợ xử lý", "Sử dụng Helpdesk",
            "Chuẩn bị nội dung, chọn danh mục và kiểm tra kết quả gửi yêu cầu.",
            "Tài khoản active có quyền tạo Ticket; yêu cầu của chính người đang đăng nhập. Không dùng để tạo hộ hoặc tự gán người xử lý.",
            "Đã đăng nhập; có danh mục còn hoạt động và thông tin về vấn đề cần hỗ trợ.",
            "1. Tra cứu hướng dẫn và sự cố chung trước để tránh gửi yêu cầu lặp không cần thiết.\n2. Mở Tạo Ticket, nhập tiêu đề ngắn nêu dịch vụ và triệu chứng.\n3. Mô tả việc muốn làm, kết quả đang gặp, thời điểm bắt đầu, các bước tái hiện và cách đã thử. Không nhập mật khẩu hoặc mã xác thực.\n4. Chọn danh mục phù hợp từ danh sách/cây được phép và kiểm tra các trường bắt buộc.\n5. Gửi một lần, chờ trang chi tiết mở và ghi lại mã Ticket do hệ thống cấp.\n6. Nếu mất mạng hoặc chưa rõ kết quả, kiểm tra danh sách Ticket trước khi gửi lại.",
            "Có một Ticket trong phạm vi tài khoản, mã Ticket và thông tin rõ ràng để theo dõi.",
            "Nếu không có quyền hoặc không thấy danh mục phù hợp, liên hệ đầu mối Helpdesk được đơn vị công bố; không dùng tài khoản người khác."),
        new("follow-ticket", "Theo dõi trạng thái và phản hồi kết quả xử lý Ticket", "Sử dụng Helpdesk",
            "Xem chi tiết trong phạm vi được cấp và quyết định xác nhận hoặc yêu cầu xử lý tiếp.",
            "Người dùng được quyền đọc Ticket; thao tác xác nhận hoặc mở lại phụ thuộc quyền và trạng thái do ứng dụng hiển thị.",
            "Đã đăng nhập đúng tài khoản và có mã/liên kết Ticket thuộc phạm vi được phép.",
            "1. Mở danh sách Ticket, tìm theo mã hoặc bộ lọc phù hợp.\n2. Mở chi tiết và đọc trạng thái, trao đổi công khai, activity và hướng xử lý đang có.\n3. Khi nhóm hỗ trợ yêu cầu thông tin, bổ sung qua bình luận của chính Ticket.\n4. Khi có kết quả xử lý, thử lại trong phạm vi hướng dẫn được phép.\n5. Nếu ứng dụng cho phép, xác nhận đã giải quyết hoặc mở lại kèm thông tin vấn đề còn tồn tại; không coi nút bị ẩn là quyền đã được cấp.",
            "Biết tiến độ, cung cấp đúng phản hồi và giữ lịch sử trao đổi tập trung.",
            "Nếu không mở được liên kết, kiểm tra tài khoản đang dùng và gửi mã Ticket cho đầu mối hỗ trợ; không tìm cách truy cập dữ liệu ngoài quyền."),
        new("public-comment", "Bổ sung thông tin an toàn trong phần trao đổi Ticket", "Trao đổi và tài liệu",
            "Gửi thông tin tái hiện và kết quả đã thử mà không chia sẻ bí mật.",
            "Người có quyền bình luận trên Ticket. Hướng dẫn này dành cho trao đổi công khai trong phạm vi Ticket, không phải bài viết công khai trên Internet.",
            "Đã đăng nhập, mở được Ticket và kiểm tra nội dung trước khi gửi.",
            "1. Đọc trao đổi gần nhất để tránh gửi lại thông tin đã có.\n2. Nêu bước đã thử, kết quả, thời điểm và mã lỗi; chỉ đưa dữ liệu cần cho việc hỗ trợ.\n3. Che mật khẩu, OTP, token, thông tin cá nhân không liên quan trong nội dung và ảnh chụp.\n4. Kiểm tra đối tượng được xem bình luận theo giao diện rồi gửi một lần.\n5. Kiểm tra bình luận đã xuất hiện. Bình luận hiện chỉ thêm mới; nếu cần đính chính, gửi bổ sung thay vì trông chờ sửa hoặc xóa.",
            "Nhóm hỗ trợ nhận được thông tin hữu ích trong đúng Ticket, không lộ bí mật không cần thiết.",
            "Nếu đã gửi nhầm thông tin nhạy cảm, liên hệ đầu mối an toàn thông tin/Helpdesk theo quy trình đơn vị; không đăng lại thông tin đó vào cổng tri thức."),
        new("attachments", "Đính kèm và tải tài liệu Ticket đúng phạm vi", "Trao đổi và tài liệu",
            "Chuẩn bị file, đọc giới hạn trên giao diện và kiểm tra upload/download có xác thực.",
            "Tài khoản có quyền với Ticket/comment tương ứng; file nội bộ chỉ được đọc theo quyền của backend.",
            "Ticket hoặc comment đã tồn tại, storage đang bật, file phù hợp loại và kích thước mà giao diện cho phép.",
            "1. Kiểm tra nội dung file, loại bỏ bí mật và dữ liệu cá nhân không cần thiết.\n2. Chọn đúng Ticket hoặc bình luận cần gắn file; không giả định mọi người trong Ticket đều xem được file nội bộ.\n3. Xem giới hạn định dạng/kích thước hiện trên giao diện, chọn file hợp lệ và chờ upload hoàn tất.\n4. Kiểm tra file xuất hiện trong danh sách trước khi tải lại trang hoặc upload lại.\n5. Dùng nút tải file của Helpdesk khi đã đăng nhập; không chia sẻ đường dẫn kho lưu trữ riêng tư.",
            "File gắn đúng đối tượng và chỉ được tải qua API kiểm tra quyền.",
            "Nếu storage bị tắt hoặc upload chưa rõ kết quả, kiểm tra lại danh sách rồi liên hệ hỗ trợ kèm mã Ticket, tên file và thời điểm; không gửi khóa truy cập storage."),
        new("password-session", "Đổi mật khẩu và đăng nhập lại an toàn", "Tài khoản",
            "Tự đổi mật khẩu qua trang tài khoản và hiểu việc phiên bị thu hồi.",
            "Tài khoản nội bộ còn active và biết mật khẩu hiện tại. Không phải quy trình quên mật khẩu hoặc quản trị viên đặt lại mật khẩu.",
            "Đăng nhập được vào trang tài khoản; chuẩn bị mật khẩu mới theo yêu cầu hiển thị và không dùng chung với dịch vụ khác.",
            "1. Mở trang Tài khoản của tôi trên cổng Helpdesk chính thức.\n2. Chọn đổi mật khẩu, nhập mật khẩu hiện tại và mật khẩu mới theo yêu cầu của form.\n3. Gửi một lần và đọc thông báo kết quả.\n4. Sau khi đổi thành công, đăng nhập lại bằng mật khẩu mới; các phiên cũ bị thu hồi là hành vi bảo vệ tài khoản.\n5. Không chia sẻ mật khẩu, mã xác thực hoặc ảnh chụp có chứa thông tin đăng nhập.",
            "Đăng nhập được bằng mật khẩu mới và các phiên cũ không còn được dùng.",
            "Nếu không còn nhớ mật khẩu hiện tại hoặc tài khoản bị inactive, liên hệ đầu mối quản trị để xác minh danh tính theo quy trình đơn vị; không coi form đổi mật khẩu là reset mật khẩu."),
        new("notifications", "Đọc thông báo và quay lại Ticket liên quan", "Sử dụng Helpdesk",
            "Theo dõi thông báo của chính tài khoản và kiểm tra dữ liệu mới nhất khi kết nối gián đoạn.",
            "Người dùng đã đăng nhập; thông báo và Ticket liên quan vẫn phụ thuộc quyền hiện tại.",
            "Phiên active và trình duyệt có kết nối tới Helpdesk.",
            "1. Mở chuông hoặc trang Thông báo của tài khoản đang dùng.\n2. Lọc trạng thái đọc nếu cần rồi mở thông báo phù hợp.\n3. Theo liên kết về Ticket và đọc trạng thái hiện tại; nội dung cũ trong thông báo không thay thế dữ liệu mới nhất.\n4. Đánh dấu đã đọc sau khi kiểm tra theo thao tác giao diện hỗ trợ.\n5. Nếu mất kết nối trực tiếp, dùng nút tải lại khi mạng ổn định; nếu phiên hết hạn, đăng nhập lại.",
            "Xem được cập nhật trong đúng phạm vi và biết cách đối soát khi realtime bị gián đoạn.",
            "Nếu thông báo không còn mở được, có thể quyền hoặc dữ liệu đã đổi; liên hệ hỗ trợ với mã Ticket nếu có, không truy cập bằng phiên của người khác."),
        new("uncertain-submit", "Xử lý khi gửi yêu cầu bị timeout hoặc mất kết nối", "Sử dụng Helpdesk",
            "Đối soát kết quả trước khi gửi lại để hạn chế Ticket hoặc trao đổi bị trùng.",
            "Người dùng gặp thông báo chưa xác định được kết quả sau thao tác tạo Ticket, bình luận hoặc upload; không áp dụng để bỏ qua lỗi quyền/validation.",
            "Giữ lại thời điểm thao tác, tiêu đề Ticket hoặc tên file; không cần cung cấp mật khẩu/token cho việc kiểm tra.",
            "1. Đọc thông báo lỗi và giữ nội dung đã nhập nếu giao diện còn hiển thị.\n2. Chờ kết nối ổn định rồi kiểm tra danh sách Ticket, bình luận hoặc file ở đúng tài khoản.\n3. Nếu bản ghi đã tồn tại, tiếp tục dùng bản đó và không gửi lại thao tác tạo.\n4. Nếu chưa xác định được, kiểm tra lại phạm vi/bộ lọc và liên hệ hỗ trợ kèm thời điểm cùng thông tin nhận diện an toàn.\n5. Chỉ gửi lại sau khi đã đối soát phù hợp; thao tác cập nhật báo xung đột cần tải phiên bản mới và kiểm tra nội dung trước khi quyết định.",
            "Xác định được kết quả hoặc chuyển đúng thông tin cho hỗ trợ, giảm nguy cơ tạo bản ghi lặp.",
            "Nếu giao diện yêu cầu dừng hoặc kết quả vẫn chưa rõ, gửi thông tin qua đầu mối hỗ trợ được công bố; không tự sửa dữ liệu hoặc dùng công cụ vượt quyền.")
    ];
}
