using be.Application.Common.Storage;
using be.Application.Features.Tickets;
using be.Configuration;
using be.Infrastructure.Tickets;

namespace be.Infrastructure.Storage;

public static class DependencyInjection
{
    public static IServiceCollection AddAttachmentStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MinioOptions>().Bind(configuration.GetSection(MinioOptions.SectionName)).ValidateDataAnnotations()
            .Validate(MinioOptions.IsValid, "Configure a valid Minio endpoint, bucket and credentials when storage is enabled.").ValidateOnStart();
        services.AddSingleton<MinioAttachmentStorage>();
        services.AddSingleton<IAttachmentStorage>(provider => provider.GetRequiredService<MinioAttachmentStorage>());
        services.AddScoped<IAttachmentFileValidator, AttachmentFileValidator>();
        services.AddScoped<ITicketAttachmentStore, TicketAttachmentStore>();
        services.AddScoped<TicketAttachmentService>();
        services.AddScoped<AttachmentCleanup>();
        services.AddHostedService<AttachmentCleanupWorker>();
        return services;
    }
}
