using be.Application.Features.Notifications;
using be.Configuration;
using be.Infrastructure.Realtime;

namespace be.Infrastructure.Notifications;

public static class DependencyInjection
{
    public static IServiceCollection AddNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection("Notifications")).ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<TicketNotificationWriter>();
        services.AddScoped<NotificationStore>();
        services.AddScoped<INotificationStore>(provider => provider.GetRequiredService<NotificationStore>());
        services.AddScoped<NotificationService>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<RealtimeConnectionValidator>();
        services.AddSingleton<RealtimeConnections>();
        services.AddSingleton<IRealtimeSender, RealtimeSender>();
        services.AddSignalR(options => { options.EnableDetailedErrors = false; options.MaximumReceiveMessageSize = 4096; });
        services.AddHostedService<NotificationWorker>();
        return services;
    }
}
