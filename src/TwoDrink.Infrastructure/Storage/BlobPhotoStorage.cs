using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TwoDrink.Infrastructure.Storage;

/// <summary>Selfie storage on Azure Blob Storage (private container <c>selfies</c>; uploads go straight from the app via SAS).</summary>
public sealed class BlobPhotoStorage(IConfiguration config, TimeProvider time, ILogger<BlobPhotoStorage> log)
    : IPhotoStorage, IHostedService, IDisposable
{
    public const string ContainerName = "selfies";

    private readonly SemaphoreSlim _init = new(1, 1);
    private volatile bool _containerReady;

    private readonly Lazy<BlobContainerClient> _container = new(() =>
    {
        var cs = config["Storage:ConnectionString"]
                 ?? throw new InvalidOperationException("Storage:ConnectionString is not configured.");
        var options = new BlobClientOptions
        {
            Retry = { MaxRetries = 2, Delay = TimeSpan.FromMilliseconds(300), NetworkTimeout = TimeSpan.FromSeconds(10) },
        };
        return new BlobContainerClient(cs, ContainerName, options);
    });

    private bool AllowHttp => bool.TryParse(config["Storage:AllowHttpSas"], out var b) && b;

    public Uri CreateUploadUrl(string blobName, TimeSpan ttl)
    {
        var blob = _container.Value.GetBlobClient(blobName);
        var sas = new BlobSasBuilder(BlobSasPermissions.Create | BlobSasPermissions.Write, time.GetUtcNow().Add(ttl))
        {
            BlobContainerName = ContainerName,
            BlobName = blobName,
            Resource = "b",
            Protocol = AllowHttp ? SasProtocol.HttpsAndHttp : SasProtocol.Https,
        };
        return blob.GenerateSasUri(sas);
    }

    public async Task<PhotoInfo?> GetInfoAsync(string blobName, CancellationToken ct)
    {
        await EnsureReadyAsync(ct);
        try
        {
            var props = await _container.Value.GetBlobClient(blobName).GetPropertiesAsync(cancellationToken: ct);
            return new PhotoInfo(props.Value.ContentLength, props.Value.ContentType);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (_containerReady) return;
        await _init.WaitAsync(ct);
        try
        {
            if (_containerReady) return;
            await _container.Value.CreateIfNotExistsAsync(cancellationToken: ct);
            _containerReady = true;
        }
        finally
        {
            _init.Release();
        }
    }

    /// <summary>
    /// A missing connection string fails fast; a storage outage at boot only logs a warning and the container is
    /// ensured lazily on first use, so the rest of the API stays up.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        _ = _container.Value; // throws when Storage:ConnectionString is missing
        try
        {
            await EnsureReadyAsync(ct);
        }
        catch (Exception e) when (e is RequestFailedException or HttpRequestException or TaskCanceledException or TimeoutException or AggregateException)
        {
            log.LogWarning(e, "Blob storage unavailable at startup; the selfies container will be created on first use.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public void Dispose() => _init.Dispose();
}
