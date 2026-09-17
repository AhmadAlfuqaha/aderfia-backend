using System.Text;
using Aderfia.Application.Common;

namespace Aderfia.Infrastructure.Services;

/// <summary>
/// Stores uploaded images on local disk under the web root.
/// <para>
/// The production swap is an S3 or Blob implementation of the same interface —
/// nothing above this layer changes, because the database holds storage keys
/// rather than URLs.
/// </para>
/// </summary>
public sealed class LocalImageStorage(string mediaRoot) : IImageStorage
{
    /// <summary>Formats the dimension reader understands and browsers render.</summary>
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif", "image/avif"
    };

    private const long MaxBytes = 12 * 1024 * 1024;

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

        /* Buffer to memory so dimensions can be read before writing, and so a
           file that turns out not to be an image never reaches disk. */
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        if (buffer.Length == 0) throw new BusinessRuleException("That file is empty.");
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

        var safeFolder = SanitiseSegment(folder);
        var extension = ExtensionFor(contentType, fileName);

        /* Drop the extension BEFORE sanitising, or the dot becomes a hyphen
           and "mirror.jpg" ends up stored as "mirror-jpg-<guid>.jpg". */
        var stem = SanitiseSegment(Path.GetFileNameWithoutExtension(fileName));
        if (string.IsNullOrEmpty(stem)) stem = "image";

        // Cap only the readable part. Slicing the COMBINED string is what
        // broke here before: a GUID is 32 characters, so "name-<guid>" only
        // reaches 48 when the stem happens to be exactly 15 long, and every
        // shorter filename threw.
        if (stem.Length > 40) stem = stem[..40];

        // The random suffix keeps re-uploads of "photo.jpg" from colliding and
        // stops anyone guessing or overwriting an existing key.
        var name = $"{stem}-{Guid.NewGuid():N}";
        var storageKey = $"{safeFolder}/{name}{extension}".Replace("//", "/");

        var absolutePath = Path.Combine(mediaRoot, storageKey.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(absolutePath)!;

        EnsureInsideRoot(absolutePath);
        Directory.CreateDirectory(directory);

        buffer.Position = 0;
        await using (var file = File.Create(absolutePath))
        {
            await buffer.CopyToAsync(file, ct);
        }

        return new StoredImage(storageKey, width, height, buffer.Length);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return Task.CompletedTask;

        var absolutePath = Path.Combine(mediaRoot, storageKey.Replace('/', Path.DirectorySeparatorChar));
        EnsureInsideRoot(absolutePath);

        // Deleting something already gone is the desired end state, not an error.
        if (File.Exists(absolutePath)) File.Delete(absolutePath);

        return Task.CompletedTask;
    }

    /* ---- Path safety -------------------------------------------------------
       Every path is resolved and checked against the media root before any
       file operation, so a crafted name can never read or write outside it. */
    private void EnsureInsideRoot(string absolutePath)
    {
        var root = Path.GetFullPath(mediaRoot);
        var target = Path.GetFullPath(absolutePath);

        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Invalid image path.");
    }

    /// <summary>
    /// Strips anything that could escape the media root or upset a filesystem:
    /// separators, traversal, and non-URL-safe characters.
    /// </summary>
    private static string SanitiseSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "misc";

        var builder = new StringBuilder(value.Length);

        /* Normalise first so accented characters decompose and their marks
           can be dropped: "café" folds to "cafe" rather than losing the
           letter entirely and becoming "caf". Scripts with no ASCII form
           fall through to "misc" below, which is still safe and unique
           thanks to the GUID suffix. */
        foreach (var raw in value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(raw)
                == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // Only ASCII survives into a storage key — anything else risks
            // encoding trouble across filesystems, URLs and CDNs.
            var c = raw <= 'z' ? raw : '-';

            if (char.IsLetterOrDigit(c) || c == '_')
            {
                builder.Append(c);
            }
            // A slash is legal *between* folder segments but never doubled or leading.
            else if (c == '/')
            {
                if (builder.Length > 0 && builder[^1] != '/') builder.Append('/');
            }
            // Everything else — spaces, dots, punctuation, non-ASCII — collapses
            // to a single hyphen, so a name can never gain a run of them.
            else if (builder.Length > 0 && builder[^1] is not ('-' or '/'))
            {
                builder.Append('-');
            }
        }

        var result = builder.ToString().Trim('/', '-');
        return result.Length == 0 ? "misc" : result;
    }

    private static string ExtensionFor(string contentType, string fileName) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "image/avif" => ".avif",
        // Unreachable given the allow-list, but keeps the switch total.
        _ => Path.GetExtension(fileName) is { Length: > 0 and < 6 } ext ? ext.ToLowerInvariant() : ".bin"
    };
}

/// <summary>Slugs for products, categories and collections.</summary>
public sealed class SlugGenerator : ISlugGenerator
{
    public string Generate(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var builder = new StringBuilder(input.Length);

        // Normalise so accented characters decompose and their marks can be
        // dropped — "Café" becomes "cafe", not "caf".
        foreach (var c in input.Normalize(NormalizationForm.FormD))
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == System.Globalization.UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }
}
