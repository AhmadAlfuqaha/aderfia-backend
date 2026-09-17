using Aderfia.Application.Catalog;
using Aderfia.Application.Common;
using Aderfia.Domain.Common;
using Aderfia.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace Aderfia.Application.Carts;

/* ---- Contracts ----------------------------------------------------------- */

public record CartLineDto
{
    public required string Id { get; init; }
    public required string ProductId { get; init; }
    public required string ProductSlug { get; init; }
    public required string ProductName { get; init; }
    public required string VariantId { get; init; }
    public required string VariantName { get; init; }
    public required string Sku { get; init; }
    public string? ImageUrl { get; init; }
    public int Quantity { get; init; }
    public required MoneyDto UnitPrice { get; init; }
    public required MoneyDto LineTotal { get; init; }

    /// <summary>True when the live variant price no longer matches the price
    /// captured when the line was added.</summary>
    public bool PriceChanged { get; init; }

    public required string InventoryStatus { get; init; }
}

public record CartDto
{
    public required string Id { get; init; }
    public IReadOnlyList<CartLineDto> Items { get; init; } = [];
    public required MoneyDto Subtotal { get; init; }
    public int ItemCount { get; init; }
    public long FreeShippingRemaining { get; init; }
}

public record AddToCartRequest
{
    public required Guid VariantId { get; init; }
    public int Quantity { get; init; } = 1;
}

public record UpdateCartLineRequest
{
    public required int Quantity { get; init; }
}

public interface ICartService
{
    Task<CartDto> GetAsync(CartOwner owner, CancellationToken ct = default);
    Task<CartDto> AddAsync(CartOwner owner, AddToCartRequest request, CancellationToken ct = default);
    Task<CartDto> UpdateLineAsync(CartOwner owner, Guid variantId, int quantity, CancellationToken ct = default);
    Task<CartDto> RemoveLineAsync(CartOwner owner, Guid variantId, CancellationToken ct = default);
    Task<CartDto> ClearAsync(CartOwner owner, CancellationToken ct = default);

