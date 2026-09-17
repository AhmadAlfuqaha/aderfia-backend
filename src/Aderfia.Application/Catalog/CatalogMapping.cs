using Aderfia.Application.Common;
using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;
using Aderfia.Domain.Ordering;

namespace Aderfia.Application.Catalog;

/// <summary>
/// Entity → DTO projection.
/// <para>
/// Written by hand rather than with a mapping library: the shapes differ
/// enough (roles lower-cased, selections flattened into a dictionary, image
/// keys resolved to URLs) that configuration would be longer and harder to
/// follow than the code.
/// </para>
/// <para>
/// These run in memory over already-loaded graphs, so anything is allowed
/// here — no expression-tree translation constraints apply.
/// </para>
/// </summary>
public static class CatalogMapping
{
    /* ---- Enum ⇄ wire tokens ------------------------------------------------
       The client's unions are snake_case; C# enums are PascalCase. Converted
       in one place so the two can never drift. */

    public static string ToWire(this ImageRole role) => role switch
    {
        ImageRole.Primary => "primary",
        ImageRole.Hover => "hover",
        ImageRole.Lifestyle => "lifestyle",
        ImageRole.Detail => "detail",
        _ => "gallery"
    };

    public static string ToWire(this InventoryStatus status) => status switch
    {
        InventoryStatus.InStock => "in_stock",
        InventoryStatus.LowStock => "low_stock",
        InventoryStatus.MadeToOrder => "made_to_order",
        _ => "out_of_stock"
    };

    public static string ToWire(this BadgeTone tone) => tone switch
    {
        BadgeTone.Bestseller => "bestseller",
        BadgeTone.Limited => "limited",
        BadgeTone.Sale => "sale",
        BadgeTone.Handmade => "handmade",
        _ => "new"
    };

    /* ---- Images ---------------------------------------------------------- */

    public static ProductImageDto ToDto(this ProductImage image, IImageUrlResolver urls, Language lang) => new()
    {
        Id = image.Id.ToString(),
        Url = urls.Resolve(image.StorageKey),
        Alt = image.AltText.Get(lang),
        Role = image.Role.ToWire(),
        AspectRatio = image.AspectRatio,
        Position = image.Position,
        BlurDataUrl = image.BlurDataUrl
    };

    /* ---- Variants -------------------------------------------------------- */

    public static ProductVariantDto ToDto(this ProductVariant variant, Language lang) => new()
    {
        Id = variant.Id.ToString(),
        Sku = variant.Sku,
        Name = variant.Name.Get(lang),
        // Flattened from the join rows into { optionName: chosenValue }, which
        // is what the storefront's variant selector matches against.
        Selections = variant.OptionValues
            .Where(v => v.OptionValue?.Option is not null)
            .OrderBy(v => v.OptionValue.Option.Position)
            .ToDictionary(v => v.OptionValue.Option.Name.Get(lang), v => v.OptionValue.Value.Get(lang)),
        Price = MoneyDto.From(variant.Price),
        CompareAtPrice = MoneyDto.FromNullable(variant.CompareAtPrice),
        Inventory = new InventoryDto
        {
            Status = variant.Inventory.Status.ToWire(),
            Quantity = variant.Inventory.Quantity,
            LeadTime = variant.Inventory.LeadTime.GetOrNull(lang)
        },
        Dimensions = variant.Dimensions is null ? null : DimensionsDto.From(variant.Dimensions)
    };

    /* ---- Products -------------------------------------------------------- */

    public static ProductSummaryDto ToSummaryDto(this Product product, IImageUrlResolver urls, Language lang)
    {
        var (lowest, highest) = product.PriceBounds();

        return new ProductSummaryDto
        {
            Id = product.Id.ToString(),
            Slug = product.Slug,
            Name = product.Name.Get(lang),
            Tagline = product.Tagline.Get(lang),
            CategoryId = product.CategoryId.ToString(),
            CategorySlug = product.Category?.Slug ?? string.Empty,
            CategoryName = product.Category?.Name.Get(lang) ?? string.Empty,
            Price = MoneyDto.From(lowest),
            MaxPrice = MoneyDto.From(highest),
            CompareAtPrice = MoneyDto.FromNullable(
                product.Variants.OrderBy(v => v.Price.Amount).FirstOrDefault()?.CompareAtPrice),
            Images = product.Images
                .OrderBy(i => i.Position)
                .Select(i => i.ToDto(urls, lang))
                .ToList(),
            Badges = product.Badges
                .OrderBy(b => b.Position)
                .Select(b => new ProductBadgeDto { Label = b.Label.Get(lang), Tone = b.Tone.ToWire() })
                .ToList(),
            Featured = product.IsFeatured,
            InStock = product.HasAvailableVariant(),
            Rating = product.RatingAverage is { } average
                ? new RatingDto(average, product.RatingCount)
                : null,
            CreatedAt = product.CreatedAt
        };
    }

