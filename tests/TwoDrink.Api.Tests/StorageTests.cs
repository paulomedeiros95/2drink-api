using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TwoDrink.Api.Tests.Infrastructure;
using TwoDrink.Infrastructure.Storage;

namespace TwoDrink.Api.Tests;

public class UnreachableStorageFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // Nothing listens on port 1.
        builder.UseSetting("Storage:ConnectionString",
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
            "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            "BlobEndpoint=http://127.0.0.1:1/devstoreaccount1;");
    }
}

public class StorageStartupTests : IClassFixture<UnreachableStorageFactory>
{
    private readonly UnreachableStorageFactory _f;
    public StorageStartupTests(UnreachableStorageFactory f) => _f = f;

    [Fact]
    public async Task Host_starts_and_health_is_ok_when_storage_is_unreachable_at_boot()
    {
        var res = await _f.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Intent_returns_503_storage_unavailable_when_storage_is_down()
    {
        var player = await _f.CreatePlayerClientAsync("sd-" + Guid.NewGuid().ToString("N"));
        await using var db = _f.NewDb();
        var venue = new TwoDrink.Infrastructure.Data.Entities.Venue
        {
            Id = Guid.NewGuid(), PlaceId = "storage-down-" + Guid.NewGuid().ToString("N"), Name = "Bar", Address = "Rua",
            Lat = -23.5505, Lng = -46.6333, CachedAt = _f.Clock.GetUtcNow(),
        };
        db.Venues.Add(venue);
        await db.SaveChangesAsync();

        var res = await player.Http.PostAsJsonAsync("/checkins/intents",
            new { venueId = venue.Id, lat = -23.5506, lng = -46.6333, accuracyM = 10, isMocked = false });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Equal("storage_unavailable", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}

public class PhotoStorageTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public PhotoStorageTests(ApiFactory f) => _f = f;

    private IPhotoStorage Storage => _f.Services.GetRequiredService<IPhotoStorage>();

    [Fact]
    public async Task GetInfo_returns_null_for_missing_blob()
    {
        Assert.Null(await Storage.GetInfoAsync($"checkins/{Guid.NewGuid()}.jpg", default));
    }

    [Fact]
    public async Task GetInfo_returns_size_and_content_type_for_uploaded_blob()
    {
        var name = $"checkins/{Guid.NewGuid()}.jpg";
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9];
        await Storage.EnsureReadyAsync(default);
        using var http = new HttpClient();
        var req = new HttpRequestMessage(HttpMethod.Put, Storage.CreateUploadUrl(name, TimeSpan.FromMinutes(5)))
        {
            Content = new ByteArrayContent(bytes),
        };
        req.Content.Headers.ContentType = new("image/jpeg");
        req.Headers.Add("x-ms-blob-type", "BlockBlob");
        Assert.Equal(HttpStatusCode.Created, (await http.SendAsync(req)).StatusCode);

        var info = await Storage.GetInfoAsync(name, default);
        Assert.Equal(new PhotoInfo(bytes.Length, "image/jpeg"), info);
    }
}
