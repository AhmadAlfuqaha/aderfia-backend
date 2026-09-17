using Aderfia.Domain.Common;

namespace Aderfia.Application.Catalog;

/* =========================================================================
   Wire contracts.

   These mirror `frontend/src/types/catalog.ts` field for field. Serialised
   with the camelCase policy configured in Program.cs, so `LowestPrice`
   arrives as `lowestPrice`.

   They are separate from the domain entities on purpose: the storefront gets
   a flat, stable shape, and internal refactors of the entities cannot break
   a deployed client.
   ========================================================================= */

/// <summary>Money on the wire. Amount stays in minor units.</summary>
public record MoneyDto(long Amount, string Currency)
{
    public static MoneyDto From(Money money) => new(money.Amount, money.Currency);

    public static MoneyDto? FromNullable(Money? money) =>
        money is { } value ? From(value) : null;
}

public record DimensionsDto
{
    public string Unit { get; init; } = "cm";
    public decimal? Width { get; init; }
    public decimal? Height { get; init; }
    public decimal? Depth { get; init; }
    public decimal? Diameter { get; init; }
    public decimal? FrameWidth { get; init; }
    public decimal? WeightKg { get; init; }

    public static DimensionsDto From(Dimensions d) => new()
    {
        Unit = d.Unit,
        Width = d.Width,
        Height = d.Height,
        Depth = d.Depth,
        Diameter = d.Diameter,
        FrameWidth = d.FrameWidth,
        WeightKg = d.WeightKg
    };
}

public record ProductImageDto
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public required string Alt { get; init; }

    /// <summary>Lower-cased role: "primary", "hover", "gallery", "lifestyle", "detail".</summary>
    public required string Role { get; init; }

    public double AspectRatio { get; init; }
    public int Position { get; init; }
    public string? BlurDataUrl { get; init; }
}

public record ProductOptionValueDto
{
    public required string Id { get; init; }
    public required string Value { get; init; }
    public string? Swatch { get; init; }
}

public record ProductOptionDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Position { get; init; }
    public IReadOnlyList<ProductOptionValueDto> Values { get; init; } = [];
}

public record InventoryDto
{
    /// <summary>snake_case to match the client union: in_stock, low_stock,
    /// out_of_stock, made_to_order.</summary>
    public required string Status { get; init; }

    public int Quantity { get; init; }
    public string? LeadTime { get; init; }
}

public record ProductVariantDto
{
    public required string Id { get; init; }
    public required string Sku { get; init; }
    public required string Name { get; init; }

    /// <summary>Option name → chosen value, e.g. { "Finish": "Natural Oak" }.</summary>
    public IReadOnlyDictionary<string, string> Selections { get; init; } =
        new Dictionary<string, string>();

    public required MoneyDto Price { get; init; }
    public MoneyDto? CompareAtPrice { get; init; }
    public required InventoryDto Inventory { get; init; }
    public DimensionsDto? Dimensions { get; init; }
}

public record SpecificationDto
{
    public required string Group { get; init; }
    public required string Label { get; init; }
    public required string Value { get; init; }
}

public record ProductBadgeDto
{
    public required string Label { get; init; }

    /// <summary>Lower-cased tone: new, bestseller, limited, sale, handmade.</summary>
    public required string Tone { get; init; }
}

public record RatingDto(double Average, int Count);

/// <summary>Trimmed shape for grids, rails and search results.</summary>
public record ProductSummaryDto
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public required string Tagline { get; init; }

    public required string CategoryId { get; init; }
    public required string CategorySlug { get; init; }
    public required string CategoryName { get; init; }

    public required MoneyDto Price { get; init; }
    public MoneyDto? CompareAtPrice { get; init; }

    /// <summary>Highest variant price, so a card can render "From x".</summary>
    public required MoneyDto MaxPrice { get; init; }

    public IReadOnlyList<ProductImageDto> Images { get; init; } = [];
    public IReadOnlyList<ProductBadgeDto> Badges { get; init; } = [];

    public bool Featured { get; init; }
    public bool InStock { get; init; }
    public RatingDto? Rating { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Full shape for the product detail page.</summary>
public record ProductDto : ProductSummaryDto
{
    public required string Description { get; init; }
    public string? Story { get; init; }

    public IReadOnlyList<string> CollectionIds { get; init; } = [];
    public IReadOnlyList<ProductOptionDto> Options { get; init; } = [];
    public IReadOnlyList<ProductVariantDto> Variants { get; init; } = [];
    public required string DefaultVariantId { get; init; }

    public IReadOnlyList<SpecificationDto> Specifications { get; init; } = [];
    public required DimensionsDto Dimensions { get; init; }
    public IReadOnlyList<string> Materials { get; init; } = [];
    public string? Care { get; init; }

    public IReadOnlyList<string> RelatedProductIds { get; init; } = [];
}

public record CategoryDto
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public required string Tagline { get; init; }
    public required string Description { get; init; }
    public ProductImageDto? Image { get; init; }
    public string? ParentId { get; init; }
    public int Position { get; init; }
    public int ProductCount { get; init; }
    public bool Featured { get; init; }
}

public record CollectionDto
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Description { get; init; }
    public ProductImageDto? HeroImage { get; init; }
    public IReadOnlyList<ProductImageDto> Images { get; init; } = [];
    public IReadOnlyList<string> ProductIds { get; init; } = [];
    public bool Featured { get; init; }
    public int Position { get; init; }
}

/* ---- Facets --------------------------------------------------------------
   Returned so the shop's filter panel is driven by data. A new category or
   material appears in the sidebar without any frontend change. */

public record FacetOptionDto(string Value, string Label, int Count);

public record FacetDto
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public IReadOnlyList<FacetOptionDto> Options { get; init; } = [];
}

public record PriceRangeDto(long Min, long Max, string Currency);

public record ProductFacetsDto
{
    public required FacetDto Categories { get; init; }
    public required FacetDto Materials { get; init; }
    public required PriceRangeDto PriceRange { get; init; }
}

public record ShippingMethodDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required MoneyDto Price { get; init; }
    public required string Estimate { get; init; }
}
