using Aderfia.Application.Common;
using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Aderfia.Application.Catalog;

public interface ICatalogService
{
    /*
     * Listings return the FULL ProductDto, not the trimmed summary.
     *
     * The storefront is written against one `Product` shape everywhere, which
     * is what lets the mock and HTTP adapters be swapped with a single
     * environment variable. A card also genuinely needs variant data: the
     * price range behind "From $690", whether any variant is in stock, and
     * whether to offer "Add to bag" or "Select options".
     *
     * ProductSummaryDto is kept as the base type and is the obvious future
     * optimisation — it needs `defaultVariantId` and `variantCount` added, and
     * quick view changed to fetch the full product on open.
     */
    Task<PagedResult<ProductDto>> ListProductsAsync(ProductQueryParameters query, CancellationToken ct = default);
    Task<ProductDto> GetProductBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<ProductDto>> GetProductsByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<ProductDto>> SuggestAsync(string term, int take = 6, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct = default);
    Task<CategoryDto> GetCategoryBySlugAsync(string slug, CancellationToken ct = default);

    Task<IReadOnlyList<CollectionDto>> ListCollectionsAsync(CancellationToken ct = default);
    Task<CollectionDto> GetCollectionBySlugAsync(string slug, CancellationToken ct = default);

    Task<ProductFacetsDto> GetFacetsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ShippingMethodDto>> ListShippingMethodsAsync(CancellationToken ct = default);
}

