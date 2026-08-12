namespace Fatora.API.Services;

public interface IFileStorageService
{
    /// <summary>
    /// Uploads an image to Cloudinary under fatora/{ownerId}/{subFolder}/ and returns its full HTTPS URL.
    /// If <paramref name="previousRelativeUrl"/> is provided, the old asset is deleted (best-effort).
    /// </summary>
    Task<string> SaveImageAsync(IFormFile file, Guid ownerId, string subFolder, string? previousRelativeUrl = null);

    /// <summary>
    /// Deletes a previously saved image (best-effort - a missing/foreign asset is not an error).
    /// </summary>
    Task DeleteImageAsync(string relativeUrl);
}
