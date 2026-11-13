using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
        return await Guard(() => PropertiesOrNullAsync(_container.Value.GetBlobClient(blobName), ct), ct);
    }

    public async Task<PhotoInfo?> CopyAsync(string sourceBlobName, string destinationBlobName, CancellationToken ct)
    {
        await EnsureReadyAsync(ct);
        return await Guard(async () =>
        {
            var source = _container.Value.GetBlobClient(sourceBlobName);
            var target = _container.Value.GetBlobClient(destinationBlobName);
            if (await PropertiesOrNullAsync(source, ct) is null) return null;
            try
            {
                // Same account: the shared key that authorises the destination also authorises the source.
                var copy = await target.StartCopyFromUriAsync(source.Uri, cancellationToken: ct);
                await copy.WaitForCompletionAsync(ct);
            }
            catch (RequestFailedException e) when (e.Status == 404)
            {
                return null; // the source vanished between the check and the copy
            }
            BlobProperties props;
            try
            {
                props = (await target.GetPropertiesAsync(cancellationToken: ct)).Value;
            }
            catch (RequestFailedException e) when (e.Status == 404)
            {
                throw new PhotoStorageUnavailableException(e); // the copy reported completion but left nothing behind
            }
            if (props.CopyStatus != CopyStatus.Success)
                throw new PhotoStorageUnavailableException(new InvalidOperationException(
                    $"Copy to {destinationBlobName} ended with status {props.CopyStatus}: {props.CopyStatusDescription}"));
            return new PhotoInfo(props.ContentLength, props.ContentType);
        }, ct);
    }

    public async Task DeleteAsync(string blobName, CancellationToken ct)
    {
        await EnsureReadyAsync(ct);
        await Guard(async () => (await _container.Value.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct)).Value, ct);
    }

    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (_containerReady) return;
        await _init.WaitAsync(ct);
        try
        {
            if (_containerReady) return;
            await Guard(() => _container.Value.CreateIfNotExistsAsync(cancellationToken: ct), ct);
            _containerReady = true;
        }
        finally
        {
            _init.Release();
        }
    }

    private static async Task<PhotoInfo?> PropertiesOrNullAsync(BlobClient blob, CancellationToken ct)
    {
        try
        {
            var props = await blob.GetPropertiesAsync(cancellationToken: ct);
            return new PhotoInfo(props.Value.ContentLength, props.Value.ContentType);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    /// <summary>Turns storage/network failures into <see cref="PhotoStorageUnavailableException"/>; caller cancellation passes through.</summary>
    private static async Task<T> Guard<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        try
        {
            return await operation();
        }
        catch (Exception e) when (e is RequestFailedException or HttpRequestException or TimeoutException or AggregateException
                                   || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new PhotoStorageUnavailableException(e);
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
        catch (Exception e) when (e is PhotoStorageUnavailableException or OperationCanceledException)
        {
            log.LogWarning(e, "Blob storage unavailable at startup; the selfies container will be created on first use.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public void Dispose() => _init.Dispose();
}
