using Microsoft.AspNetCore.Authorization;
using be.Api.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace be.Api;

public sealed class AuthenticationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var attributes = context.MethodInfo.GetCustomAttributes(true)
            .Concat(context.MethodInfo.DeclaringType?.GetCustomAttributes(true) ?? []);
        if (attributes.OfType<AllowAnonymousAttribute>().Any() || !attributes.OfType<AuthorizeAttribute>().Any()) return;
        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
            }
        ];
        AddProblemResponse(operation, context, "401", "Yêu cầu xác thực.");
        if (attributes.OfType<RequirePermissionAttribute>().Any())
            AddProblemResponse(operation, context, "403", "Bạn không có quyền thực hiện thao tác này.");
    }

    private static void AddProblemResponse(OpenApiOperation operation, OperationFilterContext context, string status, string description)
    {
        if (operation.Responses.ContainsKey(status)) return;
        operation.Responses.Add(status, new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new()
                {
                    Schema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository)
                }
            }
        });
    }
}
