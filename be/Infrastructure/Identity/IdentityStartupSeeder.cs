using System.Net.Sockets;
using Npgsql;
using be.Infrastructure.Seeding;

namespace be.Infrastructure.Identity;

public sealed class IdentityStartupSeeder(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<IdentityStartupSeeder> logger) : IHostedService
{
    public const string ConfigurationKey = "Identity:SeedOnStartup";
    public const string BootstrapConfigurationKey = "Identity:BootstrapAdminOnStartup";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var seed = configuration.GetValue<bool>(ConfigurationKey);
        var bootstrapAdmin = configuration.GetValue<bool>(BootstrapConfigurationKey);
        var seedDevelopmentData = configuration.GetValue<bool>(DevelopmentDataInitializer.ConfigurationKey);
        if (!seed && !bootstrapAdmin && !seedDevelopmentData) return;
        if (!environment.IsDevelopment())
            throw new IdentityInitializationException("Startup Identity seeding, admin bootstrap and sample data are supported only in Development.");

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var initializer = scope.ServiceProvider.GetRequiredService<IdentityInitializer>();
            await initializer.SeedAsync(cancellationToken);
            logger.LogInformation("Startup Identity seed completed. Existing data and role permissions were preserved.");
            if (bootstrapAdmin)
            {
                var settings = configuration.GetSection(BootstrapAdminOptions.SectionName).Get<BootstrapAdminOptions>()
                    ?? new BootstrapAdminOptions();
                var created = await initializer.BootstrapAdminAsync(settings, cancellationToken);
                logger.LogInformation(created ? "Initial administrator created at startup." : "An administrator already exists. No account was changed.");
            }
            if (seedDevelopmentData)
            {
                var created = await scope.ServiceProvider.GetRequiredService<DevelopmentDataInitializer>().SeedAsync(cancellationToken);
                logger.LogInformation(created
                    ? "Development sample data created: 3 departments, 3 teams, 7 categories and 5 tickets."
                    : "Development sample data skipped because business data already exists. No existing data was changed.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var guidance = GetFailureGuidance(exception);
            logger.LogError("Startup Identity seed failed ({ExceptionType}). {Guidance}",
                exception.GetType().Name, guidance);
            // Avoid exposing raw connection/database exceptions through host startup diagnostics.
            throw new IdentityInitializationException(
                $"Startup Identity seed failed. {guidance}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string GetFailureGuidance(Exception exception)
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
        {
            if (cause is IdentityInitializationException)
                return "Initialization prerequisites are not met. Check applied migrations and BootstrapAdmin:Email, DisplayName, Password in user-secrets. Sample data requires an active SystemAdmin with ticket.read.all and ticket.update.status. Existing accounts and grants were preserved.";
            if (cause is PostgresException postgres)
                return postgres.SqlState switch
                {
                    "28P01" or "28000" => "PostgreSQL authentication failed. Check the configured username and password; changing .env does not update credentials in an existing volume.",
                    "3D000" => "The configured PostgreSQL database does not exist. Check the database name.",
                    "42P01" or "3F000" => "Required tables or schemas are missing. Check the target database and apply the reviewed migrations.",
                    "42501" => "The PostgreSQL account lacks permission to read or seed Identity tables.",
                    _ => "PostgreSQL rejected Identity initialization. Check database permissions, schema and existing Identity data."
                };
            if (cause is SocketException or TimeoutException)
                return "Cannot reach PostgreSQL. In the Visual Studio Docker profile use Database:HostOverride=host.docker.internal; verify PostgreSQL is running and its host port is published.";
        }
        return "Check the connection string configuration and applied migrations. Raw exception details are omitted to protect credentials.";
    }
}
