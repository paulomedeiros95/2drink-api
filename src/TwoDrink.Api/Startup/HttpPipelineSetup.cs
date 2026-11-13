using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using TwoDrink.Api.Features.Auth;

namespace TwoDrink.Api.Startup;

public static class HttpPipelineSetup
{
    public const int DefaultPermitLimit = 120;

    /// <summary>
    /// Forwarded headers (section <c>ForwardedHeaders</c>): off unless <c>Enabled=true</c>. Only proxies listed in
    /// <c>KnownProxies</c> (IP addresses) or <c>KnownNetworks</c> (CIDR) are trusted, besides loopback. Configured lazily so
    /// tests can override it.
    /// </summary>
    public static IServiceCollection AddForwardedHeadersFromConfig(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((o, config) =>
        {
            var section = config.GetSection("ForwardedHeaders");
            if (!section.GetValue("Enabled", false))
            {
                o.ForwardedHeaders = ForwardedHeaders.None;
                return;
            }
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var ip in section.GetSection("KnownProxies").Get<string[]>() ?? [])
                if (!string.IsNullOrWhiteSpace(ip)) o.KnownProxies.Add(IPAddress.Parse(ip.Trim()));
            foreach (var cidr in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
                if (!string.IsNullOrWhiteSpace(cidr)) o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr.Trim()));
        });
        return services;
    }

    /// <summary>
    /// Global fixed window per user (<c>sub</c>), else per client IP: <c>RateLimiting:PermitLimit</c> (default 120) per minute.
    /// Endpoints marked <c>DisableRateLimiting</c> (/health) are exempt.
    /// </summary>
    public static IServiceCollection AddRateLimitingFromConfig(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, config) =>
        {
            var permitLimit = config.GetValue("RateLimiting:PermitLimit", DefaultPermitLimit);
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var user = ctx.User.FindFirstValue(AuthSetup.SubjectClaim);
                var key = user ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1),
                });
            });
        });
        return services;
    }
}
