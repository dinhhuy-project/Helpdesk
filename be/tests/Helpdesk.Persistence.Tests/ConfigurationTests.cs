using be.Api;
using be.Configuration;
using be.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Helpdesk.Persistence.Tests;

public class ConfigurationTests
{
    [Fact]
    public void DockerHostOverridePreservesPortDatabaseAndCredentials()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5544;Database=helpdesk;Username=test-user;Password=test-only-value",
            ["Database:HostOverride"] = "host.docker.internal"
        }).Build();
        var services = new ServiceCollection();
        services.AddPersistence(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var settings = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());
        Assert.Equal("host.docker.internal", settings.Host);
        Assert.Equal(5544, settings.Port);
        Assert.Equal("helpdesk", settings.Database);
        Assert.Equal("test-user", settings.Username);
        Assert.Equal("test-only-value", settings.Password);
    }

    [Theory]
    [InlineData("Cors:AllowedOrigins:0", "*")]
    [InlineData("Cors:AllowedOrigins:0", "https://example.test/path")]
    [InlineData("AuthRateLimit:PermitLimit", "0")]
    [InlineData("Jwt:SigningKey", "short-key")]
    [InlineData("Jwt:RequireConfiguration", "true")]
    public void InvalidConfigurationIsRejected(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiFoundation(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void CanonicalConnectionOverridesLegacyAndLegacyRemainsSupported()
    {
        foreach (var canonical in new[] { true, false })
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Helpdesk"] = canonical ? "Host=canonical;Database=test;Username=test" : null,
                ["ConnectionStrings:DefaultConnection"] = "Host=legacy;Database=test;Username=test"
            }).Build();
            var services = new ServiceCollection();
            services.AddPersistence(configuration);
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
            Assert.Contains(canonical ? "canonical" : "legacy", context.Database.GetConnectionString());
        }
    }
}
