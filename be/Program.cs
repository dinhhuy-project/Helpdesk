
using be.Infrastructure.Persistence;
using be.Infrastructure.Identity;
using be.Api;
using be.Infrastructure.Authentication;
using be.Infrastructure.Authorization;
using be.Application.Features.Accounts;
using be.Infrastructure.Accounts;
using be.Application.Features.Departments;
using be.Infrastructure.Departments;
using be.Application.Features.SupportTeams;
using be.Infrastructure.SupportTeams;
using be.Application.Features.Categories;
using be.Infrastructure.Categories;
using be.Application.Features.Tickets;
using be.Infrastructure.Tickets;
using be.Infrastructure.Storage;
using be.Infrastructure.Notifications;
using be.Infrastructure.Realtime;
using be.Infrastructure.Sla;
using be.Application.Features.Reporting;
using be.Infrastructure.Reporting;
using Microsoft.OpenApi.Models;

namespace be
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var isIdentityCommand = IdentityCommands.IsCommand(args);
            var isStorageCommand = args.Length > 0 && args[0] == "initialize-storage";
            var builder = WebApplication.CreateBuilder(isIdentityCommand || isStorageCommand ? [] : args);

            // Add services to the container.

            builder.Services.AddApiFoundation(builder.Configuration);
            builder.Services.AddPersistence(builder.Configuration);
            builder.Services.AddIdentityInitialization();
            builder.Services.AddHelpdeskAuthentication();
            builder.Services.AddHelpdeskAuthorization();
            builder.Services.AddAttachmentStorage(builder.Configuration);
            builder.Services.AddNotifications(builder.Configuration);
            builder.Services.AddSla(builder.Configuration);
            builder.Services.AddScoped<IReportStore, ReportStore>();
            builder.Services.AddScoped<ReportService>();
            builder.Services.AddScoped<IAccountAdministrationStore, AccountAdministrationStore>();
            builder.Services.AddScoped<AccountAdministrationService>();
            builder.Services.AddScoped<IDepartmentStore, DepartmentStore>();
            builder.Services.AddScoped<DepartmentService>();
            builder.Services.AddScoped<ISupportTeamStore, SupportTeamStore>();
            builder.Services.AddScoped<SupportTeamService>();
            builder.Services.AddScoped<ITicketCategoryStore, TicketCategoryStore>();
            builder.Services.AddScoped<TicketCategoryService>();
            builder.Services.AddScoped<TicketScope>();
            builder.Services.AddScoped<ITicketAccessChecker, TicketAccessChecker>();
            builder.Services.AddScoped<ITicketReadStore, TicketReadStore>();
            builder.Services.AddScoped<TicketReadService>();
            builder.Services.AddScoped<ITicketCreateStore, TicketCreateStore>();
            builder.Services.AddScoped<TicketCreateService>();
            builder.Services.AddScoped<ITicketMutationStore, TicketMutationStore>();
            builder.Services.AddScoped<TicketMutationService>();
            builder.Services.AddScoped<ITicketDiscussionStore, TicketDiscussionStore>();
            builder.Services.AddScoped<TicketDiscussionService>();
            builder.Services.AddScoped<TicketWriteGuard>();
            // Framework diagnostics include raw exceptions; our handler emits safe trace-based logs.
            builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
            builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerHandler", LogLevel.None);
            // Browser SignalR transports use access_token in the URL; request-start logs must not expose it.
            builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
            builder.Services.AddHealthChecks();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
                    Description = "Dán accessToken nhận từ POST /api/v1/auth/login."
                });
                options.OperationFilter<AuthenticationOperationFilter>();
                options.OperationFilter<FileResponseOperationFilter>();
            });

            var app = builder.Build();

            if (isStorageCommand)
            {
                await using (app)
                    return await StorageCommands.InitializeAsync(args, app.Services);
            }

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
            app.UseAuthentication();
            app.UseAuthorization();


            app.MapControllers();
            app.MapHub<NotificationsHub>(NotificationsHub.Path, options => options.CloseOnAuthenticationExpiration = true);
            app.MapHealthChecks("/health/live");

            await app.RunAsync();
            return 0;
        }
    }
}
