namespace TwoDrink.Infrastructure.Storage;

public record PhotoInfo(long SizeBytes, string ContentType);

public interface IPhotoStorage
{
    /// <summary>A write-only (create/write) SAS URL for one blob in the private <c>selfies</c> container.</summary>
    Uri CreateUploadUrl(string blobName, TimeSpan ttl);

    /// <summary>Size and content type of the blob, or null when it does not exist.</summary>
    Task<PhotoInfo?> GetInfoAsync(string blobName, CancellationToken ct);
}
