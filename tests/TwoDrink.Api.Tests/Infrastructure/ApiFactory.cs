using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TwoDrink.Domain.Rules;
using TwoDrink.Infrastructure.Auth;
using TwoDrink.Infrastructure.Data;

namespace TwoDrink.Api.Tests.Infrastructure;

/// <summary>A signed-in player with a complete profile.</summary>
public record PlayerClient(HttpClient Http, Guid UserId, Guid LedgerAccountId);

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppUser = "twodrink_api";
    private const string AppPassword = "twodrink_api_test_pw";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public TestClock Clock { get; } = new();

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
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-test-signing-key-test-signing-key-0123456789");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IExternalTokenValidator>();
            services.AddSingleton<IExternalTokenValidator, FakeExternalTokenValidator>();
        });
    }

    /// <summary>
    /// Logs in via the fake validator (<c>valid:&lt;subject&gt;</c> on /auth/google), completes the profile as an adult at the
    /// current <see cref="Clock"/>, and returns a client carrying the bearer token.
    /// </summary>
    public async Task<PlayerClient> CreatePlayerClientAsync(string subject = "p1")
    {
        var http = CreateClient();
        var login = await http.PostAsJsonAsync("/auth/google", new { idToken = "valid:" + subject });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<JsonElement>();
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());

        var adultBirth = BusinessTime.Today(Clock.GetUtcNow()).AddYears(-30);
        var put = await http.PutAsJsonAsync("/me/profile", new
        {
            displayName = subject,
            birthDate = adultBirth,
            acceptedTermsVersion = "v1",
            locale = "pt-BR",
        });
        put.EnsureSuccessStatusCode();

        await using var db = NewDb();
        var user = db.Users.Single(u => u.Provider == ExternalProvider.Google && u.Subject == subject);
        return new PlayerClient(http, user.Id, user.LedgerAccountId);
    }
}
