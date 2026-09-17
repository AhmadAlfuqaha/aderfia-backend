using Aderfia.Domain.Common;

namespace Aderfia.Domain.Catalog;

/// <summary>
/// A taxonomy node. Self-referencing via <see cref="ParentId"/>, so
/// "Lighting → Pendants" can be introduced later without a schema change or
/// any storefront work — the UI renders whatever tree the API returns.
/// </summary>
public class Category : Entity, ISluggable, ISoftDeletable
{
    public LocalizedText Name { get; set; } = LocalizedText.Empty;

    public required string Slug { get; set; }

    /// <summary>One-line hook used on category cards.</summary>
    public LocalizedText Tagline { get; set; } = LocalizedText.Empty;

    public LocalizedText Description { get; set; } = LocalizedText.Empty;

    /// <summary>Null for a top-level category.</summary>
    public Guid? ParentId { get; set; }

    public Category? Parent { get; set; }

    public ICollection<Category> Children { get; set; } = new List<Category>();

    /// <summary>Manual ordering within the parent. Lower sorts first.</summary>
    public int Position { get; set; }

    /// <summary>Surfaces this category on the homepage.</summary>
    public bool IsFeatured { get; set; }

    public Guid? ImageId { get; set; }

    public ProductImage? Image { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();

    // ---- SEO ----
    public LocalizedText MetaTitle { get; set; } = LocalizedText.Empty;
    public LocalizedText MetaDescription { get; set; } = LocalizedText.Empty;

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
