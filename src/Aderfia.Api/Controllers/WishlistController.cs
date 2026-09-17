using System.Security.Claims;
using Aderfia.Application.Customers;
using Microsoft.AspNetCore.Mvc;

namespace Aderfia.Api.Controllers;

public record WishlistToggleRequest
{
    public required Guid ProductId { get; init; }
}

/// <summary>
/// Saved pieces.
/// <para>
/// Every route requires an authenticated subject — a wishlist is personal,
/// and unlike a cart there is no guest equivalent worth issuing a cookie for.
/// The storefront's local wishlist is folded in by POST /merge on first
/// sign-in.
/// </para>
/// </summary>
[ApiController]
[Route("api/wishlist")]
[Produces("application/json")]
public class WishlistController(IWishlistService wishlist) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(WishlistDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<WishlistDto>> Get(CancellationToken ct) =>
        CurrentUserId() is { } userId
            ? Ok(await wishlist.GetAsync(userId, ct))
            : Unauthorized();

    [HttpPost("toggle")]
    [ProducesResponseType(typeof(WishlistDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WishlistDto>> Toggle(
        [FromBody] WishlistToggleRequest request,
        CancellationToken ct) =>
        CurrentUserId() is { } userId
            ? Ok(await wishlist.ToggleAsync(userId, request.ProductId, ct))
            : Unauthorized();

    [HttpPost("merge")]
    [ProducesResponseType(typeof(WishlistDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<WishlistDto>> Merge(
        [FromBody] IReadOnlyList<Guid> productIds,
        CancellationToken ct) =>
        CurrentUserId() is { } userId
            ? Ok(await wishlist.MergeAsync(userId, productIds, ct))
            : Unauthorized();

    [HttpDelete("{productId:guid}")]
    [ProducesResponseType(typeof(WishlistDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<WishlistDto>> Remove(Guid productId, CancellationToken ct) =>
        CurrentUserId() is { } userId
            ? Ok(await wishlist.RemoveAsync(userId, productId, ct))
            : Unauthorized();

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
