using be.Application.Common.Storage;
using be.Configuration;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace be.Infrastructure.Storage;

public sealed class AttachmentCleanup(HelpdeskDbContext database, IAttachmentStorage storage, TimeProvider clock, ILogger<AttachmentCleanup> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        Guid[] claimed;
        await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken))
        {
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734205001)", cancellationToken);
            var rows = await database.AttachmentUploadIntents.Where(intent => intent.ExpiresAt <= now &&
                !database.TicketAttachments.Any(file => file.Bucket == intent.Bucket && file.ObjectKey == intent.ObjectKey))
                .OrderBy(x => x.ExpiresAt).Take(10).ToListAsync(cancellationToken);
            foreach (var intent in rows) { intent.IsDeleting = true; intent.ExpiresAt = now.AddMinutes(5); }
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            claimed = rows.Select(x => x.Id).ToArray();
        }
        var removed = 0;
        foreach (var id in claimed)
        {
            var intent = await database.AttachmentUploadIntents.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
            // No external I/O in a transaction. Once claimed, finalization is permanently denied.
            try
            {
                await storage.DeleteAsync(intent.Bucket, intent.ObjectKey, cancellationToken);
                removed += await database.AttachmentUploadIntents.Where(x => x.Id == id && x.IsDeleting).ExecuteDeleteAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                logger.LogWarning("Attachment cleanup item failed ({ExceptionType}); reservation {ReservationId} will be retried.", error.GetType().Name, id);
            }
        }
        return removed;
    }
}

public sealed class AttachmentCleanupWorker(IServiceScopeFactory scopes, IOptions<MinioOptions> settings,
    ILogger<AttachmentCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Value.Enabled || !settings.Value.CleanupEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AttachmentCleanup>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning("Attachment cleanup failed ({ExceptionType}); durable reservations will be retried.", error.GetType().Name); }
            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }
}
