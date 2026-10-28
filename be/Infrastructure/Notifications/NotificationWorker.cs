using be.Configuration;
using be.Infrastructure.Realtime;
using Microsoft.Extensions.Options;

namespace be.Infrastructure.Notifications;

public sealed class NotificationWorker(IServiceScopeFactory scopes, RealtimeConnections connections, IOptions<NotificationOptions> options,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<RealtimeConnectionValidator>().RunOnceAsync(stoppingToken);
                if (options.Value.DispatchEnabled)
                    await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                // Fail closed if session/ACL lookup fails, including an unavailable database.
                foreach (var connection in connections.Snapshot()) { connections.Remove(connection.Id); connection.Abort(); }
                logger.LogWarning("Notification worker failed ({ExceptionType}); durable notifications remain available for retry.", error.GetType().Name);
            }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
