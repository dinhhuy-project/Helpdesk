using be.Domain.Entities;
using be.Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;

namespace be.Infrastructure.Identity;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInitialization(this IServiceCollection services)
    {
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IdentityInitializer>();
        services.AddScoped<DevelopmentDataInitializer>();
        services.AddHostedService<IdentityStartupSeeder>();
        return services;
    }
}
