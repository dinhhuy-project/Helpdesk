using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace be.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
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
            Title = status switch
            {
                409 => "Dữ liệu bị xung đột.",
                400 => "Yêu cầu không hợp lệ.",
                _ => "Không thể xử lý yêu cầu."
            },
            Detail = status == 409 ? "Vui lòng tải lại dữ liệu và thử lại." : null
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