    public static ProductDto ToDto(this Product product, IImageUrlResolver urls, Language lang)
    {
        var summary = product.ToSummaryDto(urls, lang);

        return new ProductDto
        {
            // ---- inherited ----
            Id = summary.Id,
            Slug = summary.Slug,
            Name = summary.Name,
            Tagline = summary.Tagline,
            CategoryId = summary.CategoryId,
            CategorySlug = summary.CategorySlug,
            CategoryName = summary.CategoryName,
            Price = summary.Price,
            MaxPrice = summary.MaxPrice,
            CompareAtPrice = summary.CompareAtPrice,
            Images = summary.Images,
            Badges = summary.Badges,
            Featured = summary.Featured,
            InStock = summary.InStock,
            Rating = summary.Rating,
            CreatedAt = summary.CreatedAt,

            // ---- detail ----
            Description = product.Description.Get(lang),
            Story = product.Story.GetOrNull(lang),
            CollectionIds = product.Collections.Select(c => c.Id.ToString()).ToList(),
            Options = product.Options
                .OrderBy(o => o.Position)
                .Select(o => new ProductOptionDto
                {
                    Id = o.Id.ToString(),
                    Name = o.Name.Get(lang),
                    Position = o.Position,
                    Values = o.Values
                        .OrderBy(v => v.Position)
                        .Select(v => new ProductOptionValueDto
                        {
                            Id = v.Id.ToString(),
                            Value = v.Value.Get(lang),
                            Swatch = v.Swatch
                        })
                        .ToList()
                })
                .ToList(),
            Variants = product.Variants
                .OrderBy(v => v.Position)
                .Select(v => v.ToDto(lang))
                .ToList(),
            // Falls back to the cheapest variant so the PDP always opens on a
            // real, purchasable combination even if no default was configured.
            DefaultVariantId =
                (product.DefaultVariantId
                 ?? product.Variants.OrderBy(v => v.Price.Amount).FirstOrDefault()?.Id
                 ?? Guid.Empty).ToString(),
            Specifications = product.Specifications
                .OrderBy(s => s.Position)
                .Select(s => new SpecificationDto { Group = s.Group.Get(lang), Label = s.Label.Get(lang), Value = s.Value.Get(lang) })
                .ToList(),
            Dimensions = DimensionsDto.From(product.Dimensions),
            Materials = product.Materials
                .OrderBy(m => m.Position)
                .Select(m => m.Name.Get(lang))
                .ToList(),
            Care = product.CareInstructions.GetOrNull(lang),
            RelatedProductIds = product.RelatedProducts
                .OrderBy(r => r.Position)
                .Select(r => r.RelatedProductId.ToString())
                .ToList()
        };
    }

    /// <summary>Cheapest and dearest variant prices, for "From x" rendering.</summary>
    public static (Money Lowest, Money Highest) PriceBounds(this Product product)
    {
        if (product.Variants.Count == 0)
        {
            var zero = Money.Zero(Money.ShopCurrency);
            return (zero, zero);
        }

        var ordered = product.Variants.OrderBy(v => v.Price.Amount).ToList();
        return (ordered[0].Price, ordered[^1].Price);
    }

    /* ---- Taxonomy -------------------------------------------------------- */

    public static CategoryDto ToDto(this Category category, IImageUrlResolver urls, Language lang, int productCount) => new()
    {
        Id = category.Id.ToString(),
        Slug = category.Slug,
        Name = category.Name.Get(lang),
        Tagline = category.Tagline.Get(lang),
        Description = category.Description.Get(lang),
        Image = category.Image?.ToDto(urls, lang),
        ParentId = category.ParentId?.ToString(),
        Position = category.Position,
        ProductCount = productCount,
        Featured = category.IsFeatured
    };

    public static CollectionDto ToDto(this Collection collection, IImageUrlResolver urls, Language lang) => new()
    {
        Id = collection.Id.ToString(),
        Slug = collection.Slug,
        Title = collection.Title.Get(lang),
        Subtitle = collection.Subtitle.Get(lang),
        Description = collection.Description.Get(lang),
        HeroImage = collection.HeroImage?.ToDto(urls, lang),
        Images = collection.Images.OrderBy(i => i.Position).Select(i => i.ToDto(urls, lang)).ToList(),
        ProductIds = collection.Products.Select(p => p.Id.ToString()).ToList(),
        Featured = collection.IsFeatured,
        Position = collection.Position
    };

    public static ShippingMethodDto ToDto(this ShippingMethod method, Language lang) => new()
    {
        Id = method.Id.ToString(),
        Name = method.Name.Get(lang),
        Description = method.Description.Get(lang),
        Price = MoneyDto.From(method.Price),
        Estimate = method.Estimate.Get(lang)
    };
}
