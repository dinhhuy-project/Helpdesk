using be.Domain.Constants;
using be.Domain.Entities;
using be.Infrastructure.Identity;
using be.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Net;
using Xunit;

namespace Helpdesk.Persistence.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class IdentityInitializationTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task ConcurrentSeedingCreatesOneCatalogAndPreservesEditedGrants()
    {
        await using (var second = CreateContext())
            await Task.WhenAll(StartSeededApiAsync(), CreateInitializer(second).SeedAsync());

        await using var context = CreateContext();
        Assert.Equal(4, await context.Roles.CountAsync());
        Assert.Equal(11, await context.Permissions.CountAsync());
        var requester = await context.Roles.SingleAsync(x => x.Name == RoleNames.Requester);
        var createPermission = await context.Permissions.SingleAsync(x => x.Code == PermissionCodes.TicketCreate);
        var readAllPermission = await context.Permissions.SingleAsync(x => x.Code == PermissionCodes.TicketReadAll);
        var removed = await context.RolePermissions.SingleAsync(x => x.RoleId == requester.Id && x.PermissionId == createPermission.Id);
        context.RolePermissions.Remove(removed);
        var custom = new RolePermission { RoleId = requester.Id, PermissionId = readAllPermission.Id };
        context.RolePermissions.Add(custom);
        requester.Description = "Administrator customization";
        await context.SaveChangesAsync();
        try
        {
            // Starting two successive API hosts exercises the same path used by the Docker profile.
            for (var startup = 0; startup < 2; startup++)
                await StartSeededApiAsync();
            await using var rerun = CreateContext();
            await CreateInitializer(rerun).SeedAsync();
            Assert.False(await rerun.RolePermissions.AnyAsync(x => x.RoleId == requester.Id && x.PermissionId == createPermission.Id));
            Assert.True(await rerun.RolePermissions.AnyAsync(x => x.RoleId == requester.Id && x.PermissionId == readAllPermission.Id));
            Assert.Equal("Administrator customization", (await rerun.Roles.SingleAsync(x => x.Id == requester.Id)).Description);
        }
        finally
        {
            context.RolePermissions.Remove(custom);
            context.RolePermissions.Add(new RolePermission { RoleId = requester.Id, PermissionId = createPermission.Id });
            requester.Description = null;
            await context.SaveChangesAsync();
        }
    }

    [PostgresFact]
    public async Task BootstrapRejectsExistingUsersAndNeverReplacesTheFirstAdmin()
    {
        await using var context = CreateContext();
        await CreateInitializer(context).SeedAsync();
        var password = Guid.NewGuid().ToString("N");
        var existing = new User { Email = $"{Guid.NewGuid():N}@example.test", DisplayName = "Existing requester" };
        context.Users.Add(existing);
        await context.SaveChangesAsync();
        await using (var collisionContext = CreateContext())
        {
            await Assert.ThrowsAsync<IdentityInitializationException>(() => CreateInitializer(collisionContext).BootstrapAdminAsync(new()
            {
                Email = existing.Email.ToUpperInvariant(), DisplayName = "Must not change", Password = password
            }));
        }
        Assert.False(await context.UserRoles.AnyAsync(x => x.UserId == existing.Id));

        var email = $"{Guid.NewGuid():N}@example.test";
        var settings = new BootstrapAdminOptions { Email = email.ToUpperInvariant(), DisplayName = "Initial admin", Password = password };
        await Task.WhenAll(StartSeededApiAsync(settings), StartSeededApiAsync(settings));
        context.ChangeTracker.Clear();
        var admin = await context.Users.SingleAsync(x => x.Email == email);
        Assert.NotEqual(password, admin.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success, CreateHasher().VerifyHashedPassword(admin, admin.PasswordHash!, password));
        var originalHash = admin.PasswordHash;
        // Once an admin exists, startup must not require the original bootstrap password anymore.
        await StartSeededApiAsync(new BootstrapAdminOptions());
        await using (var rerun = CreateContext())
        {
            Assert.False(await CreateInitializer(rerun).BootstrapAdminAsync(new()
            {
                Email = $"{Guid.NewGuid():N}@example.test", DisplayName = "Replacement", Password = Guid.NewGuid().ToString("N")
            }));
        }
        context.ChangeTracker.Clear();
        Assert.Equal(originalHash, (await context.Users.SingleAsync(x => x.Id == admin.Id)).PasswordHash);
        var adminRole = await context.Roles.SingleAsync(x => x.Name == RoleNames.SystemAdmin);
        Assert.Equal(1, await context.UserRoles.CountAsync(x => x.RoleId == adminRole.Id));
    }

    private HelpdeskDbContext CreateContext() => ModelTests.CreateContext(fixture.ConnectionString);
    private async Task StartSeededApiAsync(BootstrapAdminOptions? bootstrap = null)
    {
        await using var factory = new ApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [IdentityStartupSeeder.ConfigurationKey] = "true",
                    [IdentityStartupSeeder.BootstrapConfigurationKey] = (bootstrap is not null).ToString(),
                    ["BootstrapAdmin:Email"] = bootstrap?.Email,
                    ["BootstrapAdmin:DisplayName"] = bootstrap?.DisplayName,
                    ["BootstrapAdmin:Password"] = bootstrap?.Password,
                    ["Jwt:Issuer"] = "Helpdesk.Test.Api",
                    ["Jwt:Audience"] = "Helpdesk.Test.Web",
                    ["Jwt:SigningKey"] = "test-only-key-at-least-32-bytes-long-for-validation",
                    ["ConnectionStrings:Helpdesk"] = fixture.ConnectionString
                })));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static IdentityInitializer CreateInitializer(HelpdeskDbContext context) => new(context, CreateHasher());
    private static IPasswordHasher<User> CreateHasher()
    {
        var services = new ServiceCollection();
        services.AddIdentityInitialization();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
    }
}
