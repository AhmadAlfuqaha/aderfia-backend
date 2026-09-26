using Aderfia.Api.Middleware;
using Aderfia.Application.Common;
using Aderfia.Application.Site;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aderfia.Api.Controllers;

/// <summary>
/// Homepage content the admin controls without a deploy.
/// </summary>
[ApiController]
[Route("api/homepage")]
[Produces("application/json")]
public class HomepageController(ISiteSettingsService site) : ControllerBase
{
    /// <summary>
    /// The hero card on the phone homepage.
    /// <para>
    /// Returns 204 rather than a body when nothing has been configured, so
    /// the storefront can tell "not set" from "set to something" and keep its
    /// own fallback — the newest product — for the first case.
    /// </para>
    /// </summary>
    [HttpGet("hero")]
    [ProducesResponseType(typeof(HomeHeroDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // Short, and varying by language because the alt text is localised. The
    // admin changes this rarely; a minute of staleness is not worth a
    // database round trip on every homepage visit.
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, VaryByQueryKeys = ["lang"])]
    public async Task<ActionResult<HomeHeroDto>> Hero(CancellationToken ct)
    {
        var hero = await site.GetHomeHeroAsync(ct);
        return hero is null ? NoContent() : Ok(hero);
    }
}

/// <summary>
/// Editing side of the same setting.
/// </summary>
[ApiController]
[Route("api/admin/homepage")]
[Produces("application/json")]
[RequireAdminKey]
[EnableRateLimiting("admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminHomepageController(ISiteSettingsService site) : ControllerBase
{
    /// <summary>Current hero card: the picture and the product it links to.</summary>
    [HttpGet("hero")]
    [ProducesResponseType(typeof(AdminHomeHeroDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminHomeHeroDto>> Get(CancellationToken ct) =>
        Ok(await site.GetAdminHomeHeroAsync(ct));

    /// <summary>Choose the product the card opens. A null id clears it.</summary>
    [HttpPut("hero")]
    [ProducesResponseType(typeof(AdminHomeHeroDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminHomeHeroDto>> SetProduct(
        [FromBody] HomeHeroWriteRequest request,
        CancellationToken ct) =>
        Ok(await site.SetHomeHeroProductAsync(request, ct));

    /// <summary>
    /// Replace the picture. The previous one is deleted from blob storage
    /// once the new row is safely saved.
    /// </summary>
    [HttpPost("hero/image")]
    [ProducesResponseType(typeof(AdminHomeHeroDto), StatusCodes.Status200OK)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<AdminHomeHeroDto>> UploadImage(
        IFormFile file,
        [FromForm] string? altTextAr,
        [FromForm] string? altTextEn,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new BusinessRuleException("Choose a file to upload.");

        await using var stream = file.OpenReadStream();

        return Ok(await site.SetHomeHeroImageAsync(
            stream,
            file.FileName,
            file.ContentType,
            new LocalizedTextDto { Ar = altTextAr ?? string.Empty, En = altTextEn ?? string.Empty },
            ct));
    }

    /// <summary>Remove the picture and go back to the product's own photo.</summary>
    [HttpDelete("hero/image")]
    [ProducesResponseType(typeof(AdminHomeHeroDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminHomeHeroDto>> DeleteImage(CancellationToken ct) =>
        Ok(await site.ClearHomeHeroImageAsync(ct));
}