    /// <summary>Folds a guest cart into a customer's on sign-in.</summary>
    Task<CartDto> MergeAsync(string anonymousId, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Who a cart belongs to: a signed-in customer, or a guest identified by an
/// opaque cookie. One type keeps every method signature identical for both.
/// </summary>
public readonly record struct CartOwner(Guid? UserId, string? AnonymousId)
{
    public static CartOwner ForUser(Guid userId) => new(userId, null);
    public static CartOwner ForGuest(string anonymousId) => new(null, anonymousId);

    public bool IsValid => UserId is not null || !string.IsNullOrWhiteSpace(AnonymousId);
}

/* ---- Implementation ------------------------------------------------------ */

public sealed class CartService(
    IAderfiaDbContext db,
    IImageUrlResolver urls,
    IDateTimeProvider clock,
    ICurrentLanguage language) : ICartService
{
    private Language Lang => language.Value;

    /// <summary>Subtotal, in minor units, above which delivery is free.</summary>
    private const long FreeShippingThreshold = 60_000;

    private const int MaxLineQuantity = 20;

    public async Task<CartDto> GetAsync(CartOwner owner, CancellationToken ct = default)
    {
        var cart = await FindCartAsync(owner, ct);
        return cart is null ? EmptyCart() : ToDto(cart);
    }

    public async Task<CartDto> AddAsync(CartOwner owner, AddToCartRequest request, CancellationToken ct = default)
    {
        if (request.Quantity < 1) throw new BusinessRuleException("Quantity must be at least one.");

        var variant = await db.ProductVariants
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Id == request.VariantId, ct)
            ?? throw NotFoundException.For("Variant", request.VariantId.ToString());

        if (!variant.IsPurchasable)
            throw new BusinessRuleException($"{variant.Product.Name} ({variant.Name}) is not available.", isConflict: true);

        var cart = await FindCartAsync(owner, ct) ?? await CreateCartAsync(owner, variant.Price.Currency, ct);

        var existing = cart.Items.FirstOrDefault(i => i.ProductVariantId == variant.Id);
        var newQuantity = Math.Min(MaxLineQuantity, (existing?.Quantity ?? 0) + request.Quantity);

        if (!variant.Inventory.CanFulfil(newQuantity))
        {
            throw new BusinessRuleException(
                $"Only {variant.Inventory.Quantity} of {variant.Product.Name} remain in this finish.",
                isConflict: true);
        }

        if (existing is null)
        {
            var line = new CartItem
            {
                CartId = cart.Id,
                ProductVariantId = variant.Id,
                Quantity = newQuantity,
                // Captured now, re-validated at checkout.
                UnitPrice = variant.Price
            };

            /* Added to the DbSet explicitly, not just to cart.Items.
               Entity pre-assigns its own Id, and when change detection finds
               an untracked child on an ALREADY-TRACKED parent whose key is
               already set, it marks it Modified rather than Added — producing
               an UPDATE that matches no rows. An explicit Add states the
               intent and sidesteps that rule entirely.

               Note we do NOT also add to cart.Items: relationship fixup does
               that, and doing both leaves the same line in the list twice. */
            db.CartItems.Add(line);
        }
        else
        {
            existing.Quantity = newQuantity;
        }

        cart.ExpiresAt = clock.UtcNow.AddDays(30);
        await db.SaveChangesAsync(ct);

        return ToDto(await ReloadAsync(cart.Id, ct));
    }

    public async Task<CartDto> UpdateLineAsync(
        CartOwner owner, Guid variantId, int quantity, CancellationToken ct = default)
    {
        var cart = await FindCartAsync(owner, ct) ?? throw NotFoundException.For("Cart", "current");

        var line = cart.Items.FirstOrDefault(i => i.ProductVariantId == variantId)
                   ?? throw NotFoundException.For("Cart line", variantId.ToString());

        // Zero is a removal, not an error — it is what the stepper sends.
        if (quantity <= 0)
        {
            cart.Items.Remove(line);
            db.CartItems.Remove(line);
        }
        else
        {
            line.Quantity = Math.Min(MaxLineQuantity, quantity);
        }

        await db.SaveChangesAsync(ct);
        return ToDto(await ReloadAsync(cart.Id, ct));
    }

    public async Task<CartDto> RemoveLineAsync(CartOwner owner, Guid variantId, CancellationToken ct = default) =>
        await UpdateLineAsync(owner, variantId, 0, ct);

    public async Task<CartDto> ClearAsync(CartOwner owner, CancellationToken ct = default)
    {
        var cart = await FindCartAsync(owner, ct);
        if (cart is null) return EmptyCart();

        db.CartItems.RemoveRange(cart.Items);
        cart.Items.Clear();
        await db.SaveChangesAsync(ct);

        return ToDto(cart);
    }

    public async Task<CartDto> MergeAsync(string anonymousId, Guid userId, CancellationToken ct = default)
    {
        var guestCart = await FindCartAsync(CartOwner.ForGuest(anonymousId), ct);
        var userCart = await FindCartAsync(CartOwner.ForUser(userId), ct);

        if (guestCart is null)
            return userCart is null ? EmptyCart() : ToDto(userCart);

        if (userCart is null)
        {
            // Nothing to merge into — just claim the guest cart.
            guestCart.UserId = userId;
            guestCart.AnonymousId = null;
            await db.SaveChangesAsync(ct);
            return ToDto(await ReloadAsync(guestCart.Id, ct));
        }

        foreach (var guestLine in guestCart.Items)
        {
            var existing = userCart.Items.FirstOrDefault(i => i.ProductVariantId == guestLine.ProductVariantId);

            if (existing is null)
            {
                // Explicit Add, and fixup handles the navigation — same
                // reasoning as AddAsync above.
                db.CartItems.Add(new CartItem
                {
                    CartId = userCart.Id,
                    ProductVariantId = guestLine.ProductVariantId,
                    Quantity = guestLine.Quantity,
                    UnitPrice = guestLine.UnitPrice
                });
            }
            else
            {
                // Take the larger quantity rather than the sum: a shopper who
                // added the same piece on two devices meant one order of it.
                existing.Quantity = Math.Min(MaxLineQuantity, Math.Max(existing.Quantity, guestLine.Quantity));
            }
        }

        db.CartItems.RemoveRange(guestCart.Items);
        db.Carts.Remove(guestCart);
        await db.SaveChangesAsync(ct);

        return ToDto(await ReloadAsync(userCart.Id, ct));
    }

    /* ---- Helpers --------------------------------------------------------- */

    private IQueryable<Cart> CartQuery() =>
        db.Carts
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v.Product)
                .ThenInclude(p => p.Images)
            .AsSplitQuery();

