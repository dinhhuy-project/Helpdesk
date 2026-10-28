using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using be.Application.Features.Accounts;
using be.Application.Features.Departments;
using be.Application.Features.SupportTeams;
using be.Application.Features.Categories;
using be.Application.Features.Tickets;
using be.Application.Features.Sla;

namespace be.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            SlaOperationException sla => sla.Error switch
            {
                SlaError.InvalidInput => 400,
                SlaError.NotFound => 404,
                SlaError.Unauthenticated => 401,
                SlaError.Forbidden => 403,
                _ => 409
            },
            TicketOperationException ticket => ticket.Error switch
            {
                TicketError.NotFound => 404,
                TicketError.Unauthenticated => 401,
                TicketError.Forbidden => 403,
                TicketError.StorageUnavailable => 503,
                TicketError.UnsupportedFile => 415,
                TicketError.AttachmentQuotaExceeded => 409,
                TicketError.InvalidFile => 400,
                TicketError.InvalidInput or TicketError.InvalidAssignment or TicketError.InvalidCategory or TicketError.ResolutionRequired => 400,
                _ => 409
            },
            CategoryOperationException category => category.Error switch
            {
                CategoryError.NotFound => 404,
                CategoryError.Unauthenticated => 401,
                CategoryError.InsufficientScope => 403,
                CategoryError.InvalidInput or CategoryError.InvalidParent or CategoryError.CategoryCycle => 400,
                _ => 409
            },
            TeamOperationException team => team.Error switch
            {
                TeamError.NotFound => 404,
                TeamError.Unauthenticated => 401,
                TeamError.InsufficientScope => 403,
                TeamError.InvalidInput or TeamError.InvalidDepartment or TeamError.InvalidUser or TeamError.InvalidLeader => 400,
                _ => 409
            },
            DepartmentOperationException department => department.Error switch
            {
                DepartmentError.NotFound => 404,
                DepartmentError.Unauthenticated => 401,
                DepartmentError.InsufficientScope => 403,
                DepartmentError.InvalidInput => 400,
                _ => 409
            },
            AccountOperationException account => account.Error switch
            {
                AccountError.NotFound => 404,
                AccountError.Unauthenticated => 401,
                AccountError.InsufficientScope => 403,
                AccountError.InvalidInput or AccountError.InvalidDepartment or AccountError.InvalidRole or AccountError.InvalidCurrentPassword => 400,
                _ => 409
            },
            DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
            DbUpdateException { InnerException: PostgresException { SqlState: "23505" or "23503" } }
                => StatusCodes.Status409Conflict,
            BadHttpRequestException badRequest => badRequest.StatusCode,
            _ => StatusCodes.Status500InternalServerError
        };
        // Do not log exception messages, bodies or database values; the trace identifies the request.
        logger.LogError("API failure {ExceptionType}; trace {TraceId}; status {StatusCode}",
            exception.GetType().Name, Activity.Current?.Id ?? context.TraceIdentifier, status);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = exception is SlaOperationException slaFailure ? slaFailure.Error switch
            {
                SlaError.InvalidInput => "Dữ liệu SLA không hợp lệ.",
                SlaError.NotFound => "Không tìm thấy dữ liệu trong phạm vi được phép.",
                SlaError.Unauthenticated => "Yêu cầu xác thực.",
                SlaError.Forbidden => "Thao tác vượt quá phạm vi được cấp quyền.",
                SlaError.ActivePolicyExists => "Priority đã có policy SLA active.",
                _ => "Dữ liệu đã thay đổi. Vui lòng tải lại."
            } : exception is TicketOperationException ticketFailure ? TicketTitle(ticketFailure.Error) :
                exception is CategoryOperationException categoryFailure ? CategoryTitle(categoryFailure.Error) :
                exception is TeamOperationException teamFailure ? TeamTitle(teamFailure.Error) :
                exception is DepartmentOperationException departmentFailure ? DepartmentTitle(departmentFailure.Error) :
                exception is AccountOperationException known ? AccountTitle(known.Error) : status switch
            {
                409 => "Dữ liệu bị xung đột.",
                400 => "Yêu cầu không hợp lệ.",
                _ => "Không thể xử lý yêu cầu."
            },
            Detail = status == 409 ? "Vui lòng tải lại dữ liệu và thử lại." : null
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        if (exception is AccountOperationException accountError) problem.Extensions["code"] = accountError.Error.ToString();
        if (exception is DepartmentOperationException departmentError) problem.Extensions["code"] = departmentError.Error.ToString();
        if (exception is TeamOperationException teamError) problem.Extensions["code"] = teamError.Error.ToString();
        if (exception is CategoryOperationException categoryError) problem.Extensions["code"] = categoryError.Error.ToString();
        if (exception is TicketOperationException ticketError) problem.Extensions["code"] = ticketError.Error.ToString();
        if (exception is SlaOperationException slaError) problem.Extensions["code"] = slaError.Error.ToString();
        if (status == 401) context.Response.Headers.WWWAuthenticate = "Bearer";
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }

    private static string TicketTitle(TicketError error) => error switch
    {
        TicketError.NotFound => "Không tìm thấy Ticket trong phạm vi được phép.",
        TicketError.Unauthenticated => "Yêu cầu xác thực.",
        TicketError.Forbidden => "Thao tác vượt quá phạm vi được cấp quyền.",
        TicketError.InvalidInput => "Yêu cầu Ticket không hợp lệ.",
        TicketError.InvalidCategory => "Danh mục không tồn tại hoặc không còn khả dụng trong cây danh mục.",
        TicketError.InvalidAssignment => "Nhóm hoặc nhân viên được chọn không hợp lệ trong phạm vi phân công.",
        TicketError.InvalidTransition => "Không thể chuyển Ticket từ trạng thái hiện tại sang trạng thái được yêu cầu.",
        TicketError.ResolutionRequired => "Cần nhập nội dung kết quả giải quyết trước khi resolve Ticket.",
        TicketError.InvalidFile => "File, tên file, nội dung hoặc kích thước không hợp lệ.",
        TicketError.UnsupportedFile => "Định dạng hoặc MIME của file không được hỗ trợ.",
        TicketError.AttachmentQuotaExceeded => "Đã đạt hạn mức file đính kèm cho phạm vi này của Ticket.",
        TicketError.UploadExpired => "Phiên upload đã hết hạn hoặc đang được dọn dẹp. Vui lòng tải lại dữ liệu.",
        TicketError.StorageUnavailable => "Dịch vụ lưu file chưa sẵn sàng. Vui lòng thử lại sau.",
        _ => "Ticket đã thay đổi. Vui lòng tải lại dữ liệu."
    };

    private static string CategoryTitle(CategoryError error) => error switch
    {
        CategoryError.NotFound => "Không tìm thấy danh mục Ticket.",
        CategoryError.NameInUse => "Tên danh mục đã được sử dụng trong cùng cấp, kể cả danh mục inactive.",
        CategoryError.VersionConflict => "Dữ liệu đã thay đổi. Vui lòng tải lại danh mục.",
        CategoryError.InvalidInput => "Thông tin danh mục không hợp lệ.",
        CategoryError.InvalidParent => "Parent không tồn tại hoặc có danh mục inactive trong chuỗi tổ tiên.",
        CategoryError.CategoryCycle => "Không thể chọn chính danh mục hoặc một node con làm parent.",
        CategoryError.Unauthenticated => "Yêu cầu xác thực.",
        _ => "Thao tác vượt quá phạm vi được cấp quyền."
    };

    private static string TeamTitle(TeamError error) => error switch
    {
        TeamError.NotFound => "Không tìm thấy dữ liệu nhóm trong phạm vi được phép.",
        TeamError.NameInUse => "Tên nhóm đã được sử dụng trong phòng ban này, kể cả nhóm đã ngừng hoạt động.",
        TeamError.VersionConflict => "Dữ liệu đã thay đổi. Vui lòng tải lại nhóm và thành viên.",
        TeamError.InvalidInput => "Thông tin nhóm hoặc thành viên không hợp lệ.",
        TeamError.InvalidDepartment => "Phòng ban không tồn tại hoặc đã ngừng hoạt động.",
        TeamError.InactiveTeam => "Nhóm đã ngừng hoạt động.",
        TeamError.InvalidUser => "Tài khoản không tồn tại hoặc đã ngừng hoạt động.",
        TeamError.MembershipExists => "Thành viên đã có trong nhóm. Sử dụng thao tác cập nhật trạng thái.",
        TeamError.InvalidLeader => "Trưởng nhóm phải là thành viên active và có quyền quản lý nhóm.",
        TeamError.Unauthenticated => "Yêu cầu xác thực.",
        _ => "Thao tác vượt quá phạm vi được cấp quyền."
    };

    private static string DepartmentTitle(DepartmentError error) => error switch
    {
        DepartmentError.NotFound => "Không tìm thấy phòng ban.",
        DepartmentError.NameInUse => "Tên phòng ban đã được sử dụng, kể cả phòng ban đã ngừng hoạt động.",
        DepartmentError.VersionConflict => "Dữ liệu đã thay đổi. Vui lòng tải lại.",
        DepartmentError.InvalidInput => "Thông tin phòng ban không hợp lệ.",
        DepartmentError.Unauthenticated => "Yêu cầu xác thực.",
        _ => "Thao tác vượt quá phạm vi được cấp quyền."
    };

    private static string AccountTitle(AccountError error) => error switch
    {
        AccountError.InvalidInput => "Dữ liệu đầu vào không hợp lệ.",
        AccountError.NotFound => "Không tìm thấy dữ liệu.",
        AccountError.VersionConflict => "Dữ liệu đã thay đổi. Vui lòng tải lại.",
        AccountError.EmailInUse => "Email đã được sử dụng.",
        AccountError.InvalidDepartment => "Phòng ban không tồn tại hoặc đã ngừng hoạt động.",
        AccountError.LastActiveAdmin => "Phải giữ ít nhất một SystemAdmin đang hoạt động.",
        AccountError.InvalidRole => "Role hoặc permission không hợp lệ.",
        AccountError.ProtectedAdminGrant => "SystemAdmin phải giữ quyền quản trị tài khoản và role.",
        AccountError.InvalidCurrentPassword => "Mật khẩu hiện tại không hợp lệ hoặc mật khẩu mới trùng mật khẩu cũ.",
        AccountError.PasswordlessAccount => "Tài khoản không có mật khẩu nội bộ.",
        AccountError.Unauthenticated => "Yêu cầu xác thực.",
        _ => "Thao tác vượt quá phạm vi được cấp quyền."
    };
}
