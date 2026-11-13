using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TwoDrink.Api.Features.Health;
using TwoDrink.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();

// The connection string is read lazily so tests can override it after the host is configured.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options
        .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"))
        .UseSnakeCaseNamingConvention());

builder.Services.AddHealth();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var user = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var key = user ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
        });
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

app.MapHealthEndpoints();

app.Run();

public partial class Program;
