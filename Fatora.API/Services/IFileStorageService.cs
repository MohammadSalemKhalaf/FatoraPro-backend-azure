namespace Fatora.API.Services;

public interface IFileStorageService
{
    /// <summary>
    /// Saves an uploaded image under wwwroot/uploads/{subFolder}/ and returns its relative URL.
    /// If <paramref name="previousRelativeUrl"/> is provided, the old file is deleted (best-effort).
    /// </summary>
    Task<string> SaveImageAsync(IFormFile file, string subFolder, string? previousRelativeUrl = null);

    /// <summary>
    /// Deletes a previously saved image (best-effort - a missing file is not an error).
    /// </summary>
    Task DeleteImageAsync(string relativeUrl);
}