    private async Task<Cart?> FindCartAsync(CartOwner owner, CancellationToken ct)
    {
        if (!owner.IsValid) throw new BusinessRuleException("A cart owner is required.");

        return owner.UserId is { } userId
            ? await CartQuery().FirstOrDefaultAsync(c => c.UserId == userId, ct)
            : await CartQuery().FirstOrDefaultAsync(c => c.AnonymousId == owner.AnonymousId, ct);
    }

    private async Task<Cart> ReloadAsync(Guid cartId, CancellationToken ct) =>
        await CartQuery().FirstAsync(c => c.Id == cartId, ct);

    private async Task<Cart> CreateCartAsync(CartOwner owner, string currency, CancellationToken ct)
    {
        var cart = new Cart
        {
            UserId = owner.UserId,
            AnonymousId = owner.AnonymousId,
            Currency = currency,
            ExpiresAt = clock.UtcNow.AddDays(30)
        };

        db.Carts.Add(cart);
        await db.SaveChangesAsync(ct);
        return cart;
    }

    private static CartDto EmptyCart() => new()
    {
        Id = Guid.Empty.ToString(),
        Items = [],
        Subtotal = new MoneyDto(0, Money.ShopCurrency),
        ItemCount = 0,
        FreeShippingRemaining = FreeShippingThreshold
    };

    private CartDto ToDto(Cart cart)
    {
        var lines = cart.Items
            .Where(i => i.ProductVariant is not null)
            .Select(item =>
            {
                var variant = item.ProductVariant;
                var product = variant.Product;
                var image = product.Images.FirstOrDefault(i => i.Role == ImageRole.Primary)
                            ?? product.Images.OrderBy(i => i.Position).FirstOrDefault();

                return new CartLineDto
                {
                    Id = item.Id.ToString(),
                    ProductId = product.Id.ToString(),
                    ProductSlug = product.Slug,
                    ProductName = product.Name.Get(Lang),
                    VariantId = variant.Id.ToString(),
                    VariantName = variant.Name.Get(Lang),
                    Sku = variant.Sku,
                    ImageUrl = image is null ? null : urls.Resolve(image.StorageKey),
                    Quantity = item.Quantity,
                    // The LIVE price is what the customer will actually pay.
                    UnitPrice = MoneyDto.From(variant.Price),
                    LineTotal = MoneyDto.From(variant.Price * item.Quantity),
                    PriceChanged = variant.Price.Amount != item.UnitPrice.Amount,
                    InventoryStatus = variant.Inventory.Status.ToWire()
                };
            })
            .ToList();

        var subtotal = lines.Sum(l => l.LineTotal.Amount);

        return new CartDto
        {
            Id = cart.Id.ToString(),
            Items = lines,
            Subtotal = new MoneyDto(subtotal, cart.Currency),
            ItemCount = lines.Sum(l => l.Quantity),
            FreeShippingRemaining = Math.Max(0, FreeShippingThreshold - subtotal)
        };
    }
}
