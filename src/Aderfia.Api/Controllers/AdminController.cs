using Aderfia.Api.Middleware;
using Aderfia.Application.Admin;
using Aderfia.Application.Catalog;
using Aderfia.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aderfia.Api.Controllers;

/* =========================================================================
   Admin write API.

   Every route here is guarded by [RequireAdminKey] and must never be cached —
   an admin is editing, so a stale response is worse than a slow one.
   ========================================================================= */

[ApiController]
[Route("api/admin/products")]
[Produces("application/json")]
[RequireAdminKey]
[EnableRateLimiting("admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminProductsController(
    IAdminProductService products,
    IAdminMediaService media) : ControllerBase
{
    /// <summary>Table of every product, drafts included.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminProductRow>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminProductRow>>> List(
        [FromQuery] string? search,
        CancellationToken ct)
        => Ok(await products.ListAsync(search, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdminProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminProductDto>> Get(Guid id, CancellationToken ct)
        => Ok(await products.GetForEditAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(typeof(AdminProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminProductDto>> Create(
        [FromBody] ProductWriteRequest request,
        CancellationToken ct)
    {
        var created = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AdminProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminProductDto>> Update(
        Guid id,
        [FromBody] ProductWriteRequest request,
        CancellationToken ct)
        => Ok(await products.UpdateAsync(id, request, ct));

    /// <summary>Soft delete — the row survives for existing orders.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Publish or unpublish without touching anything else.</summary>
    [HttpPatch("{id:guid}/publish")]
    [ProducesResponseType(typeof(AdminProductDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminProductDto>> SetPublished(
        Guid id,
        [FromBody] PublishUpdate update,
        CancellationToken ct)
        => Ok(await products.SetPublishedAsync(id, update.IsPublished, ct));

    /* ---- Targeted variant edits ------------------------------------------
       So changing one price is not a full-product round trip. */

    [HttpPatch("variants/{variantId:guid}/price")]
    [ProducesResponseType(typeof(AdminProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminProductDto>> UpdatePrice(
        Guid variantId,
        [FromBody] VariantPriceUpdate update,
        CancellationToken ct)
        => Ok(await products.UpdateVariantPriceAsync(variantId, update, ct));

    [HttpPatch("variants/{variantId:guid}/stock")]
    [ProducesResponseType(typeof(AdminProductDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminProductDto>> UpdateStock(
        Guid variantId,
        [FromBody] VariantStockUpdate update,
        CancellationToken ct)
        => Ok(await products.UpdateVariantStockAsync(variantId, update, ct));

    /* ---- Images ---------------------------------------------------------- */

    [HttpGet("{id:guid}/images")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminImageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminImageDto>>> ListImages(Guid id, CancellationToken ct)
        => Ok(await media.ListForProductAsync(id, ct));

    /// <summary>
    /// Uploads a photograph and attaches it to the product. Pixel dimensions
    /// are read from the file itself, so the storefront always reserves the
    /// right box and nothing shifts as images load.
    /// </summary>
    [HttpPost("{id:guid}/images")]
    [ProducesResponseType(typeof(AdminImageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    // 12 MB, matching the storage layer's own limit.
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<AdminImageDto>> UploadImage(
        Guid id,
        IFormFile file,
        [FromForm] string role,
        [FromForm] string? altTextAr,
        [FromForm] string? altTextEn,
        [FromForm] int position,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new BusinessRuleException("Choose a file to upload.");

        await using var stream = file.OpenReadStream();

        var image = await media.UploadAsync(
            MediaOwner.Product,
            id,
            stream,
            file.FileName,
            file.ContentType,
            new ImageAttachRequest
            {
                Role = string.IsNullOrWhiteSpace(role) ? "gallery" : role,
                AltText = new LocalizedTextDto { Ar = altTextAr ?? string.Empty, En = altTextEn ?? string.Empty },
                Position = position
            },
            ct);

        return CreatedAtAction(nameof(ListImages), new { id }, image);
    }

    [HttpPatch("images/{imageId:guid}")]
    [ProducesResponseType(typeof(AdminImageDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminImageDto>> UpdateImage(
        Guid imageId,
        [FromBody] ImageUpdateRequest request,
        CancellationToken ct)
        => Ok(await media.UpdateAsync(imageId, request, ct));

    [HttpDelete("images/{imageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteImage(Guid imageId, CancellationToken ct)
    {
        await media.DeleteAsync(imageId, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/images/order")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminImageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminImageDto>>> ReorderImages(
        Guid id,
        [FromBody] List<Guid> orderedIds,
        CancellationToken ct)
        => Ok(await media.ReorderAsync(id, orderedIds, ct));
}

[ApiController]
[Route("api/admin/categories")]
[Produces("application/json")]
[RequireAdminKey]
[EnableRateLimiting("admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminCategoriesController(
    IAdminTaxonomyService taxonomy,
    IAdminMediaService media) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminCategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminCategoryDto>>> List(CancellationToken ct)
        => Ok(await taxonomy.ListCategoriesAsync(ct));

    [HttpPost]
    [ProducesResponseType(typeof(AdminCategoryDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AdminCategoryDto>> Create(
        [FromBody] CategoryWriteRequest request,
        CancellationToken ct)
    {
        var created = await taxonomy.CreateCategoryAsync(request, ct);
        return CreatedAtAction(nameof(List), null, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AdminCategoryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminCategoryDto>> Update(
        Guid id,
        [FromBody] CategoryWriteRequest request,
        CancellationToken ct)
        => Ok(await taxonomy.UpdateCategoryAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await taxonomy.DeleteCategoryAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/image")]
    [ProducesResponseType(typeof(AdminImageDto), StatusCodes.Status200OK)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<AdminImageDto>> UploadImage(
        Guid id,
        IFormFile file,
        [FromForm] string? altTextAr,
        [FromForm] string? altTextEn,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new BusinessRuleException("Choose a file to upload.");

        await using var stream = file.OpenReadStream();

        return Ok(await media.UploadAsync(
            MediaOwner.Category, id, stream, file.FileName, file.ContentType,
            new ImageAttachRequest
            {
                Role = "primary",
                AltText = new LocalizedTextDto { Ar = altTextAr ?? string.Empty, En = altTextEn ?? string.Empty }
            },
            ct));
    }
}

[ApiController]
[Route("api/admin/collections")]
[Produces("application/json")]
[RequireAdminKey]
[EnableRateLimiting("admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminCollectionsController(
    IAdminTaxonomyService taxonomy,
    IAdminMediaService media) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminCollectionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminCollectionDto>>> List(CancellationToken ct)
        => Ok(await taxonomy.ListCollectionsAsync(ct));

    [HttpPost]
    [ProducesResponseType(typeof(AdminCollectionDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AdminCollectionDto>> Create(
        [FromBody] CollectionWriteRequest request,
        CancellationToken ct)
    {
        var created = await taxonomy.CreateCollectionAsync(request, ct);
        return CreatedAtAction(nameof(List), null, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AdminCollectionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminCollectionDto>> Update(
        Guid id,
        [FromBody] CollectionWriteRequest request,
        CancellationToken ct)
        => Ok(await taxonomy.UpdateCollectionAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await taxonomy.DeleteCollectionAsync(id, ct);
        return NoContent();
    }

    /// <summary>`hero` replaces the banner; `study` appends a supporting image.</summary>
    [HttpPost("{id:guid}/images")]
    [ProducesResponseType(typeof(AdminImageDto), StatusCodes.Status200OK)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<AdminImageDto>> UploadImage(
        Guid id,
        IFormFile file,
        [FromForm] string kind,
        [FromForm] string? altTextAr,
        [FromForm] string? altTextEn,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new BusinessRuleException("Choose a file to upload.");

        var owner = string.Equals(kind, "study", StringComparison.OrdinalIgnoreCase)
            ? MediaOwner.CollectionStudy
            : MediaOwner.CollectionHero;

        await using var stream = file.OpenReadStream();

        return Ok(await media.UploadAsync(
            owner, id, stream, file.FileName, file.ContentType,
            new ImageAttachRequest
            {
                Role = "lifestyle",
                AltText = new LocalizedTextDto { Ar = altTextAr ?? string.Empty, En = altTextEn ?? string.Empty }
            },
            ct));
    }
}

/// <summary>
/// Lets the admin UI check a key before showing the dashboard, so a wrong key
/// produces a clear sign-in error rather than a wall of failed requests.
/// </summary>
[ApiController]
[Route("api/admin/session")]
[Produces("application/json")]
[RequireAdminKey]
[EnableRateLimiting("admin")]
public class AdminSessionController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Verify() => Ok(new { ok = true });
}
