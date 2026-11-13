using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using TwoDrink.Infrastructure.Data;

namespace TwoDrink.Api.Tests.Infrastructure;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppUser = "twodrink_api";
    private const string AppPassword = "twodrink_api_test_pw";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public FakeTimeProvider Clock { get; } =
        new(new DateTimeOffset(2027, 2, 6, 20, 0, 0, TimeSpan.FromHours(-3)));

    /// <summary>Container superuser (database owner): migrations, seeding that needs privileges, tampering tests.</summary>
    public string OwnerConnectionString => _postgres.GetConnectionString();

    /// <summary>Unprivileged login member of <c>twodrink_app</c>, as the API connects in production.</summary>
    public string AppConnectionString => new NpgsqlConnectionStringBuilder(OwnerConnectionString)
    {
        Username = AppUser,
        Password = AppPassword,
    }.ConnectionString;

    /// <summary>A context on the app-user connection (default) or the owner connection.</summary>
    public AppDbContext NewDb(bool asOwner = false)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        builder.ConfigureApp(asOwner ? OwnerConnectionString : AppConnectionString);
        return new AppDbContext(builder.Options);
    }

    public AppDbContext NewOwnerDb() => NewDb(asOwner: true);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using (var owner = NewOwnerDb())
        {
            await owner.Database.MigrateAsync();
            await owner.Database.ExecuteSqlRawAsync(
                "CREATE ROLE " + AppUser + " LOGIN PASSWORD '" + AppPassword + "' IN ROLE twodrink_app");
        }
    }

    async Task IAsyncLifetime.DisposeAsync() => await _postgres.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", AppConnectionString);
        builder.UseSetting("Ledger:VerificationJobEnabled", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}
