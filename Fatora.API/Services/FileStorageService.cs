using Fatora.BL.Exceptions;

namespace Fatora.API.Services;

public class FileStorageService(IWebHostEnvironment webHostEnvironment) : IFileStorageService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public async Task<string> SaveImageAsync(IFormFile file, string subFolder, string? previousRelativeUrl = null)
    {
        if (file is null || file.Length == 0)
        {
            throw new BadRequestException("No file was uploaded.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            throw new BadRequestException("Image must be 5 MB or smaller.");
        }

        var extension = Path.GetExtension(file.FileName);

        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new BadRequestException("Only jpg, jpeg, png, webp, and gif images are allowed.");
        }

        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("Uploaded file is not a valid image.");
        }

        var webRootPath = webHostEnvironment.WebRootPath ?? Path.Combine(webHostEnvironment.ContentRootPath, "wwwroot");
        var folderPath = Path.Combine(webRootPath, "uploads", subFolder);
        Directory.CreateDirectory(folderPath);

        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(folderPath, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        DeletePreviousFile(webRootPath, previousRelativeUrl);

        return $"/uploads/{subFolder}/{fileName}";
    }

    private static void DeletePreviousFile(string webRootPath, string? previousRelativeUrl)
    {
        if (string.IsNullOrWhiteSpace(previousRelativeUrl))
        {
            return;
        }

        try
        {
            var previousPath = Path.Combine(webRootPath, previousRelativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(previousPath))
            {
                File.Delete(previousPath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; failing to delete the old file should not block the new upload.
        }
    }
}
