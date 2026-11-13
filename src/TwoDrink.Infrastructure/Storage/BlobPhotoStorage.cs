using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace TwoDrink.Infrastructure.Storage;

/// <summary>Selfie storage on Azure Blob Storage (private container <c>selfies</c>; uploads go straight from the app via SAS).</summary>
public sealed class BlobPhotoStorage(IConfiguration config, TimeProvider time) : IPhotoStorage, IHostedService
{
    public const string ContainerName = "selfies";

    private readonly Lazy<BlobContainerClient> _container = new(() =>
    {
        var cs = config["Storage:ConnectionString"]
                 ?? throw new InvalidOperationException("Storage:ConnectionString is not configured.");
        return new BlobContainerClient(cs, ContainerName);
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

    /// <summary>Creates the private container when missing (production provisioning creates it too).</summary>
    public async Task StartAsync(CancellationToken ct) => await _container.Value.CreateIfNotExistsAsync(cancellationToken: ct);

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
