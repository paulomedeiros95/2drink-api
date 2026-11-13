using TwoDrink.Infrastructure.Storage;

namespace TwoDrink.Api.Tests.Infrastructure;

/// <summary>Delegates to the real (Azurite) storage; tests can make the server-side copy fail like an outage.</summary>
public sealed class FaultInjectingPhotoStorage(BlobPhotoStorage inner) : IPhotoStorage
{
    public volatile bool FailCopy;

    public Task EnsureReadyAsync(CancellationToken ct) => inner.EnsureReadyAsync(ct);

    public Uri CreateUploadUrl(string blobName, TimeSpan ttl) => inner.CreateUploadUrl(blobName, ttl);

    public Task<PhotoInfo?> GetInfoAsync(string blobName, CancellationToken ct) => inner.GetInfoAsync(blobName, ct);

    public Task<PhotoInfo?> CopyAsync(string sourceBlobName, string destinationBlobName, CancellationToken ct) =>
        FailCopy
            ? throw new PhotoStorageUnavailableException(new HttpRequestException("injected storage outage"))
            : inner.CopyAsync(sourceBlobName, destinationBlobName, ct);

    public Task DeleteAsync(string blobName, CancellationToken ct) => inner.DeleteAsync(blobName, ct);
}
