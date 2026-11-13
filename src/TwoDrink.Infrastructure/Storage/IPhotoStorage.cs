namespace TwoDrink.Infrastructure.Storage;

public record PhotoInfo(long SizeBytes, string ContentType);

/// <summary>Blob storage could not be reached or failed (network, throttling, 5xx). Maps to 503 <c>storage_unavailable</c>.</summary>
public sealed class PhotoStorageUnavailableException(Exception inner)
    : Exception("Photo storage is unavailable.", inner);

/// <summary>Selfie storage. Failures to reach the store surface as <see cref="PhotoStorageUnavailableException"/>.</summary>
public interface IPhotoStorage
{
    /// <summary>Makes sure the private container exists (once; retried on the next call after a failure).</summary>
    Task EnsureReadyAsync(CancellationToken ct);

    /// <summary>A write-only (create/write) SAS URL for one blob in the private <c>selfies</c> container.</summary>
    Uri CreateUploadUrl(string blobName, TimeSpan ttl);

    /// <summary>Size and content type of the blob, or null when it does not exist.</summary>
    Task<PhotoInfo?> GetInfoAsync(string blobName, CancellationToken ct);

    /// <summary>
    /// Server-side copy (same storage account) of <paramref name="sourceBlobName"/> to <paramref name="destinationBlobName"/>,
    /// waiting for completion. Returns the destination's size and content type, or null when the source does not exist.
    /// The destination is out of reach of the upload SAS, so the copy freezes the photo.
    /// </summary>
    Task<PhotoInfo?> CopyAsync(string sourceBlobName, string destinationBlobName, CancellationToken ct);

    /// <summary>Deletes the blob if it exists.</summary>
    Task DeleteAsync(string blobName, CancellationToken ct);
}
