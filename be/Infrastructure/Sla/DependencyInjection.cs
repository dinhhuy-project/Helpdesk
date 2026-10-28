using be.Application.Features.Sla;
using be.Configuration;
using Microsoft.Extensions.Options;

namespace be.Infrastructure.Sla;

public static class DependencyInjection
{
    public static IServiceCollection AddSla(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SlaOptions>().Bind(configuration.GetSection("Sla")).ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<ISlaStore, SlaStore>();
        services.AddScoped<SlaService>();
        services.AddScoped<TicketSlaTracker>();
        services.AddScoped<SlaOverdueProcessor>();
        services.AddHostedService<SlaWorker>();
        return services;
    }
}

public sealed class SlaWorker(IServiceScopeFactory scopes, IOptions<SlaOptions> options, ILogger<SlaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SlaOverdueProcessor>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning("SLA processing failed ({ExceptionType}); pending breaches will be retried.", error.GetType().Name); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) return; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
