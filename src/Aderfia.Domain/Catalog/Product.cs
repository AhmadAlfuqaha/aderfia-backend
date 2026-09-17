using Aderfia.Domain.Common;

namespace Aderfia.Domain.Catalog;

/// <summary>
/// The catalog's aggregate root.
/// <para>
/// A product owns its images, options, variants and specifications. Nothing
/// about it is specific to mirrors, clocks or shelves: a new product type
/// declares its own option axes and specification groups, and both the API
/// and the storefront render them generically.
/// </para>
/// </summary>
public class Product : Entity, ISluggable, ISoftDeletable
{
    public LocalizedText Name { get; set; } = LocalizedText.Empty;

    public required string Slug { get; set; }

    /// <summary>Short editorial line shown beneath the name.</summary>
    public LocalizedText Tagline { get; set; } = LocalizedText.Empty;

    public LocalizedText Description { get; set; } = LocalizedText.Empty;

    /// <summary>Longer narrative for the product page's story block.</summary>
    public LocalizedText Story { get; set; } = LocalizedText.Empty;

    // ---- Taxonomy ----
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public ICollection<Collection> Collections { get; set; } = new List<Collection>();

    // ---- Composition ----
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductOption> Options { get; set; } = new List<ProductOption>();
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductSpecification> Specifications { get; set; } = new List<ProductSpecification>();
    public ICollection<ProductBadge> Badges { get; set; } = new List<ProductBadge>();

    /// <summary>
    /// The variant selected when the page first loads. Every product has at
    /// least one variant, even when it has no options — that keeps pricing
    /// and stock in exactly one place rather than two.
    /// </summary>
    public Guid? DefaultVariantId { get; set; }
    public ProductVariant? DefaultVariant { get; set; }

    // ---- Descriptive ----
    /// <summary>
    /// Ordered; the first entry is the primary substrate and is the only one
    /// used as a filter facet. The rest are specification copy.
    /// </summary>
    public ICollection<ProductMaterial> Materials { get; set; } = new List<ProductMaterial>();

    public Dimensions Dimensions { get; set; } = new();

    public LocalizedText CareInstructions { get; set; } = LocalizedText.Empty;

    // ---- Merchandising ----
    public bool IsFeatured { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>
    /// Curated cross-sells. When empty the API falls back to same-category
    /// products, so an editor never has to fill these in for the PDP to work.
    /// </summary>
    public ICollection<ProductRelation> RelatedProducts { get; set; } = new List<ProductRelation>();

    // ---- Reviews (denormalised for listing performance) ----
    public double? RatingAverage { get; set; }
    public int RatingCount { get; set; }

    // ---- SEO ----
    public LocalizedText MetaTitle { get; set; } = LocalizedText.Empty;
    public LocalizedText MetaDescription { get; set; } = LocalizedText.Empty;

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// Lowest price across all variants — what a listing shows as "From £x".
    /// Computed rather than stored so a variant price change can never leave
    /// the card showing a stale figure.
    /// </summary>
    public Money LowestPrice() =>
        Variants.Count == 0
            ? Money.Zero(Money.ShopCurrency)
            : Variants.MinBy(v => v.Price.Amount)!.Price;

    public bool HasAvailableVariant() =>
        Variants.Any(v => v.Inventory.Status != InventoryStatus.OutOfStock);
}

/// <summary>A material entry. Ordered, so "primary" is simply position 0.</summary>
public class ProductMaterial : Entity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public LocalizedText Name { get; set; } = LocalizedText.Empty;
    public int Position { get; set; }
}

/// <summary>
/// A directed "related product" edge. A join entity rather than a
/// many-to-many so the relation can carry its own ordering.
/// </summary>
public class ProductRelation : Entity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid RelatedProductId { get; set; }
    public Product RelatedProduct { get; set; } = null!;

    public int Position { get; set; }
}

/// <summary>A short marketing label. At most one is rendered on a card.</summary>
public class ProductBadge : Entity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public LocalizedText Label { get; set; } = LocalizedText.Empty;
    public BadgeTone Tone { get; set; }
    public int Position { get; set; }
}

/// <summary>
/// A grouped key/value specification row. Flat and generic on purpose: a
/// clock declares "Movement / Type", a shelf declares "Load / Capacity", and
/// neither needs a column of its own.
/// </summary>
public class ProductSpecification : Entity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Display section, e.g. "Construction", "Mounting", "Care".</summary>
    public LocalizedText Group { get; set; } = LocalizedText.Empty;

    public LocalizedText Label { get; set; } = LocalizedText.Empty;

    public LocalizedText Value { get; set; } = LocalizedText.Empty;

    public int Position { get; set; }
}
