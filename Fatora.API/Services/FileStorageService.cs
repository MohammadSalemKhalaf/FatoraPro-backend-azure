using System.Text.RegularExpressions;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Fatora.BL.Exceptions;

namespace Fatora.API.Services;

// Render's free tier gives the container an ephemeral filesystem - anything
// written to local disk (the old wwwroot/uploads approach) is wiped on
// every redeploy and on every spin-down/spin-up cycle. Cloudinary is the
// actual persistent store; nothing here ever touches local disk.
public partial class FileStorageService : IFileStorageService
{
    private readonly Cloudinary _cloudinary;

    public FileStorageService(IConfiguration configuration)
    {
        var account = new Account(
            configuration["CLOUDINARY_CLOUD_NAME"],
            configuration["CLOUDINARY_API_KEY"],
            configuration["CLOUDINARY_API_SECRET"]);
        _cloudinary = new Cloudinary(account) { Api = { Secure = true } };
    }

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public async Task<string> SaveImageAsync(IFormFile file, Guid ownerId, string subFolder, string? previousRelativeUrl = null)
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

        await using var stream = file.OpenReadStream();
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            // Scoped per business, not just by asset type - keeps every
            // tenant's assets cleanly separated (auditing, per-tenant
            // export/backup, and bulk cleanup if a business's account is
            // ever closed all become a single well-defined folder instead
            // of a filtered search across everyone's images).
            Folder = $"fatora/{ownerId}/{subFolder}",
            PublicId = Guid.NewGuid().ToString(),
            Overwrite = false
        };

        var result = await _cloudinary.UploadAsync(uploadParams);

        if (result.Error is not null)
        {
            throw new BadRequestException($"Image upload failed: {result.Error.Message}");
        }

        await DeletePreviousFileAsync(previousRelativeUrl);

        return result.SecureUrl.ToString();
    }

    public Task DeleteImageAsync(string relativeUrl) => DeletePreviousFileAsync(relativeUrl);

    // Old (pre-Cloudinary) rows still hold a local "/uploads/..." path - those
    // files no longer exist anywhere and never will again, so there is
    // nothing to delete for them; only a real Cloudinary URL is ever acted
    // on here.
    private async Task DeletePreviousFileAsync(string? previousUrl)
    {
        if (string.IsNullOrWhiteSpace(previousUrl))
        {
            return;
        }

        var publicId = ExtractPublicId(previousUrl);

        if (publicId is null)
        {
            return;
        }

        try
        {
            await _cloudinary.DestroyAsync(new DeletionParams(publicId));
        }
        catch (Exception)
        {
            // Best-effort cleanup; failing to delete the old asset should not block the new upload.
        }
    }

    [GeneratedRegex(@"/upload/(?:v\d+/)?(?<publicId>.+)\.[a-zA-Z0-9]+$")]
    private static partial Regex CloudinaryPublicIdPattern();

    private static string? ExtractPublicId(string url)
    {
        var match = CloudinaryPublicIdPattern().Match(url);
        return match.Success ? match.Groups["publicId"].Value : null;
    }
}
