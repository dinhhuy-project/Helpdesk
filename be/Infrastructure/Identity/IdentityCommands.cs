namespace be.Infrastructure.Identity;

public static class IdentityCommands
{
    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] is "seed-identity" or "bootstrap-admin" or "grant-account-administration" or "grant-department-administration" or "grant-team-leadership" or "grant-category-administration" or "grant-ticket-workflow" or "grant-community-administration" or "seed-community-demo";

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
            if (args[0] == "seed-community-demo")
            {
                if (!configuration.GetValue<bool>("CommunityDemoSeed:Enabled"))
                    throw new IdentityInitializationException("Set CommunityDemoSeed:Enabled=true to explicitly publish the six curated guides and two clearly marked demo incidents.");
                var created = await scope.ServiceProvider.GetRequiredService<be.Infrastructure.Seeding.CommunityDemoInitializer>().SeedAsync();
                logger.LogInformation(created ? "Public community examples created (6 articles, 2 demo incidents). No existing Ticket content was published." : "Community examples already exist; no changes.");
            }
            else if (args[0] == "seed-identity")
            {
                await initializer.SeedAsync();
                logger.LogInformation("Identity catalog initialized. Existing role permissions were preserved.");
            }
            else if (args[0] == "grant-community-administration")
            {
                await initializer.GrantCommunityAdministrationAsync();
                logger.LogInformation("Community draft/publish permissions enabled for SystemAdmin. Other grants preserved.");
            }
            else if (args[0] == "grant-account-administration")
            {
                await initializer.GrantAccountAdministrationAsync();
                logger.LogInformation("Account administration grants enabled for SystemAdmin. Other grants were preserved.");
            }
            else if (args[0] == "grant-department-administration")
            {
                await initializer.GrantDepartmentAdministrationAsync();
                logger.LogInformation("Department administration grant enabled for SystemAdmin. Other grants were preserved.");
            }
            else if (args[0] == "grant-team-leadership")
            {
                await initializer.GrantTeamLeadershipAsync();
                logger.LogInformation("Team management grant enabled for TeamLeader. Resource scope still requires active leadership in each team.");
            }
            else if (args[0] == "grant-category-administration")
            {
                await initializer.GrantCategoryAdministrationAsync();
                logger.LogInformation("Category administration grant enabled for SystemAdmin. Other grants were preserved.");
            }
            else if (args[0] == "grant-ticket-workflow")
            {
                await initializer.GrantTicketWorkflowAsync();
                logger.LogInformation("Ticket confirm/reopen/cancel grants enabled for the four built-in roles. Ticket resource scope still applies. Other grants were preserved.");
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
