using System.Security.Cryptography;
using Aderfia.Application.Carts;
using Aderfia.Application.Catalog;
using Aderfia.Application.Common;
using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;
using Aderfia.Domain.Ordering;
using Microsoft.EntityFrameworkCore;

namespace Aderfia.Application.Orders;

/* ---- Contracts ----------------------------------------------------------- */

public record AddressRequest
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Line1 { get; init; }
    public string? Line2 { get; init; }
    public required string City { get; init; }
    public string? Region { get; init; }
    /* Optional, and deliberately so. Jordan does not use postal codes in
       retail addressing and the checkout does not ask for one — a `required`
       field that no form fills is a 400 waiting for the first client that
       does not happen to send an empty string. */
    public string? PostalCode { get; init; }

    public required string Country { get; init; }

    /// <summary>How the workshop replies, so it matters more than the rest.</summary>
    public string? Phone { get; init; }
}

/// <summary>One line the customer wants. Price is NOT accepted from the
/// client — the variant's own price is used.</summary>
public record OrderLineRequest
{
    public required Guid VariantId { get; init; }
    public int Quantity { get; init; } = 1;
}

public record PlaceOrderRequest
{
    /// <summary>
    /// Optional. The storefront no longer asks for an email — the order is
    /// completed over WhatsApp and the phone on the shipping address is the
    /// contact. Kept on the contract for orders placed through other
    /// channels, and because guest lookup still matches on it.
    /// </summary>
    public string? Email { get; init; }
    public required AddressRequest ShippingAddress { get; init; }
    public AddressRequest? BillingAddress { get; init; }

    /// <summary>
    /// Optional. Orders are completed over WhatsApp and delivery is agreed
    /// there, so no method is chosen on the site. Kept on the contract for
    /// when a rate table is reintroduced.
    /// </summary>
    public Guid? ShippingMethodId { get; init; }

    /// <summary>
    /// The lines to order. The storefront keeps its bag in localStorage, so
    /// it sends what it holds; when empty, the server-side cart is used
    /// instead. Either way the server re-prices every line.
    /// </summary>
    public List<OrderLineRequest> Items { get; init; } = [];

    public string? CustomerNote { get; init; }
}

public record OrderItemDto
{
    public required string ProductName { get; init; }
    public required string VariantName { get; init; }
    public required string Sku { get; init; }
    public string? ImageUrl { get; init; }
    public int Quantity { get; init; }
    public required MoneyDto UnitPrice { get; init; }
    public required MoneyDto LineTotal { get; init; }
}

public record OrderDto
{
    public required string Id { get; init; }
    public required string Reference { get; init; }
    public required string Email { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset PlacedAt { get; init; }

    public IReadOnlyList<OrderItemDto> Items { get; init; } = [];

    public required MoneyDto Subtotal { get; init; }
    public required MoneyDto Shipping { get; init; }
    public required MoneyDto Tax { get; init; }
    public required MoneyDto Discount { get; init; }
    public required MoneyDto Total { get; init; }

    public required string ShippingMethodName { get; init; }
    public string? TrackingNumber { get; init; }
}

public interface IOrderService
{
    Task<OrderDto> PlaceAsync(CartOwner owner, PlaceOrderRequest request, CancellationToken ct = default);
    Task<OrderDto> GetByReferenceAsync(string reference, string email, CancellationToken ct = default);
    Task<IReadOnlyList<OrderDto>> ListForUserAsync(Guid userId, CancellationToken ct = default);
}

/* ---- Implementation ------------------------------------------------------ */

public sealed class OrderService(
    IAderfiaDbContext db,
    IImageUrlResolver urls,
    IDateTimeProvider clock,
    ICurrentLanguage language) : IOrderService
{
    /* An order records names in the language it was PLACED in. That is what
       the customer saw when they agreed to buy, what their WhatsApp message
       quotes, and therefore what the record has to preserve. */
    private Language Lang => language.Value;

    public async Task<OrderDto> PlaceAsync(
        CartOwner owner,
        PlaceOrderRequest request,
        CancellationToken ct = default)
    {
        /* Two ways an order arrives.

           The storefront keeps its bag in localStorage and posts the lines it
           holds. A client that had used the server-side cart posts nothing and
           we read that instead. Either way the variant is looked up here and
           ITS price is used — the request never sets a price. */
        var cart = await db.Carts
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v.Product)
                .ThenInclude(p => p.Images)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c =>
                (owner.UserId != null && c.UserId == owner.UserId) ||
                (owner.AnonymousId != null && c.AnonymousId == owner.AnonymousId), ct);

