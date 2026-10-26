using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using be.Application.Features.Accounts;
using be.Application.Features.Departments;

namespace be.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
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
                AccountError.InvalidDepartment or AccountError.InvalidRole or AccountError.InvalidCurrentPassword => 400,
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
            Title = exception is DepartmentOperationException departmentFailure ? DepartmentTitle(departmentFailure.Error) :
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
        if (status == 401) context.Response.Headers.WWWAuthenticate = "Bearer";
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }

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
