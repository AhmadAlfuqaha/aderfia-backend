using System.ComponentModel.DataAnnotations;
using Aderfia.Application.Common;

namespace Aderfia.Application.Admin;

/* =========================================================================
   Admin contracts.

   Two things separate these from the storefront's DTOs.

   Text: every editable string is a LocalizedTextDto carrying BOTH languages.
   The storefront receives one resolved string and has no idea a second
   language exists; the editor needs both side by side.

   Money: amounts are in MINOR UNITS (3400 = $34.00), matching the rest of
   the system. The admin UI converts at the input, so a decimal point exists
   in exactly one place.
   ========================================================================= */

public record DimensionsInput
{
    public string Unit { get; init; } = "cm";
    public decimal? Width { get; init; }
    public decimal? Height { get; init; }
    public decimal? Depth { get; init; }
    public decimal? Diameter { get; init; }
    public decimal? FrameWidth { get; init; }
    public decimal? WeightKg { get; init; }
}

public record OptionValueInput
{
    public required LocalizedTextDto Value { get; init; }

    /// <summary>Hex colour; renders the value as a swatch instead of a pill.</summary>
    [MaxLength(9)]
    public string? Swatch { get; init; }
}

public record OptionInput
{
    public required LocalizedTextDto Name { get; init; }
    public int Position { get; init; }
    public List<OptionValueInput> Values { get; init; } = [];
}

public record VariantInput
{
    /// <summary>Null creates a new variant; set updates the existing one.</summary>
    public Guid? Id { get; init; }

    [Required, MaxLength(64)]
    public required string Sku { get; init; }

    /// <summary>Left blank, the label is built from the chosen option values.</summary>
    public LocalizedTextDto? Name { get; init; }

    /// <summary>
    /// Option name → chosen value, both keyed by their ENGLISH text.
    /// <para>
    /// English is the stable identifier here. Keying on the displayed language
    /// would mean a variant's selections stopped matching the moment someone
    /// edited an Arabic label.
    /// </para>
    /// </summary>
    public Dictionary<string, string> Selections { get; init; } = [];

    [Range(0, long.MaxValue)]
    public long PriceAmount { get; init; }

    /// <summary>Was-price for a sale. Null clears it.</summary>
    [Range(0, long.MaxValue)]
    public long? CompareAtAmount { get; init; }

    [Range(0, int.MaxValue)]
    public int Quantity { get; init; }

    [Range(0, int.MaxValue)]
    public int LowStockThreshold { get; init; } = 3;

    public bool IsMadeToOrder { get; init; }
    public bool AllowBackorder { get; init; }

    public LocalizedTextDto? LeadTime { get; init; }

    /// <summary>Set when this variant is physically a different size.</summary>
    public DimensionsInput? Dimensions { get; init; }

    public int Position { get; init; }
}

public record SpecificationInput
{
    public required LocalizedTextDto Group { get; init; }
    public required LocalizedTextDto Label { get; init; }
    public required LocalizedTextDto Value { get; init; }
}

public record BadgeInput
{
    public required LocalizedTextDto Label { get; init; }

    /// <summary>new | bestseller | limited | sale | handmade</summary>
    [Required] public required string Tone { get; init; }
}

public record ProductWriteRequest
{
    public required LocalizedTextDto Name { get; init; }

    /// <summary>
    /// Left blank, generated from the ENGLISH name and de-duplicated. Slugs
    /// stay Latin: an Arabic URL segment survives copy-paste badly and turns
    /// into percent-encoded noise when shared.
    /// </summary>
    [MaxLength(200)]
    public string? Slug { get; init; }

    public LocalizedTextDto Tagline { get; init; } = new();
    public LocalizedTextDto Description { get; init; } = new();
    public LocalizedTextDto Story { get; init; } = new();
    public LocalizedTextDto CareInstructions { get; init; } = new();

    [Required] public required Guid CategoryId { get; init; }

    public List<Guid> CollectionIds { get; init; } = [];

    /// <summary>Ordered. The first entry is the primary material and the only
    /// one that becomes a filter facet.</summary>
    public List<LocalizedTextDto> Materials { get; init; } = [];

    public DimensionsInput Dimensions { get; init; } = new();

    public List<OptionInput> Options { get; init; } = [];
    public List<VariantInput> Variants { get; init; } = [];

    /// <summary>SKU of the variant to select when the page opens.</summary>
    public string? DefaultVariantSku { get; init; }

    public List<SpecificationInput> Specifications { get; init; } = [];
    public List<BadgeInput> Badges { get; init; } = [];

    public bool IsFeatured { get; init; }
    public bool IsPublished { get; init; } = true;

    public LocalizedTextDto MetaTitle { get; init; } = new();
    public LocalizedTextDto MetaDescription { get; init; } = new();
}

/// <summary>Targeted edits, so changing a price is not a whole-product PUT.</summary>
public record VariantPriceUpdate
{
    [Range(0, long.MaxValue)] public long PriceAmount { get; init; }
    [Range(0, long.MaxValue)] public long? CompareAtAmount { get; init; }
}

public record VariantStockUpdate
{
    [Range(0, int.MaxValue)] public int Quantity { get; init; }
    public bool? IsMadeToOrder { get; init; }
    public LocalizedTextDto? LeadTime { get; init; }
}

public record PublishUpdate
{
    public bool IsPublished { get; init; }
}

/* ---- Taxonomy ------------------------------------------------------------ */

public record CategoryWriteRequest
{
    public required LocalizedTextDto Name { get; init; }
    [MaxLength(160)] public string? Slug { get; init; }
    public LocalizedTextDto Tagline { get; init; } = new();
    public LocalizedTextDto Description { get; init; } = new();
    public Guid? ParentId { get; init; }
    public int Position { get; init; }
    public bool IsFeatured { get; init; } = true;
}

