using Aderfia.Domain.Common;

namespace Aderfia.Domain.Catalog;

/// <summary>
/// A catalog image.
/// <para>
/// Stores a storage KEY, not a public URL. The API turns keys into URLs at
/// serialisation time, which means moving from local disk to a CDN is a
/// configuration change rather than a data migration.
/// </para>
/// </summary>
public class ProductImage : Entity
{
    /// <summary>Null for images attached to a category or collection instead.</summary>
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>Path within the configured storage root.</summary>
    public required string StorageKey { get; set; }

    /// <summary>Descriptive alt text. Required for accessibility, not optional copy.</summary>
    public LocalizedText AltText { get; set; } = LocalizedText.Empty;

    public ImageRole Role { get; set; } = ImageRole.Gallery;

    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>
    /// Width ÷ height. Sent to the client so it can reserve the box before
    /// the file arrives and avoid layout shift.
    /// </summary>
    public double AspectRatio => Height == 0 ? 1 : (double)Width / Height;

    /// <summary>Tiny blurred preview, inlined as a data URI for progressive loading.</summary>
    public string? BlurDataUrl { get; set; }

    public int Position { get; set; }
}

/// <summary>
/// An editorial grouping that cuts across categories — "The Entrance",
/// "The Dark Grain". This is the mechanism behind the storytelling sections
/// on the homepage and the collection pages.
/// </summary>
public class Collection : Entity, ISluggable, ISoftDeletable
{
    public LocalizedText Title { get; set; } = LocalizedText.Empty;

    public required string Slug { get; set; }

    public LocalizedText Subtitle { get; set; } = LocalizedText.Empty;

    public LocalizedText Description { get; set; } = LocalizedText.Empty;

    public Guid? HeroImageId { get; set; }
    public ProductImage? HeroImage { get; set; }

    /// <summary>Supporting interior studies shown beneath the product grid.</summary>
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();

    public ICollection<Product> Products { get; set; } = new List<Product>();

    public bool IsFeatured { get; set; }

    public int Position { get; set; }

    public bool IsPublished { get; set; } = true;

    /// <summary>Optional scheduling, for seasonal drops.</summary>
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }

    public LocalizedText MetaTitle { get; set; } = LocalizedText.Empty;
    public LocalizedText MetaDescription { get; set; } = LocalizedText.Empty;

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public bool IsLive(DateTimeOffset now) =>
        IsPublished
        && !IsDeleted
        && (StartsAt is null || StartsAt <= now)
        && (EndsAt is null || EndsAt >= now);
}
