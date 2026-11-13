using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TwoDrink.Api.Jobs;
using TwoDrink.Api.Tests.Infrastructure;
using TwoDrink.Domain.Scoring;

namespace TwoDrink.Api.Tests;

public class IntentCleanupTests(ApiFactory factory) : CheckInTestBase(factory)
{
    async Task<int> RunCleanup()
    {
        await using var scope = F.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IntentCleanup>().CleanupAsync(default);
    }

    async Task<bool> UploadExists(Guid intentId) =>
        (await F.SelfiesContainer().GetBlobClient($"checkins/{intentId}.jpg").ExistsAsync()).Value;

    async Task<bool> IntentExists(Guid intentId)
    {
        await using var db = F.NewDb();
        return await db.CheckInIntents.AnyAsync(i => i.Id == intentId);
    }

    [Fact]
    public async Task Deletes_blob_and_row_of_intents_expired_for_over_an_hour()
    {
        var stale = await CreateIntent(Me, Bar, InsideBar);
        await Upload(stale.UploadUrl);
        var confirmed = await CreateIntent(Me, Bar, InsideBar);
        await Upload(confirmed.UploadUrl);
        await ReadConfirm(await PostConfirm(Me, confirmed.IntentId, InsideBar, (DrinkType.Beer, 1)));

        // 10 min lifetime + 1h grace: not yet.
        Clock.Advance(CheckInIntentLifetime + TimeSpan.FromMinutes(59));
        await RunCleanup();
        Assert.True(await UploadExists(stale.IntentId));
        Assert.True(await IntentExists(stale.IntentId));

        Clock.Advance(TimeSpan.FromMinutes(2));
        var fresh = await NewIntentAt(Clock.GetUtcNow());
        Assert.True(await RunCleanup() >= 1);

        Assert.False(await UploadExists(stale.IntentId));
        Assert.False(await IntentExists(stale.IntentId));
        Assert.True(await IntentExists(confirmed.IntentId)); // confirmed intents are kept
        Assert.True(await IntentExists(fresh)); // not expired
    }

    [Fact]
    public async Task Storage_failure_keeps_the_row_for_the_next_run()
    {
        var stale = await CreateIntent(Me, Bar, InsideBar);
        await Upload(stale.UploadUrl);
        Clock.Advance(TimeSpan.FromHours(2));

        F.PhotoStorage.FailDelete = true;
        await RunCleanup(); // best effort: does not throw
        Assert.True(await IntentExists(stale.IntentId));

        F.PhotoStorage.FailDelete = false;
        await RunCleanup();
        Assert.False(await IntentExists(stale.IntentId));
        Assert.False(await UploadExists(stale.IntentId));
    }

    static readonly TimeSpan CheckInIntentLifetime = TwoDrink.Api.Features.CheckIns.CheckInIntentEndpoints.IntentLifetime;

    /// <summary>An unconfirmed intent row created directly (no upload), live at <paramref name="at"/>.</summary>
    async Task<Guid> NewIntentAt(DateTimeOffset at)
    {
        await using var db = F.NewDb();
        var id = Guid.NewGuid();
        db.CheckInIntents.Add(new TwoDrink.Infrastructure.Data.Entities.CheckInIntent
        {
            Id = id, UserId = Me.UserId, VenueId = Bar, BlobName = $"checkins/{id}.jpg", CreatedAt = at, ExpiresAt = at + CheckInIntentLifetime,
        });
        await db.SaveChangesAsync();
        return id;
    }
}
