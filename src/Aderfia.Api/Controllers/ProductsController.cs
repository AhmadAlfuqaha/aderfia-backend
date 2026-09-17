using Aderfia.Application.Catalog;
using Aderfia.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Aderfia.Api.Controllers;

/// <summary>
/// The storefront's catalog endpoints.
/// <para>
/// Routes and query-string shapes match the frontend's HTTP adapter exactly,
/// so switching <c>VITE_USE_MOCK_API</c> to <c>false</c> is the only change
/// needed to run the React app against this API.
/// </para>
/// </summary>
[ApiController]
[Route("api/products")]
[Produces("application/json")]
public class ProductsController(ICatalogService catalog) : ControllerBase
{
    /// <summary>Filtered, sorted, paged listing. Powers the shop grid.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
    /* No response cache.
       -------------------------------------------------------------------
       These endpoints return catalogue data the admin can change at any
       moment, and the cache had no way to be told. Replacing a collection's
       hero image deleted the old file and wrote a new record, but a shared
       cache went on handing out the previous response for up to five
       minutes — so the storefront pointed at a file that no longer existed
       and rendered an empty frame. Stale would have been tolerable; broken
       was not.

       Nothing here is expensive enough to want caching: the whole catalogue
       is a handful of rows behind an index on the same machine. If load ever
       justifies it, it needs invalidating on write rather than a duration,
       because the failure mode is a broken image on the shop's own front
       page and the person who caused it is the one least able to explain it. */
    public async Task<ActionResult<PagedResult<ProductDto>>> List(
        [FromQuery] ProductQueryParameters query,
        CancellationToken ct)
        => Ok(await catalog.ListProductsAsync(query, ct));

    /// <summary>Facet counts for the shop's filter panel.</summary>
    [HttpGet("facets")]
    [ProducesResponseType(typeof(ProductFacetsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductFacetsDto>> Facets(CancellationToken ct)
        => Ok(await catalog.GetFacetsAsync(ct));

    /// <summary>Typeahead for the search overlay.</summary>
    [HttpGet("suggest")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> Suggest(
        [FromQuery] string q,
        CancellationToken ct)
        => Ok(await catalog.SuggestAsync(q ?? string.Empty, take: 6, ct));

    /// <summary>
    /// Batch lookup, used to resolve cart lines and wishlists in one request
    /// rather than N.
    /// </summary>
    [HttpGet("batch")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> Batch(
        [FromQuery] string ids,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ids)) return Ok(Array.Empty<ProductDto>());

        // Unparseable ids are skipped rather than rejected: one stale id in a
        // long-lived localStorage cart must not fail the whole request.
        var parsed = ids
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => Guid.TryParse(id, out var guid) ? guid : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Take(100)
            .ToList();

        return Ok(await catalog.GetProductsByIdsAsync(parsed, ct));
    }

    /// <summary>
    /// A single product by slug. Registered last so it cannot shadow
    /// "facets", "suggest" or "batch".
    /// </summary>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    /* Deliberately NOT cached, unlike the listing above. This response carries
       per-variant stock counts, and the product page renders them literally
       ("Only 3 left in this finish"). A shared 60-second cache would keep
       showing a variant as available after its last one sold — on the exact
       page where someone is deciding to buy it. */
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ProductDto>> GetBySlug(string slug, CancellationToken ct)
        => Ok(await catalog.GetProductBySlugAsync(slug, ct));
}

[ApiController]
[Route("api/categories")]
[Produces("application/json")]
public class CategoriesController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List(CancellationToken ct)
        => Ok(await catalog.ListCategoriesAsync(ct));

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDto>> GetBySlug(string slug, CancellationToken ct)
        => Ok(await catalog.GetCategoryBySlugAsync(slug, ct));
}

[ApiController]
[Route("api/collections")]
[Produces("application/json")]
public class CollectionsController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CollectionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CollectionDto>>> List(CancellationToken ct)
        => Ok(await catalog.ListCollectionsAsync(ct));

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(CollectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CollectionDto>> GetBySlug(string slug, CancellationToken ct)
        => Ok(await catalog.GetCollectionBySlugAsync(slug, ct));
}

[ApiController]
[Route("api/shipping-methods")]
[Produces("application/json")]
public class ShippingMethodsController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ShippingMethodDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ShippingMethodDto>>> List(CancellationToken ct)
        => Ok(await catalog.ListShippingMethodsAsync(ct));
}
