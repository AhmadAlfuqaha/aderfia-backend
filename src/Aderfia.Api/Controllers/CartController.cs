using System.Security.Claims;
using Aderfia.Application.Carts;
using Aderfia.Application.Orders;
using Microsoft.AspNetCore.Mvc;

namespace Aderfia.Api.Controllers;

/// <summary>
/// Server-side cart.
/// <para>
/// The storefront currently keeps its bag in localStorage, which works
/// offline and needs no account. These endpoints are the upgrade path: once
/// accounts exist, the client posts to these instead and the cart follows the
/// customer between devices. The DTO shapes are already aligned.
/// </para>
/// </summary>
[ApiController]
[Route("api/cart")]
[Produces("application/json")]
public class CartController(ICartService carts) : ControllerBase
{
    private const string GuestCookie = "aderfia_cart";

    [HttpGet]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CartDto>> Get(CancellationToken ct)
        => Ok(await carts.GetAsync(ResolveOwner(), ct));

    [HttpPost("items")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CartDto>> Add(
        [FromBody] AddToCartRequest request,
        CancellationToken ct)
        => Ok(await carts.AddAsync(ResolveOwner(), request, ct));

    [HttpPatch("items/{variantId:guid}")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartDto>> UpdateLine(
        Guid variantId,
        [FromBody] UpdateCartLineRequest request,
        CancellationToken ct)
        => Ok(await carts.UpdateLineAsync(ResolveOwner(), variantId, request.Quantity, ct));

    [HttpDelete("items/{variantId:guid}")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CartDto>> RemoveLine(Guid variantId, CancellationToken ct)
        => Ok(await carts.RemoveLineAsync(ResolveOwner(), variantId, ct));

    [HttpDelete]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CartDto>> Clear(CancellationToken ct)
        => Ok(await carts.ClearAsync(ResolveOwner(), ct));

    /* ---- Owner resolution -------------------------------------------------
       A signed-in customer owns their cart by user id. A guest is identified
       by an opaque, HttpOnly cookie issued on first contact — the value is
       never read by client script and carries no personal data. */

    private CartOwner ResolveOwner()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(subject, out var userId)) return CartOwner.ForUser(userId);

        if (Request.Cookies.TryGetValue(GuestCookie, out var existing) && !string.IsNullOrWhiteSpace(existing))
            return CartOwner.ForGuest(existing);

        var anonymousId = Guid.CreateVersion7().ToString("N");

        Response.Cookies.Append(GuestCookie, anonymousId, new CookieOptions
        {
            HttpOnly = true,
            // Lax rather than Strict: the cart must survive arriving from an
            // email link or a search result.
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            IsEssential = true,
            Path = "/"
        });

        return CartOwner.ForGuest(anonymousId);
    }
}

[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public class OrdersController(IOrderService orders) : ControllerBase
{
    private const string GuestCookie = "aderfia_cart";

    /// <summary>
    /// Places an order from the current cart.
    /// <para>
    /// The order is created as <c>Pending</c> and no payment is taken here.
    /// Payment is confirmed against the provider and finalised by its
    /// webhook — no card details reach this application.
    /// </para>
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Place(
        [FromBody] PlaceOrderRequest request,
        CancellationToken ct)
    {
        var owner = ResolveOwner();
        var order = await orders.PlaceAsync(owner, request, ct);

        return CreatedAtAction(
            nameof(GetByReference),
            new { reference = order.Reference },
            order);
    }

    /// <summary>
    /// Guest order lookup. Requires the reference AND the email it was placed
    /// with, so a guessed reference alone reveals nothing.
    /// </summary>
    [HttpGet("{reference}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetByReference(
        string reference,
        [FromQuery] string email,
        CancellationToken ct)
        => Ok(await orders.GetByReferenceAsync(reference, email ?? string.Empty, ct));

    private CartOwner ResolveOwner()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(subject, out var userId)) return CartOwner.ForUser(userId);

        Request.Cookies.TryGetValue(GuestCookie, out var anonymousId);
        return CartOwner.ForGuest(anonymousId ?? string.Empty);
    }
}
