using Aderfia.Domain.Common;

namespace Aderfia.Application.Catalog;

/// <summary>
/// The storefront's product query, bound straight from the query string.
/// Mirrors <c>ProductQuery</c> in the frontend's types, so the mock adapter
/// and the real API accept identical inputs.
/// </summary>
public class ProductQueryParameters
{
    private const int MaxPageSize = 60;
    private const int DefaultPageSize = 9;

    public string? Search { get; set; }

    /// <summary>Repeated query keys: ?categorySlugs=mirrors&amp;categorySlugs=shelves</summary>
    public List<string> CategorySlugs { get; set; } = [];

    public List<string> CollectionSlugs { get; set; } = [];

    public List<string> Materials { get; set; } = [];

    /// <summary>Minor units, inclusive.</summary>
    public long? MinPrice { get; set; }
    public long? MaxPrice { get; set; }

    public bool InStockOnly { get; set; }
    public bool FeaturedOnly { get; set; }

    /// <summary>Accepts the client's snake_case keys ("price_asc").</summary>
    public string? Sort { get; set; }

    private int _page = 1;
    public int Page
    {
        get => _page;
        // Clamped rather than validated: a bad page number should return the
        // first page, not a 400 that breaks a shared link.
        set => _page = value < 1 ? 1 : value;
    }

    private int _pageSize = DefaultPageSize;
    public int PageSize
    {
        get => _pageSize;
        // Capped so a hand-edited URL cannot ask for the entire catalogue.
        set => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    public ProductSort SortKey => Sort?.ToLowerInvariant() switch
    {
        "newest" => ProductSort.Newest,
        "price_asc" => ProductSort.PriceAscending,
        "price_desc" => ProductSort.PriceDescending,
        "name_asc" => ProductSort.NameAscending,
        _ => ProductSort.Featured
    };

    public int Skip => (Page - 1) * PageSize;
}