public record CollectionWriteRequest
{
    public required LocalizedTextDto Title { get; init; }
    [MaxLength(160)] public string? Slug { get; init; }
    public LocalizedTextDto Subtitle { get; init; } = new();
    public LocalizedTextDto Description { get; init; } = new();
    public int Position { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsPublished { get; init; } = true;
    public DateTimeOffset? StartsAt { get; init; }
    public DateTimeOffset? EndsAt { get; init; }
    public List<Guid> ProductIds { get; init; } = [];
}

/* ---- Media --------------------------------------------------------------- */

public record ImageAttachRequest
{
    /// <summary>primary | hover | gallery | detail | lifestyle</summary>
    [Required] public required string Role { get; init; }

    public LocalizedTextDto AltText { get; init; } = new();

    public int Position { get; init; }
}

public record ImageUpdateRequest
{
    public string? Role { get; init; }
    public LocalizedTextDto? AltText { get; init; }
    public int? Position { get; init; }
}

public record AdminImageDto
{
    public required string Id { get; init; }
    public required string StorageKey { get; init; }
    public required string Url { get; init; }
    public required string Role { get; init; }
    public required LocalizedTextDto AltText { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Position { get; init; }
}

/* ---- Admin read shapes ----------------------------------------------------
   Mirror the write requests so the editor round-trips: load, change one
   field, save. Anything the write side accepts, the read side returns. */

public record AdminOptionValueDto
{
    public required string Id { get; init; }
    public required LocalizedTextDto Value { get; init; }
    public string? Swatch { get; init; }
}

public record AdminOptionDto
{
    public required string Id { get; init; }
    public required LocalizedTextDto Name { get; init; }
    public int Position { get; init; }
    public IReadOnlyList<AdminOptionValueDto> Values { get; init; } = [];
}

public record AdminVariantDto
{
    public required string Id { get; init; }
    public required string Sku { get; init; }
    public required LocalizedTextDto Name { get; init; }
    public IReadOnlyDictionary<string, string> Selections { get; init; } = new Dictionary<string, string>();
    public long PriceAmount { get; init; }
    public long? CompareAtAmount { get; init; }
    public int Quantity { get; init; }
    public int LowStockThreshold { get; init; }
    public bool IsMadeToOrder { get; init; }
    public bool AllowBackorder { get; init; }
    public required LocalizedTextDto LeadTime { get; init; }
    public DimensionsInput? Dimensions { get; init; }
    public int Position { get; init; }
    public required string InventoryStatus { get; init; }
}

public record AdminSpecificationDto
{
    public required LocalizedTextDto Group { get; init; }
    public required LocalizedTextDto Label { get; init; }
    public required LocalizedTextDto Value { get; init; }
}

public record AdminBadgeDto
{
    public required LocalizedTextDto Label { get; init; }
    public required string Tone { get; init; }
}

public record AdminProductDto
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required LocalizedTextDto Name { get; init; }
    public required LocalizedTextDto Tagline { get; init; }
    public required LocalizedTextDto Description { get; init; }
    public required LocalizedTextDto Story { get; init; }
    public required LocalizedTextDto CareInstructions { get; init; }

    public required string CategoryId { get; init; }
    public IReadOnlyList<string> CollectionIds { get; init; } = [];

    public IReadOnlyList<LocalizedTextDto> Materials { get; init; } = [];
    public required DimensionsInput Dimensions { get; init; }

    public IReadOnlyList<AdminOptionDto> Options { get; init; } = [];
    public IReadOnlyList<AdminVariantDto> Variants { get; init; } = [];
    public string? DefaultVariantSku { get; init; }

    public IReadOnlyList<AdminSpecificationDto> Specifications { get; init; } = [];
    public IReadOnlyList<AdminBadgeDto> Badges { get; init; } = [];
    public IReadOnlyList<AdminImageDto> Images { get; init; } = [];

    public bool IsFeatured { get; init; }
    public bool IsPublished { get; init; }

    public required LocalizedTextDto MetaTitle { get; init; }
    public required LocalizedTextDto MetaDescription { get; init; }
}

public record AdminCategoryDto
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required LocalizedTextDto Name { get; init; }
    public required LocalizedTextDto Tagline { get; init; }
    public required LocalizedTextDto Description { get; init; }
    public AdminImageDto? Image { get; init; }
    public string? ParentId { get; init; }
    public int Position { get; init; }
    public int ProductCount { get; init; }
    public bool IsFeatured { get; init; }
}

public record AdminCollectionDto
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required LocalizedTextDto Title { get; init; }
    public required LocalizedTextDto Subtitle { get; init; }
    public required LocalizedTextDto Description { get; init; }
    public AdminImageDto? HeroImage { get; init; }
    public IReadOnlyList<string> ProductIds { get; init; } = [];
    public int Position { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsPublished { get; init; }
    public DateTimeOffset? StartsAt { get; init; }
    public DateTimeOffset? EndsAt { get; init; }
}

/* ---- Listing ------------------------------------------------------------- */

/// <summary>Row shape for the admin product table — enough to scan and act on.</summary>
public record AdminProductRow
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required LocalizedTextDto Name { get; init; }
    public required LocalizedTextDto CategoryName { get; init; }
    public string? ThumbnailUrl { get; init; }
    public long LowestPriceAmount { get; init; }
    public required string Currency { get; init; }
    public int VariantCount { get; init; }
    public int TotalStock { get; init; }
    public bool IsPublished { get; init; }
    public bool IsFeatured { get; init; }
    public int ImageCount { get; init; }

    /// <summary>True when either language is missing on a field the shop
    /// shows, so gaps in translation are visible at a glance.</summary>
    public bool NeedsTranslation { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
