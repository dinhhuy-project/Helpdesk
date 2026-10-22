using System.Diagnostics;
using System.Threading.RateLimiting;
using be.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace be.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApiFoundation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Configuration.CorsOptions>().Bind(configuration.GetSection(Configuration.CorsOptions.SectionName))
            .Validate(Configuration.CorsOptions.IsValid, "Cors:AllowedOrigins must contain exact HTTP(S) origins without paths or wildcards.")
            .ValidateOnStart();
        services.AddOptions<AuthRateLimitOptions>().Bind(configuration.GetSection(AuthRateLimitOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddControllers().ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState.Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(x => x.Key, _ => new[] { "Giá trị không hợp lệ." });
                var problem = new ValidationProblemDetails(errors)
                {
                    Status = 400, Title = "Dữ liệu đầu vào không hợp lệ."
                };
                problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
                var result = new BadRequestObjectResult(problem);
                result.ContentTypes.Add("application/problem+json");
                return result;
            };
        });
        services.AddCors();
        services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
            .Configure<IOptions<Configuration.CorsOptions>>((cors, settings) => cors.AddPolicy(ApiPolicies.Cors, policy =>
            {
                if (settings.Value.AllowedOrigins.Length > 0)
                    policy.WithOrigins(settings.Value.AllowedOrigins).AllowAnyHeader().AllowAnyMethod();
                // Cookie credentials will be considered when the authentication transport is implemented.
            }));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(ApiPolicies.AuthenticationRateLimit, context =>
            {
                var settings = context.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit, Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                        QueueLimit = 0, AutoReplenishment = true
                    });
            });
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                await Results.Problem(statusCode: 429, title: "Quá nhiều yêu cầu. Vui lòng thử lại sau.")
                    .ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }
}
