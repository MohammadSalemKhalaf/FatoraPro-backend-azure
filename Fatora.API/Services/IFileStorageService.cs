namespace Fatora.API.Services;

public interface IFileStorageService
{
    /// <summary>
    /// Saves an uploaded image under wwwroot/uploads/{subFolder}/ and returns its relative URL.
    /// If <paramref name="previousRelativeUrl"/> is provided, the old file is deleted (best-effort).
    /// </summary>
    Task<string> SaveImageAsync(IFormFile file, string subFolder, string? previousRelativeUrl = null);
}
