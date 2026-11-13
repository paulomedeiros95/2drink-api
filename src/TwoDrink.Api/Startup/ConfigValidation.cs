using System.Text;
using Microsoft.Extensions.Options;
using TwoDrink.Infrastructure.Auth;

namespace TwoDrink.Api.Startup;

/// <summary>Marker options whose validation checks the configuration the API cannot run without.</summary>
public sealed class RequiredConfig;

/// <summary>
/// Fails host startup (<c>ValidateOnStart</c>) on missing/weak settings instead of failing on the first request:
/// <c>Jwt:SigningKey</c> (≥ 32 bytes), <c>Jwt:Issuer</c>, <c>Jwt:Audience</c>, <c>ConnectionStrings:Default</c> and,
/// outside Development, at least one <c>Auth:Google:ClientIds</c> or <c>Auth:Apple:Audiences</c> entry.
/// </summary>
public sealed class RequiredConfigValidator(
    IConfiguration config, IOptions<JwtOptions> jwt, IOptions<ExternalAuthOptions> external, IHostEnvironment env)
    : IValidateOptions<RequiredConfig>
{
    public ValidateOptionsResult Validate(string? name, RequiredConfig options)
    {
        var failures = new List<string>();
        var j = jwt.Value;
        if (Encoding.UTF8.GetByteCount(j.SigningKey ?? "") < 32)
            failures.Add("Jwt:SigningKey must be set and at least 32 bytes.");
        if (string.IsNullOrWhiteSpace(j.Issuer))
            failures.Add("Jwt:Issuer must be set.");
        if (string.IsNullOrWhiteSpace(j.Audience))
            failures.Add("Jwt:Audience must be set.");
        if (string.IsNullOrWhiteSpace(config.GetConnectionString("Default")))
            failures.Add("ConnectionStrings:Default must be set.");

        if (!env.IsDevelopment())
        {
            var e = external.Value;
            if (!e.Google.ClientIds.Any(s => !string.IsNullOrWhiteSpace(s)) && !e.Apple.Audiences.Any(s => !string.IsNullOrWhiteSpace(s)))
                failures.Add("At least one Auth:Google:ClientIds or Auth:Apple:Audiences entry must be set outside Development.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public static class ConfigValidationExtensions
{
    public static IServiceCollection AddRequiredConfigValidation(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<RequiredConfig>, RequiredConfigValidator>();
        services.AddOptions<RequiredConfig>().ValidateOnStart();
        return services;
    }
}
