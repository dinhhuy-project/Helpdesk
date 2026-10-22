
using be.Infrastructure.Persistence;
using be.Infrastructure.Identity;
using be.Api;

namespace be
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var isIdentityCommand = IdentityCommands.IsCommand(args);
            var builder = WebApplication.CreateBuilder(isIdentityCommand ? [] : args);

            // Add services to the container.

            builder.Services.AddApiFoundation(builder.Configuration);
            builder.Services.AddPersistence(builder.Configuration);
            builder.Services.AddIdentityInitialization();
            // Framework diagnostics include raw exceptions; our handler emits safe trace-based logs.
            builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
            builder.Services.AddHealthChecks();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            if (isIdentityCommand)
            {
                await using (app)
                    return await IdentityCommands.ExecuteAsync(args, app.Services, builder.Configuration);
            }

            app.UseExceptionHandler();
            app.UseStatusCodePages();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // Local Docker uses HTTP; production must terminate TLS at its proxy.
            if (!app.Environment.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }

            app.UseRouting();
            app.UseCors(ApiPolicies.Cors);
            app.UseRateLimiter();
            app.UseAuthorization();


            app.MapControllers();
            app.MapHealthChecks("/health/live");

            await app.RunAsync();
            return 0;
        }
    }
}