/// <summary>
/// Read-side use cases for the storefront.
/// <para>
/// Every method composes an <see cref="IQueryable{T}"/> and lets the database
/// do the filtering, sorting and paging — the catalogue is never pulled into
/// memory to be filtered there.
/// </para>
/// </summary>
public sealed class CatalogService(
    IAderfiaDbContext db,
    IImageUrlResolver urls,
    IDateTimeProvider clock,
    ICurrentLanguage language) : ICatalogService
{
    private Language Lang => language.Value;

    /* =====================================================================
       Products
       ===================================================================== */

    public async Task<PagedResult<ProductDto>> ListProductsAsync(
        ProductQueryParameters query,
        CancellationToken ct = default)
    {
        var filtered = ApplyFilters(BaseProductQuery(), query);

        var totalItems = await filtered.CountAsync(ct);
        if (totalItems == 0) return PagedResult<ProductDto>.Empty(query.Page, query.PageSize);

        // Clamp to the last real page so a stale "?page=9" still returns rows.
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)query.PageSize));
        var page = Math.Min(query.Page, totalPages);

        /* Page over IDS first, then load the full graphs for that page only.
           Paging after the Includes would skip and take joined rows rather
           than products, which silently returns the wrong page. */
        var pageIds = await ApplySort(filtered, query.SortKey, Lang)
            .Skip((page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var products = await FullProductQuery()
            .Where(p => pageIds.Contains(p.Id))
            .ToListAsync(ct);

        // The IN() lookup loses the sort, so re-impose the order of pageIds.
        var byId = products.ToDictionary(p => p.Id);

        return new PagedResult<ProductDto>
        {
            Items = pageIds.Where(byId.ContainsKey).Select(id => byId[id].ToDto(urls, Lang)).ToList(),
            Page = page,
            PageSize = query.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<ProductDto> GetProductBySlugAsync(string slug, CancellationToken ct = default)
    {
        var product = await FullProductQuery()
            .FirstOrDefaultAsync(p => p.Slug == slug, ct)
            ?? throw NotFoundException.For("Product", slug);

        return product.ToDto(urls, Lang);
    }

    public async Task<IReadOnlyList<ProductDto>> GetProductsByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct = default)
    {
        var requested = ids.Distinct().ToList();
        if (requested.Count == 0) return [];

        var products = await FullProductQuery()
            .Where(p => requested.Contains(p.Id))
            .ToListAsync(ct);

        // Return them in the order the caller asked for — cart lines and
        // wishlists depend on their own ordering, not the database's.
        var byId = products.ToDictionary(p => p.Id);
        return requested
            .Where(byId.ContainsKey)
            .Select(id => byId[id].ToDto(urls, Lang))
            .ToList();
    }

    public async Task<IReadOnlyList<ProductDto>> SuggestAsync(
        string term,
        int take = 6,
        CancellationToken ct = default)
    {
        term = term.Trim();
        if (term.Length < 2) return [];

        // Ranked ids first, full graphs second — same reasoning as the listing.
        /* Searched across BOTH languages regardless of which is displayed.
           Someone browsing in Arabic may well type an English product name,
           and a shop that cannot find its own stock is worse than useless. */
        var arabic = Lang == Language.Ar;

        var rankedIds = await BaseProductQuery()
            .Where(p =>
                EF.Functions.Like(p.Name.Ar, $"%{term}%") ||
                EF.Functions.Like(p.Name.En, $"%{term}%") ||
                EF.Functions.Like(p.Tagline.Ar, $"%{term}%") ||
                EF.Functions.Like(p.Tagline.En, $"%{term}%") ||
                EF.Functions.Like(p.Category.Name.Ar, $"%{term}%") ||
                EF.Functions.Like(p.Category.Name.En, $"%{term}%"))
            // A name match outranks a tagline or category match.
            .OrderByDescending(p =>
                EF.Functions.Like(p.Name.Ar, $"{term}%") || EF.Functions.Like(p.Name.En, $"{term}%"))
            .ThenByDescending(p => p.IsFeatured)
            .ThenBy(p => arabic ? p.Name.Ar : p.Name.En)
            .Take(take)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (rankedIds.Count == 0) return [];

        var products = await FullProductQuery()
            .Where(p => rankedIds.Contains(p.Id))
            .ToListAsync(ct);

        var byId = products.ToDictionary(p => p.Id);
        return rankedIds.Where(byId.ContainsKey).Select(id => byId[id].ToDto(urls, Lang)).ToList();
    }

    /* =====================================================================
       Taxonomy
       ===================================================================== */

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var categories = await db.Categories
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Position).ThenBy(c => Lang == Language.Ar ? c.Name.Ar : c.Name.En)
            .Include(c => c.Image)
            .AsNoTracking()
            .ToListAsync(ct);

        var counts = await ProductCountsByCategoryAsync(ct);

        return categories
            .Select(c => c.ToDto(urls, Lang, counts.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<CategoryDto> GetCategoryBySlugAsync(string slug, CancellationToken ct = default)
    {
        var category = await db.Categories
            .Where(c => !c.IsDeleted)
            .Include(c => c.Image)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Slug == slug, ct)
            ?? throw NotFoundException.For("Category", slug);

        var count = await BaseProductQuery().CountAsync(p => p.CategoryId == category.Id, ct);
        return category.ToDto(urls, Lang, count);
    }

    public async Task<IReadOnlyList<CollectionDto>> ListCollectionsAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var collections = await db.Collections
            .Where(c => !c.IsDeleted && c.IsPublished)
            .Where(c => (c.StartsAt == null || c.StartsAt <= now) && (c.EndsAt == null || c.EndsAt >= now))
            .OrderBy(c => c.Position).ThenBy(c => Lang == Language.Ar ? c.Title.Ar : c.Title.En)
            .Include(c => c.HeroImage)
            .Include(c => c.Images)
            .Include(c => c.Products)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(ct);

        return collections.Select(c => c.ToDto(urls, Lang)).ToList();
    }

    public async Task<CollectionDto> GetCollectionBySlugAsync(string slug, CancellationToken ct = default)
    {
        var collection = await db.Collections
            .Where(c => !c.IsDeleted && c.IsPublished)
            .Include(c => c.HeroImage)
            .Include(c => c.Images)
            .Include(c => c.Products)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Slug == slug, ct)
            ?? throw NotFoundException.For("Collection", slug);

        if (!collection.IsLive(clock.UtcNow)) throw NotFoundException.For("Collection", slug);

        return collection.ToDto(urls, Lang);
    }

    /* =====================================================================
       Facets
       ===================================================================== */

    public async Task<ProductFacetsDto> GetFacetsAsync(CancellationToken ct = default)
    {
        var products = BaseProductQuery();

        /* Every facet carries a STABLE value and a localized label. Switching
           language must not invalidate a filter the shopper already applied,
           so the value stays the slug (or the English text) either way. */
        var arabic = Lang == Language.Ar;

        var categoryFacets = await db.Categories
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Position)
            .Select(c => new FacetOptionDto(
                c.Slug,
                arabic ? c.Name.Ar : c.Name.En,
                c.Products.Count(p => !p.IsDeleted && p.IsPublished)))
            .AsNoTracking()
            .ToListAsync(ct);

        // Only the PRIMARY material (position 0) becomes a facet. The rest of
        // the list is specification copy — finishes, fixings, glass — which
        // nobody wants to filter a furniture catalogue by.
        var materialFacets = await db.ProductMaterials
            .Where(m => m.Position == 0 && m.Product.IsPublished)
            // Grouped on both columns so the English value can stay the filter
            // key while the Arabic one is what gets shown.
            .GroupBy(m => new { m.Name.En, m.Name.Ar })
            // Ordered on the GROUPING, before projecting. Sorting after the
            // Select would ask the database to order by a member of a
            // constructed DTO, which it cannot translate.
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.En)
            .Select(g => new FacetOptionDto(
                g.Key.En,
                arabic && g.Key.Ar != "" ? g.Key.Ar : g.Key.En,
                g.Count()))
            .AsNoTracking()
            .ToListAsync(ct);

        var priceStats = await products
            .SelectMany(p => p.Variants)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Min = g.Min(v => v.Price.Amount),
                Max = g.Max(v => v.Price.Amount),
                Currency = g.Min(v => v.Price.Currency)
            })
            .FirstOrDefaultAsync(ct);

        return new ProductFacetsDto
        {
            /* The group headings are the store's own words, not catalogue
               content, so they are not stored per row — but they still have to
               follow the reader. `Key` stays a stable English identifier the
               client filters on; only `Label` changes. */
            Categories = new FacetDto
            {
                Key = "category",
                Label = new LocalizedText("الفئة", "Category").Get(Lang),
                Options = categoryFacets
            },
            Materials = new FacetDto
            {
                Key = "material",
                Label = new LocalizedText("الخامة", "Material").Get(Lang),
                Options = materialFacets
            },
            PriceRange = new PriceRangeDto(
                priceStats?.Min ?? 0,
                priceStats?.Max ?? 0,
                priceStats?.Currency ?? Money.ShopCurrency)
        };
    }

    public async Task<IReadOnlyList<ShippingMethodDto>> ListShippingMethodsAsync(CancellationToken ct = default)
    {
        var methods = await db.ShippingMethods
            .Where(m => m.IsActive)
            .OrderBy(m => m.Position).ThenBy(m => m.Price.Amount)
            .AsNoTracking()
            .ToListAsync(ct);

        return methods.Select(m => m.ToDto(Lang)).ToList();
    }

    /* =====================================================================
       Query building
       ===================================================================== */

    /// <summary>Everything a storefront visitor is allowed to see.</summary>
    private IQueryable<Product> BaseProductQuery() =>
        db.Products.Where(p => !p.IsDeleted && p.IsPublished);

    /// <summary>The full aggregate, for the product detail page.</summary>
    private IQueryable<Product> FullProductQuery() =>
        BaseProductQuery()
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Badges)
            .Include(p => p.Materials)
            .Include(p => p.Specifications)
            .Include(p => p.Collections)
            .Include(p => p.RelatedProducts)
            .Include(p => p.Options).ThenInclude(o => o.Values)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValues)
                .ThenInclude(vov => vov.OptionValue).ThenInclude(ov => ov.Option)
            .AsSplitQuery()
            .AsNoTracking();

    private static IQueryable<Product> ApplyFilters(IQueryable<Product> source, ProductQueryParameters query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(p =>
                EF.Functions.Like(p.Name.Ar, $"%{term}%") ||
                EF.Functions.Like(p.Name.En, $"%{term}%") ||
                EF.Functions.Like(p.Tagline.Ar, $"%{term}%") ||
                EF.Functions.Like(p.Tagline.En, $"%{term}%") ||
                EF.Functions.Like(p.Description.Ar, $"%{term}%") ||
                EF.Functions.Like(p.Description.En, $"%{term}%") ||
                EF.Functions.Like(p.Category.Name.Ar, $"%{term}%") ||
                EF.Functions.Like(p.Category.Name.En, $"%{term}%") ||
                p.Materials.Any(m =>
                    EF.Functions.Like(m.Name.Ar, $"%{term}%") ||
                    EF.Functions.Like(m.Name.En, $"%{term}%")));
        }

        if (query.CategorySlugs.Count > 0)
            source = source.Where(p => query.CategorySlugs.Contains(p.Category.Slug));

        if (query.CollectionSlugs.Count > 0)
            source = source.Where(p => p.Collections.Any(c => query.CollectionSlugs.Contains(c.Slug)));

        if (query.Materials.Count > 0)
            // Keyed off the English value, which is what the facet emits.
            source = source.Where(p => p.Materials.Any(m => query.Materials.Contains(m.Name.En)));

        if (query.FeaturedOnly)
            source = source.Where(p => p.IsFeatured);

        // Price filters test the CHEAPEST variant, which is the figure the
        // card shows — filtering on any-variant would return products whose
        // displayed price sits outside the chosen range.
        if (query.MinPrice is { } min)
            source = source.Where(p => p.Variants.Min(v => v.Price.Amount) >= min);

        if (query.MaxPrice is { } max)
            source = source.Where(p => p.Variants.Min(v => v.Price.Amount) <= max);

        if (query.InStockOnly)
        {
            // Status is a computed property and cannot be translated, so the
            // same rule is expressed here in terms the database understands.
            source = source.Where(p => p.Variants.Any(v =>
                v.Inventory.IsMadeToOrder || v.Inventory.AllowBackorder || v.Inventory.Quantity > 0));
        }

        return source;
    }

    private static IQueryable<Product> ApplySort(IQueryable<Product> source, ProductSort sort, Language lang) => sort switch
    {
        ProductSort.Newest => source.OrderByDescending(p => p.PublishedAt ?? p.CreatedAt),
        ProductSort.PriceAscending => source.OrderBy(p => p.Variants.Min(v => v.Price.Amount)),
        ProductSort.PriceDescending => source.OrderByDescending(p => p.Variants.Min(v => v.Price.Amount)),
        // Alphabetical has to follow whichever script the shopper is reading.
        ProductSort.NameAscending => lang == Language.Ar
            ? source.OrderBy(p => p.Name.Ar)
            : source.OrderBy(p => p.Name.En),
        _ => source
            .OrderByDescending(p => p.IsFeatured)
            .ThenByDescending(p => p.RatingAverage ?? 0)
            .ThenBy(p => lang == Language.Ar ? p.Name.Ar : p.Name.En)
    };

    private async Task<Dictionary<Guid, int>> ProductCountsByCategoryAsync(CancellationToken ct) =>
        await BaseProductQuery()
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);
}
