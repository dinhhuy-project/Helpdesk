using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace be.Api;

public sealed class FileResponseOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var binary = context.MethodInfo.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), true)
            .Cast<ProducesResponseTypeAttribute>().Where(attribute => typeof(FileResult).IsAssignableFrom(attribute.Type));
        foreach (var response in binary)
            if (operation.Responses.TryGetValue(response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture), out var item))
                item.Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/octet-stream"] = new() { Schema = new OpenApiSchema { Type = "string", Format = "binary" } }
                };
    }
}
