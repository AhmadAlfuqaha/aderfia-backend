using System.Text;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Aderfia.Application.Common;

namespace Aderfia.Infrastructure.Services;

/// <summary>
/// Stores uploaded images in Azure Blob Storage using the Container App's
/// managed identity. No storage account key or connection string is required.
/// </summary>
public sealed class AzureBlobImageStorage : IImageStorage
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif", "image/avif"
    };

    private const long MaxBytes = 12 * 1024 * 1024;

    private readonly BlobContainerClient _container;

    public AzureBlobImageStorage(Uri serviceUri, string containerName)
    {
        var service = new BlobServiceClient(serviceUri, new DefaultAzureCredential());
        _container = service.GetBlobContainerClient(containerName);
    }

    public async Task<StoredImage> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken ct = default)
    {
        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new BusinessRuleException(
                $"'{contentType}' is not an image type we accept. Use JPEG, PNG, WebP or AVIF.");
        }

        if (content.CanSeek && content.Length > MaxBytes)
        {
            throw new BusinessRuleException(
                $"That file is {content.Length / 1_048_576.0:0.#} MB. The limit is {MaxBytes / 1_048_576} MB.");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        if (buffer.Length == 0)
            throw new BusinessRuleException("That file is empty.");

        if (buffer.Length > MaxBytes)
        {
            throw new BusinessRuleException(
                $"That file is {buffer.Length / 1_048_576.0:0.#} MB. The limit is {MaxBytes / 1_048_576} MB.");
        }

        var (width, height) = ImageDimensionReader.Read(buffer);
        if (width == 0 || height == 0)
        {
            throw new BusinessRuleException(
                "That file does not look like a readable image. It may be corrupt or misnamed.");
        }

        var safeFolder = SanitisePath(folder);
        var extension = ExtensionFor(contentType, fileName);
        var stem = SanitiseSegment(Path.GetFileNameWithoutExtension(fileName));
        if (string.IsNullOrEmpty(stem)) stem = "image";
        if (stem.Length > 40) stem = stem[..40];

        var name = $"{stem}-{Guid.NewGuid():N}{extension}";
        var storageKey = string.IsNullOrEmpty(safeFolder) ? name : $"{safeFolder}/{name}";

        var blob = _container.GetBlobClient(storageKey);

        buffer.Position = 0;
        await blob.UploadAsync(
            buffer,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            ct);

        return new StoredImage(storageKey, width, height, buffer.Length);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return;

        var safeKey = storageKey.TrimStart('/');
        await _container.GetBlobClient(safeKey).DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: ct);
    }

    private static string SanitisePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        return string.Join(
            '/',
            value.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(SanitiseSegment)
                .Where(x => !string.IsNullOrEmpty(x)));
    }

    private static string SanitiseSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "misc";

        var builder = new StringBuilder(value.Length);

        foreach (var raw in value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(raw)
                == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (raw <= 'z' && (char.IsLetterOrDigit(raw) || raw == '_'))
            {
                builder.Append(raw);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string ExtensionFor(string contentType, string fileName) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "image/avif" => ".avif",
        _ => Path.GetExtension(fileName) is { Length: > 0 and < 6 } ext ? ext.ToLowerInvariant() : ".bin"
    };
}
