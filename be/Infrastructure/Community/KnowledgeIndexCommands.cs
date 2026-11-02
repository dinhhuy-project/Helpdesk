namespace be.Infrastructure.Community;

public static class KnowledgeIndexCommands
{
    public static async Task<int> ExecuteAsync(string[] args, IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("KnowledgeIndexCommands");
        if (args.Length != 4 || args[1] is not ("check" or "rebuild") || args[2] != "--manifest" || string.IsNullOrWhiteSpace(args[3]))
        {
            logger.LogError("Usage: knowledge-index check|rebuild --manifest <dedicated-local-file.json>");
            return 2;
        }
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await using var manifest = await KnowledgeIndexManifest.OpenAsync(args[3], deadline.Token);
            await using var scope = services.CreateAsyncScope();
            var current = await scope.ServiceProvider.GetRequiredService<KnowledgeIndexMaintenance>().InspectAsync(args[1] == "rebuild", deadline.Token);
            var changes = manifest.Compare(current);
            logger.LogInformation("Knowledge index: healthy={Healthy}, documents={Count}, added={Added}, updated={Updated}, removed={Removed}, unversioned={Unversioned}, regressions={Regressions}. Native PostgreSQL synchronization uses current public snapshots.",
                current.Healthy, current.Documents.Count, changes.Added.Count, changes.Updated.Count, changes.Removed.Count, changes.UnversionedChanges.Count, changes.RevisionRegressions.Count);
            await manifest.SaveAsync(current, deadline.Token);
            logger.LogInformation("Knowledge index checkpoint saved. No articles were edited, seeded or published.");
            return 0;
        }
        catch (Exception error)
        {
            // Do not log provider exceptions, paths, connection strings or document contents.
            logger.LogError("Knowledge index operation failed ({ExceptionType}). Check database connectivity, reviewed index definition, revision discipline, manifest permissions and competing maintenance. The checkpoint was not advanced; rerun after fixing the cause.", error.GetType().Name);
            return 1;
        }
    }
}