        var requested = request.Items
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.VariantId)
            // The same variant twice in one payload is a quantity, not two lines.
            .Select(g => new { VariantId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToList();

        List<(ProductVariant Variant, int Quantity)> lines;

        if (requested.Count > 0)
        {
            var ids = requested.Select(r => r.VariantId).ToList();

            var variants = await db.ProductVariants
                .Where(v => ids.Contains(v.Id))
                .Include(v => v.Product).ThenInclude(p => p.Images)
                .AsSplitQuery()
                .ToListAsync(ct);

            var byId = variants.ToDictionary(v => v.Id);

            var missing = requested.Where(r => !byId.ContainsKey(r.VariantId)).ToList();
            if (missing.Count > 0)
            {
                throw new BusinessRuleException(
                    "One of the pieces in your bag is no longer available. Please review it and try again.",
                    isConflict: true);
            }

            lines = requested.Select(r => (byId[r.VariantId], r.Quantity)).ToList();
        }
        else
        {
            if (cart is null || cart.Items.Count == 0)
                throw new BusinessRuleException("Your bag is empty.");

            lines = cart.Items.Select(i => (i.ProductVariant, i.Quantity)).ToList();
        }

        if (lines.Count == 0) throw new BusinessRuleException("Your bag is empty.");

        var currency = lines[0].Variant.Price.Currency;

        /* Delivery is agreed over WhatsApp, so no method is normally chosen.
           One is still honoured if supplied, for when a rate table returns. */
        ShippingMethod? shippingMethod = null;
        if (request.ShippingMethodId is { } methodId)
        {
            shippingMethod = await db.ShippingMethods
                .FirstOrDefaultAsync(m => m.Id == methodId && m.IsActive, ct)
                ?? throw NotFoundException.For("Shipping method", methodId.ToString());
        }

        /* Re-check stock at the moment of placement. A bag can sit for days,
           and the last one of something may have gone in the meantime. */
        foreach (var (variant, quantity) in lines)
        {
            if (!variant.Inventory.CanFulfil(quantity))
            {
                throw new BusinessRuleException(
                    $"{variant.Product.Name} ({variant.Name}) is no longer available in that quantity.",
                    isConflict: true);
            }
        }

        var order = new Order
        {
            Reference = GenerateReference(),
            UserId = owner.UserId,
            Email = request.Email?.Trim() ?? string.Empty,
            Currency = currency,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            PlacedAt = clock.UtcNow,
            ShippingAddress = ToOrderAddress(request.ShippingAddress),
            // Billing defaults to shipping when the customer didn't differentiate.
            BillingAddress = ToOrderAddress(request.BillingAddress ?? request.ShippingAddress),
            ShippingMethodId = shippingMethod?.Id,
            ShippingMethodName = shippingMethod is null
                ? (Lang == Language.Ar ? "يُتفق عليه عبر واتساب" : "Arranged on WhatsApp")
                : shippingMethod.Name.Get(Lang),
            CustomerNote = request.CustomerNote
        };

        foreach (var (variant, quantity) in lines)
        {
            var product = variant.Product;
            var image = product.Images.FirstOrDefault(i => i.Role == ImageRole.Primary)
                        ?? product.Images.OrderBy(i => i.Position).FirstOrDefault();

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductVariantId = variant.Id,
                // Snapshotted so the order still renders if the product changes.
                ProductName = product.Name.Get(Lang),
                VariantName = variant.Name.Get(Lang),
                Sku = variant.Sku,
                ImageStorageKey = image?.StorageKey,
                Quantity = quantity,
                // Priced from the LIVE variant, never from the request.
                UnitPrice = variant.Price
            });
        }

        order.TaxTotal = Money.Zero(currency);
        order.DiscountTotal = Money.Zero(currency);
        order.ShippingTotal = Money.Zero(currency);

        order.CalculateTotals();

        // Only meaningful when a method was supplied; otherwise stays zero and
        // the real cost is agreed in the WhatsApp conversation.
        if (shippingMethod is not null)
        {
            order.ShippingTotal = shippingMethod.PriceFor(order.Subtotal);
            order.CalculateTotals();
        }

        db.Orders.Add(order);

        /* Decrement stock for anything actually held. Made-to-order pieces
           are produced on demand and have no count to draw down. */
        foreach (var (variant, quantity) in lines)
        {
            var inventory = variant.Inventory;
            if (!inventory.IsMadeToOrder)
            {
                inventory.Quantity = Math.Max(0, inventory.Quantity - quantity);
            }
        }

        // Empty the server-side cart if there was one, whichever source was used.
        if (cart is not null && cart.Items.Count > 0)
        {
            db.CartItems.RemoveRange(cart.Items);
            cart.Items.Clear();
        }

        await db.SaveChangesAsync(ct);

        /* The order exists as Pending. It is completed over WhatsApp: the
           customer sends the message, and delivery and payment are agreed
           there. No card data passes through this application at any point. */

        return ToDto(order);
    }

    public async Task<OrderDto> GetByReferenceAsync(
        string reference,
        string email,
        CancellationToken ct = default)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Reference == reference, ct)
            ?? throw NotFoundException.For("Order", reference);

        // Guest order lookup requires reference AND email, so a guessed
        // reference alone discloses nothing. An order placed without an email
        // therefore has no second factor and is not retrievable this way —
        // matching "" against "" would make the reference the only secret.
        if (string.IsNullOrWhiteSpace(order.Email) ||
            !string.Equals(order.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
            throw NotFoundException.For("Order", reference);

        return ToDto(order);
    }

    public async Task<IReadOnlyList<OrderDto>> ListForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var orders = await db.Orders
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.PlacedAt)
            .Include(o => o.Items)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(ct);

        return orders.Select(ToDto).ToList();
    }

    /* ---- Helpers --------------------------------------------------------- */

    private static OrderAddress ToOrderAddress(AddressRequest a) => new()
    {
        FirstName = a.FirstName.Trim(),
        LastName = a.LastName.Trim(),
        Line1 = a.Line1.Trim(),
        Line2 = a.Line2?.Trim(),
        City = a.City.Trim(),
        Region = a.Region?.Trim(),
        PostalCode = a.PostalCode?.Trim() ?? string.Empty,
        Country = a.Country.Trim(),
        Phone = a.Phone?.Trim()
    };

    /// <summary>
    /// Human-readable, unambiguous reference. Uses a crypto RNG over an
    /// alphabet with no 0/O or 1/I, so references read correctly over the
    /// phone and cannot be enumerated.
    /// </summary>
    private static string GenerateReference()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> buffer = stackalloc char[6];

        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

        return $"ADF-{new string(buffer)}";
    }

    private OrderDto ToDto(Order order) => new()
    {
        Id = order.Id.ToString(),
        Reference = order.Reference,
        Email = order.Email,
        Status = order.Status.ToString().ToLowerInvariant(),
        PlacedAt = order.PlacedAt,
        Items = order.Items.Select(item => new OrderItemDto
        {
            ProductName = item.ProductName,
            VariantName = item.VariantName,
            Sku = item.Sku,
            ImageUrl = item.ImageStorageKey is null ? null : urls.Resolve(item.ImageStorageKey),
            Quantity = item.Quantity,
            UnitPrice = MoneyDto.From(item.UnitPrice),
            LineTotal = MoneyDto.From(item.LineTotal())
        }).ToList(),
        Subtotal = MoneyDto.From(order.Subtotal),
        Shipping = MoneyDto.From(order.ShippingTotal),
        Tax = MoneyDto.From(order.TaxTotal),
        Discount = MoneyDto.From(order.DiscountTotal),
        Total = MoneyDto.From(order.GrandTotal),
        ShippingMethodName = order.ShippingMethodName,
        TrackingNumber = order.TrackingNumber
    };
}
