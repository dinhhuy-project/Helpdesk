namespace be.Infrastructure.Storage;

public static class StorageCommands
{
    public static async Task<int> InitializeAsync(string[] args, IServiceProvider services)
    {
        if (args.Length != 1) return 2;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("StorageCommands");
        try
        {
            await services.GetRequiredService<MinioAttachmentStorage>().InitializeAsync(CancellationToken.None);
            logger.LogInformation("Private attachment bucket initialized. Existing policy/versioning were not changed.");
            return 0;
        }
        catch (Exception error)
        {
            logger.LogError("Storage initialization failed ({ExceptionType}). Check enabled configuration, credentials and private bucket/versioning.", error.GetType().Name);
            return 1;
        }
    }
}
