using System.Security.Cryptography;
using System.Text;
using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Identity;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace be.Infrastructure.Seeding;

// Curated examples only. No existing Ticket, internal note or employee information is published.
public sealed class CommunityDemoInitializer(HelpdeskDbContext database, TimeProvider clock)
{
    private static Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("community-demo-v1:" + key))[..16]);
    public async Task<bool> SeedAsync(CancellationToken ct = default)
    {
        await using var tx = await database.Database.BeginTransactionAsync(ct);
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", ct);
        if ((await database.Database.GetPendingMigrationsAsync(ct)).Any()) throw new IdentityInitializationException("Review and apply migrations before seeding public community examples.");
        if (await database.IdentityAuditLogs.AnyAsync(x => x.Id == Id("completed"), ct)) return false;
        var admin = await (from u in database.Users join ur in database.UserRoles on u.Id equals ur.UserId
            join role in database.Roles on ur.RoleId equals role.Id where u.IsActive && role.Name == RoleNames.SystemAdmin
            orderby u.CreatedAt, u.Id select (Guid?)u.Id).FirstOrDefaultAsync(ct) ?? throw new IdentityInitializationException("Bootstrap an active administrator first.");
        string[] required = [PermissionCodes.KnowledgeManage, PermissionCodes.KnowledgePublish, PermissionCodes.IncidentManage, PermissionCodes.IncidentPublish];
        var grants = await (from ur in database.UserRoles join rp in database.RolePermissions on ur.RoleId equals rp.RoleId
            join p in database.Permissions on rp.PermissionId equals p.Id where ur.UserId == admin select p.Code).Distinct().ToArrayAsync(ct);
        if (required.Except(grants).Any()) throw new IdentityInitializationException("Grant community administration permissions before publishing demo content.");
        var now = clock.GetUtcNow().UtcDateTime;
        (string Title, string Topic, string Summary, string Content)[] articles =
        [
            ("Kiểm tra kết nối VPN trước khi gửi yêu cầu", "Mạng và VPN", "Các bước cơ bản để kiểm tra kết nối an toàn và ghi nhận thông tin lỗi.", "1. Kiểm tra kết nối Internet bằng một trang web thông thường.\n2. Kiểm tra thời gian trên thiết bị và ứng dụng VPN đang dùng.\n3. Ngắt kết nối VPN rồi kết nối lại bằng ứng dụng được tổ chức phê duyệt.\n4. Nếu vẫn lỗi, ghi lại thông báo lỗi và thời điểm xảy ra để gửi nhóm hỗ trợ.\nKhông chia sẻ mật khẩu, mã OTP hoặc cấu hình có chứa bí mật lên diễn đàn công khai."),
            ("Email không đồng bộ: những điều cần kiểm tra", "Email", "Phân biệt lỗi kết nối, ứng dụng và dung lượng hộp thư.", "1. Kiểm tra thư trên cổng webmail chính thức.\n2. Kiểm tra kết nối mạng và dung lượng hộp thư.\n3. Khởi động lại ứng dụng email, kiểm tra thư mục thư rác.\n4. Nếu webmail hoạt động nhưng ứng dụng không đồng bộ, ghi lại phiên bản ứng dụng và mã lỗi.\nChỉ dùng liên kết đăng nhập chính thức do tổ chức cung cấp."),
            ("Nhận biết email lừa đảo và bảo vệ tài khoản", "An toàn thông tin", "Dừng lại, kiểm tra và báo cáo khi email yêu cầu cung cấp thông tin nhạy cảm.", "Không nhập mật khẩu hoặc mã xác thực qua liên kết không rõ nguồn gốc.\nKiểm tra địa chỉ người gửi và tên miền; một tên hiển thị quen thuộc chưa đủ để xác minh.\nKhông mở tệp đính kèm đáng ngờ. Báo cáo email qua kênh an toàn của tổ chức.\nNếu đã cung cấp thông tin đăng nhập, liên hệ bộ phận hỗ trợ ngay và làm theo quy trình bảo vệ tài khoản."),
            ("Máy in chưa nhận lệnh: hướng dẫn kiểm tra ban đầu", "Thiết bị", "Các bước tự kiểm tra máy in dùng chung trước khi nhờ hỗ trợ.", "1. Kiểm tra nguồn điện, giấy và thông báo trên màn hình máy in.\n2. Xác nhận đã chọn đúng máy in và thiết bị cùng mạng cho phép.\n3. Kiểm tra hàng đợi in; không tự xóa lệnh của người khác.\n4. Ghi lại mã thiết bị, vị trí và thông báo lỗi nếu cần hỗ trợ.\nKhông mở cụm sấy hoặc bộ phận kỹ thuật khi chưa được hướng dẫn."),
            ("Cách mô tả yêu cầu hỗ trợ để được xử lý nhanh hơn", "Sử dụng Helpdesk", "Thông tin rõ ràng giúp nhóm hỗ trợ xác định vấn đề và giảm trao đổi lặp lại.", "Nêu việc bạn muốn thực hiện, điều đã xảy ra và thời điểm bắt đầu.\nBổ sung các bước tái hiện, phạm vi ảnh hưởng và thông báo lỗi.\nKiểm tra mục Sự cố chung trước khi tạo yêu cầu tương tự.\nVới nội dung nhạy cảm, gửi qua Ticket có phân quyền; không đưa mật khẩu, OTP hoặc dữ liệu cá nhân vào bài viết công khai."),
            ("Theo dõi sự cố chung và xác nhận bị ảnh hưởng", "Sử dụng Helpdesk", "Tra cứu tiến trình mà không cần đăng nhập; đăng nhập khi muốn xác nhận ảnh hưởng.", "Mở mục Sự cố chung và tìm dịch vụ hoặc chủ đề liên quan.\nĐọc mô tả, hướng dẫn tạm thời và tiến trình xử lý.\nSau khi đăng nhập, chọn Tôi cũng gặp vấn đề này nếu thông tin phù hợp. Mỗi tài khoản chỉ được tính một lần cho một sự cố.\nNếu vấn đề khác với mô tả sự cố chung, hãy tạo Ticket riêng và cung cấp thông tin cần thiết.")
        ];
        for (var i = 0; i < articles.Length; i++)
        {
            var a = articles[i]; var at = now.AddDays(-2).AddHours(i);
            database.CommunityEntries.Add(new CommunityEntry { Id = Id($"article:{i}"), Kind = CommunityKind.Knowledge,
                Title = a.Title, Summary = a.Summary, Content = a.Content, Topic = a.Topic,
                PublicTitle = a.Title, PublicSummary = a.Summary, PublicContent = a.Content, PublicTopic = a.Topic,
                CreatedById = admin, CreatedAt = at, UpdatedAt = at, IsPublished = true, PublicRevision = 1, PublishedAt = at });
        }
        foreach (var resolved in new[] { false, true })
        {
            var at = now.AddHours(resolved ? -12 : -2); var key = resolved ? "resolved" : "active";
            var title = resolved ? "[Demo] Đã khôi phục chức năng in văn phòng" : "[Demo] Đang kiểm tra kết nối VPN gián đoạn";
            var summary = "Tình huống minh họa của đồ án, không phải thông báo sự cố thực tế.";
            var status = resolved ? IncidentStatus.Resolved : IncidentStatus.Investigating;
            var entry = new CommunityEntry { Id = Id($"incident:{key}"), Kind = CommunityKind.Incident, Title = title, Summary = summary,
                Content = "Nội dung mô phỏng phục vụ nghiệm thu. Theo dõi timeline để hiểu cách nhóm hỗ trợ cập nhật tình trạng dịch vụ.", Topic = "Minh họa",
                Status = status, PublicTitle = title, PublicSummary = summary, PublicTopic = "Minh họa", PublicStatus = status,
                CreatedById = admin, CreatedAt = at, UpdatedAt = resolved ? at.AddHours(1) : at, IsPublished = true,
                PublicRevision = resolved ? 2 : 1, PublishedAt = resolved ? at.AddHours(1) : at };
            entry.PublicContent = entry.Content; database.CommunityEntries.Add(entry);
            database.IncidentUpdates.Add(new IncidentUpdate { Id = Id($"update:{key}:1"), EntryId = entry.Id, Message = "[Demo] Nhóm hỗ trợ đã tiếp nhận và đang xác minh tình huống.",
                Status = IncidentStatus.Investigating, Revision = 1, CreatedAt = at });
            if (resolved) database.IncidentUpdates.Add(new IncidentUpdate { Id = Id($"update:{key}:2"), EntryId = entry.Id,
                Message = "[Demo] Đã kiểm tra khôi phục dịch vụ và kết thúc tình huống mô phỏng.", Status = IncidentStatus.Resolved, Revision = 2, CreatedAt = at.AddHours(1) });
        }
        database.IdentityAuditLogs.Add(new IdentityAuditLog { Id = Id("completed"), ActorId = admin, Action = "CommunityDemoSeeded", CreatedAt = now,
            Data = "{\"Batch\":\"community-demo-v1\",\"KnowledgeArticles\":6,\"Incidents\":2,\"CuratedPublicExamples\":true}" });
        await database.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }
}
