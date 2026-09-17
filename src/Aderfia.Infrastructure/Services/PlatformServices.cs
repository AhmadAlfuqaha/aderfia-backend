using Aderfia.Application.Common;

namespace Aderfia.Infrastructure.Services;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Turns a stored image key into a public URL.
/// <para>
/// The whole point of the indirection: the database holds
/// <c>products/halo-round-mirror/1.jpg</c>, and moving from
/// <c>/media/…</c> to <c>https://cdn.aderfia.com/…</c> is a config change,
/// not a data migration.
/// </para>
/// </summary>
public sealed class ImageUrlResolver(string baseUrl) : IImageUrlResolver
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');

    public string Resolve(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return string.Empty;

        // Already absolute (an external asset, or a data URI) — leave it alone.
        if (storageKey.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || storageKey.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || storageKey.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return storageKey;
        }

        return $"{_baseUrl}/{storageKey.TrimStart('/')}";
    }
}
