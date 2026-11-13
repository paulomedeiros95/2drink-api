using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TwoDrink.Infrastructure.Data;

/// <summary>Single source of truth for how <see cref="AppDbContext"/> is configured (production, tests, tooling).</summary>
public static class AppDbContextRegistration
{
    public static DbContextOptionsBuilder ConfigureApp(this DbContextOptionsBuilder options, string? connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    /// <summary>The connection string is resolved lazily so hosts/tests can override configuration after setup.</summary>
    public static IServiceCollection AddAppDbContext(this IServiceCollection services, Func<IServiceProvider, string?> connectionString) =>
        services.AddDbContext<AppDbContext>((sp, options) => options.ConfigureApp(connectionString(sp)));
}
