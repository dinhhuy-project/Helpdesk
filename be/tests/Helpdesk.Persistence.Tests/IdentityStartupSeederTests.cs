using be.Infrastructure.Identity;
using Npgsql;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class IdentityStartupSeederTests
{
    [Theory]
    [InlineData("socket", "Cannot reach PostgreSQL")]
    [InlineData("authentication", "PostgreSQL authentication failed")]
    [InlineData("schema", "Required tables or schemas are missing")]
    public async Task StartupFailureExplainsCauseWithoutLeakingExceptionDetails(string failure, string expected)
    {
        Exception cause = failure switch
        {
            "socket" => new NpgsqlException("secret-marker", new SocketException((int)SocketError.ConnectionRefused)),
            "authentication" => new PostgresException("secret-marker", "ERROR", "ERROR", "28P01"),
            _ => new PostgresException("secret-marker", "ERROR", "ERROR", "42P01")
        };
        var services = new ServiceCollection();
        services.AddScoped<IdentityInitializer>(_ => throw cause);
        using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<IdentityInitializationException>(() =>
            CreateSeeder(provider, "Development", true).StartAsync(CancellationToken.None));
        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("secret-marker", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task DisabledStartupDoesNotResolveDatabaseServices()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var seeder = CreateSeeder(provider, "Production", false);
        // No initializer or DbContext registered: disabled startup must not resolve either.
        await seeder.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ProductionCannotEnableStartupSeeding()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var seeder = CreateSeeder(provider, "Production", true);
        await Assert.ThrowsAsync<IdentityInitializationException>(() => seeder.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ProductionCannotEnableStartupAdminBootstrap()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [IdentityStartupSeeder.BootstrapConfigurationKey] = "true"
        }).Build();
        var seeder = new IdentityStartupSeeder(provider.GetRequiredService<IServiceScopeFactory>(), configuration,
            new TestEnvironment { EnvironmentName = "Production" }, NullLogger<IdentityStartupSeeder>.Instance);
        await Assert.ThrowsAsync<IdentityInitializationException>(() => seeder.StartAsync(CancellationToken.None));
    }

    private static IdentityStartupSeeder CreateSeeder(IServiceProvider provider, string environment, bool enabled) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [IdentityStartupSeeder.ConfigurationKey] = enabled.ToString()
            }).Build(),
            new TestEnvironment { EnvironmentName = environment },
            NullLogger<IdentityStartupSeeder>.Instance);

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
