using Aderfia.Application.Catalog;
using Aderfia.Application.Common;
using Aderfia.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace Aderfia.Application.Customers;

public record WishlistDto
{
    public IReadOnlyList<string> ProductIds { get; init; } = [];
    public IReadOnlyList<ProductSummaryDto> Products { get; init; } = [];
    public int Count { get; init; }
}

public interface IWishlistService
{
    Task<WishlistDto> GetAsync(Guid userId, CancellationToken ct = default);
    Task<WishlistDto> ToggleAsync(Guid userId, Guid productId, CancellationToken ct = default);
    Task<WishlistDto> RemoveAsync(Guid userId, Guid productId, CancellationToken ct = default);

    /// <summary>Folds a locally-held list into the account after sign-in.</summary>
    Task<WishlistDto> MergeAsync(Guid userId, IReadOnlyList<Guid> productIds, CancellationToken ct = default);
}

/// <summary>
/// Saved pieces for a signed-in customer.
/// <para>
/// The storefront keeps its wishlist in localStorage today — no account
/// needed, works offline. This is the upgrade path: the client calls
/// <see cref="MergeAsync"/> once on first sign-in, and the list then follows
/// the customer between devices.
/// </para>
/// </summary>
public sealed class WishlistService(IAderfiaDbContext db, ICatalogService catalog) : IWishlistService
{
    public async Task<WishlistDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var productIds = await db.WishlistItems
            .Where(w => w.UserId == userId)
            // Newest first, so the page reads as a list of recent saves.
            .OrderByDescending(w => w.CreatedAt)
            // No AsNoTracking: a projection to a scalar is never tracked, and
            // the operator only accepts reference types anyway.
            .Select(w => w.ProductId)
            .ToListAsync(ct);

        // Hydrated in one batch rather than N requests from the client.
        var products = await catalog.GetProductsByIdsAsync(productIds, ct);

        return new WishlistDto
        {
            ProductIds = productIds.Select(id => id.ToString()).ToList(),
            Products = products.Cast<ProductSummaryDto>().ToList(),
            Count = productIds.Count
        };
    }

    /// <summary>
    /// Adds when absent, removes when present — idempotent per resulting
    /// state, which is exactly what a heart toggle needs.
    /// </summary>
    public async Task<WishlistDto> ToggleAsync(Guid userId, Guid productId, CancellationToken ct = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
            throw NotFoundException.For("Product", productId.ToString());

        var existing = await db.WishlistItems
            .FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == productId, ct);

        if (existing is null)
            db.WishlistItems.Add(new WishlistItem { UserId = userId, ProductId = productId });
        else
            db.WishlistItems.Remove(existing);

        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    public async Task<WishlistDto> RemoveAsync(Guid userId, Guid productId, CancellationToken ct = default)
    {
        var existing = await db.WishlistItems
            .FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == productId, ct);

        // Removing something that is not saved is a no-op, not an error.
        if (existing is not null)
        {
            db.WishlistItems.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return await GetAsync(userId, ct);
    }

    public async Task<WishlistDto> MergeAsync(
        Guid userId,
        IReadOnlyList<Guid> productIds,
        CancellationToken ct = default)
    {
        var incoming = productIds.Distinct().ToList();
        if (incoming.Count == 0) return await GetAsync(userId, ct);

        // Ignore ids that no longer exist rather than failing the whole merge:
        // a list held locally for months will contain retired pieces.
        var known = await db.Products
            .Where(p => incoming.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);

        var alreadySaved = await db.WishlistItems
            .Where(w => w.UserId == userId && known.Contains(w.ProductId))
            .Select(w => w.ProductId)
            .ToListAsync(ct);

        // Union, never replace — nothing the customer saved is dropped.
        foreach (var productId in known.Except(alreadySaved))
            db.WishlistItems.Add(new WishlistItem { UserId = userId, ProductId = productId });

        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }
}
