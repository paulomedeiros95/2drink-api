using TwoDrink.Api.Errors;
using TwoDrink.Api.Features.Auth;
using TwoDrink.Api.Features.CheckIns;
using TwoDrink.Api.Features.Health;
using TwoDrink.Api.Features.Leagues;
using TwoDrink.Api.Features.Me;
using TwoDrink.Api.Features.Venues;
using TwoDrink.Api.Jobs;
using TwoDrink.Api.Startup;
using TwoDrink.Domain.Rules;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Ledger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RuleViolationExceptionHandler>();
builder.Services.AddAuth(builder.Configuration);

// The connection string is read lazily so tests can override it after the host is configured.
builder.Services.AddAppDbContext(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"));

builder.Services.AddSingleton(builder.Configuration.GetSection("Rules").Get<RulesOptions>() ?? new RulesOptions());
builder.Services.AddHealth();
builder.Services.AddVenues(builder.Configuration);
builder.Services.AddCheckIns();
builder.Services.AddLeagues();
builder.Services.AddScoped<LedgerWriter>();
builder.Services.AddScoped<LedgerVerifier>();
if (builder.Configuration.GetValue("Ledger:VerificationJobEnabled", true))
    builder.Services.AddHostedService<LedgerVerificationJob>();

if (builder.Configuration.GetValue("CheckIns:CloserJobEnabled", true))
    builder.Services.AddHostedService<CheckInCloserJob>();
if (builder.Configuration.GetValue("CheckIns:IntentCleanupJobEnabled", true))
    builder.Services.AddHostedService<IntentCleanupJob>();
if (builder.Configuration.GetValue("Trophies:AwarderJobEnabled", true))
    builder.Services.AddHostedService<TrophyAwarderJob>();

builder.Services.AddRequiredConfigValidation();
builder.Services.AddForwardedHeadersFromConfig();
builder.Services.AddRateLimitingFromConfig();

var app = builder.Build();

// Client IP must be resolved before the rate limiter partitions by it (deploy: ForwardedHeaders section, see CLAUDE.md).
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseRateLimiter(); // after authentication (partitions by "sub"), before authorization (limits rejected calls too)
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapProfileEndpoints();
app.MapLedgerEndpoints();
app.MapVenueEndpoints();
app.MapCheckInIntentEndpoints();
app.MapCheckInEndpoints();
app.MapLeagueEndpoints();
app.MapInviteEndpoints();

app.Run();

public partial class Program;
