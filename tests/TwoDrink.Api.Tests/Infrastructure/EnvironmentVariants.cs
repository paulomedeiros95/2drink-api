using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TwoDrink.Api.Tests.Infrastructure;

public static class EnvironmentVariants
{
    /// <summary>
    /// The same API in another hosting environment. Critical settings are re-applied last (in-memory source) so a developer's
    /// user-secrets, which load in Development, can never redirect the tests to another database.
    /// </summary>
    public static WebApplicationFactory<Program> InEnvironment(this ApiFactory factory, string environment, bool? devAuth = null)
    {
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var overrides = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = config["ConnectionStrings:Default"],
            ["Jwt:SigningKey"] = config["Jwt:SigningKey"],
            ["Storage:ConnectionString"] = config["Storage:ConnectionString"],
            ["DevAuth:Enabled"] = devAuth switch { null => null, true => "true", false => "false" },
        };
        return factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(overrides.Where(kv => kv.Value is not null)));
        });
    }
}

public static class AuthenticatedClients
{
    /// <summary>
    /// A client signed in through the fake Google validator. Unknown routes answer 401 to anonymous callers (the fallback
    /// policy runs before routing falls through), so "the route does not exist" must be checked while signed in.
    /// </summary>
    public static async Task<HttpClient> CreateSignedInClientAsync(this WebApplicationFactory<Program> factory)
    {
        var http = factory.CreateClient();
        var login = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(http, "/auth/google", new { idToken = "valid:probe" });
        login.EnsureSuccessStatusCode();
        var tokens = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<System.Text.Json.JsonElement>(login.Content);
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return http;
    }
}
