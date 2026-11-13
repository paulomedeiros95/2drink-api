using TwoDrink.Infrastructure.Data;

namespace TwoDrink.Api.Features.Health;

public static class HealthEndpoints
{
    public static IServiceCollection AddHealth(this IServiceCollection services)
    {
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
        return services;
    }

    // Healthy -> 200, Unhealthy -> 503 (default HealthCheck status mapping).
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health");
        return app;
    }
}
