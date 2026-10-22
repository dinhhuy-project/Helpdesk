namespace be.Infrastructure.Identity;

public static class IdentityCommands
{
    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] is "seed-identity" or "bootstrap-admin";

    public static async Task<int> ExecuteAsync(string[] args, IServiceProvider services, IConfiguration configuration)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("IdentityCommands");
        if (args.Length != 1)
        {
            logger.LogError("Identity commands accept only the command name. Supply configuration through environment variables or user-secrets.");
            return 2;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var initializer = scope.ServiceProvider.GetRequiredService<IdentityInitializer>();
            if (args[0] == "seed-identity")
            {
                await initializer.SeedAsync();
                logger.LogInformation("Identity catalog initialized. Existing role permissions were preserved.");
            }
            else
            {
                var options = configuration.GetSection(BootstrapAdminOptions.SectionName).Get<BootstrapAdminOptions>()
                    ?? new BootstrapAdminOptions();
                var created = await initializer.BootstrapAdminAsync(options);
                logger.LogInformation(created ? "Initial administrator created." : "An administrator already exists. No account was changed.");
            }
            return 0;
        }
        catch (IdentityInitializationException exception)
        {
            logger.LogError("Identity initialization stopped: {Reason}", exception.Message);
            return 1;
        }
        catch (Exception exception)
        {
            // Deliberately exclude exception messages: configuration and database exceptions may contain secrets.
            logger.LogError("Identity initialization failed ({ExceptionType}). Check connection configuration, applied migrations, catalog and bootstrap inputs. No password or existing account is reset by these commands.",
                exception.GetType().Name);
            return 1;
        }
    }
}
