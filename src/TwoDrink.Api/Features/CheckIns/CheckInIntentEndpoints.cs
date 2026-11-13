using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TwoDrink.Api.Features.Auth;
using TwoDrink.Api.Features.Venues;
using TwoDrink.Api.Jobs;
using TwoDrink.Domain.Presence;
using TwoDrink.Domain.Scoring;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Data.Entities;
using TwoDrink.Infrastructure.Storage;

namespace TwoDrink.Api.Features.CheckIns;

public record CheckInIntentRequest(Guid VenueId, double Lat, double Lng, double AccuracyM, bool IsMocked);

public record CheckInIntentResponse(Guid IntentId, Uri UploadUrl, DateTimeOffset ExpiresAt);

public static class CheckInIntentEndpoints
{
    public static readonly TimeSpan IntentLifetime = TimeSpan.FromMinutes(10);

    public static IServiceCollection AddCheckIns(this IServiceCollection services)
    {
        services.AddScoped<Geofence>();
        services.AddScoped<CheckInCooldown>();
        services.AddSingleton<DrinkCatalog>();
        services.AddScoped<HydrationReconciler>();
        services.AddScoped<CheckInCloser>();
        services.AddScoped<CheckInService>();
        services.AddSingleton<BlobPhotoStorage>();
        services.AddSingleton<IPhotoStorage>(sp => sp.GetRequiredService<BlobPhotoStorage>());
        services.AddHostedService(sp => sp.GetRequiredService<BlobPhotoStorage>());
        return services;
    }

    public static IEndpointRouteBuilder MapCheckInIntentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/checkins/intents", async (CheckInIntentRequest req, ClaimsPrincipal principal, AppDbContext db,
            VenueService venues, Geofence geofence, CheckInCooldown cooldown, IPhotoStorage storage, TimeProvider time,
            ILoggerFactory logs, CancellationToken ct) =>
        {
            var userId = Guid.Parse(principal.FindFirstValue(AuthSetup.SubjectClaim)!);

            var venue = await db.Venues.SingleOrDefaultAsync(v => v.Id == req.VenueId, ct);
            if (venue is null)
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Venue not found",
                    extensions: new Dictionary<string, object?> { ["code"] = "venue_not_found" });

            venue = await venues.EnsureFreshAsync(venue, ct);

            var now = time.GetUtcNow();
            geofence.EnsureInside(new DeviceFix(new GeoPoint(req.Lat, req.Lng), req.AccuracyM, req.IsMocked),
                new GeoPoint(venue.Lat, venue.Lng));

            var history = db.CheckIns.AsNoTracking().Where(c => c.UserId == userId);
            DateTimeOffset? lastAnywhere = await history.MaxAsync(c => (DateTimeOffset?)c.CreatedAt, ct);
            DateTimeOffset? lastHere = await history.Where(c => c.VenueId == venue.Id)
                .MaxAsync(c => (DateTimeOffset?)c.CreatedAt, ct);
            cooldown.Ensure(now, lastHere, lastAnywhere);

            Uri uploadUrl;
            var id = Guid.NewGuid();
            var blobName = $"checkins/{id}.jpg";
            try
            {
                await storage.EnsureReadyAsync(ct);
                uploadUrl = storage.CreateUploadUrl(blobName, IntentLifetime);
            }
            catch (PhotoStorageUnavailableException e)
            {
                logs.CreateLogger("CheckIns").LogWarning(e, "Photo storage unavailable while creating a check-in intent");
                return CheckInEndpoints.StorageUnavailable();
            }

            var intent = new CheckInIntent
            {
                Id = id, UserId = userId, VenueId = venue.Id, BlobName = blobName,
                CreatedAt = now, ExpiresAt = now + IntentLifetime,
            };
            db.CheckInIntents.Add(intent);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new CheckInIntentResponse(id, uploadUrl, intent.ExpiresAt));
        }).RequireAuthorization(AuthSetup.PlayerPolicy);

        return app;
    }
}
