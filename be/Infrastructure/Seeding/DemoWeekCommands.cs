using be.Infrastructure.Identity;

namespace be.Infrastructure.Seeding;

public static class DemoWeekCommands
{
    public static async Task<int> ExecuteAsync(string[] args, IServiceProvider services, IConfiguration configuration)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DemoWeekCommands");
        if (args.Length != 1 || !configuration.GetValue<bool>("DemoWeekSeed:Enabled"))
        {
            logger.LogError("Use seed-demo-week with DemoWeekSeed:Enabled=true and DemoWeekSeed:Password supplied privately. This command is never run on startup.");
            return 2;
        }
        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<DemoWeekInitializer>()
                .SeedAsync(configuration["DemoWeekSeed:Password"] ?? "");
            logger.LogInformation("Demo week {Outcome}: {Batch}. Original week is recorded in the DemoWeekSeeded audit marker. Existing records and grants preserved.",
                result ? "created (30 accounts, 144 Tickets)" : "already exists; no changes", DemoWeekCatalog.Batch);
            return 0;
        }
        catch (IdentityInitializationException exception)
        {
            logger.LogError("Demo week stopped: {Reason}", exception.Message);
            return 1;
        }
        catch (Exception exception)
        {
            logger.LogError("Demo week failed ({ExceptionType}); transaction rolled back. Check connection, migration and reserved demo names. Credentials are never logged.", exception.GetType().Name);
            return 1;
        }
    }
}
