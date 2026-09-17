using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;
using Aderfia.Domain.Customers;
using Aderfia.Domain.Ordering;
using Microsoft.EntityFrameworkCore;

namespace Aderfia.Application.Common;

/// <summary>
/// The database, as the Application layer is allowed to see it.
/// <para>
/// Declared here and implemented in Persistence so use cases depend on an
/// abstraction they own, and can be tested against an in-memory or SQLite
/// context without the API project being involved.
/// </para>
/// </summary>
public interface IAderfiaDbContext
{
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<ProductOption> ProductOptions { get; }
    DbSet<ProductOptionValue> ProductOptionValues { get; }
    DbSet<VariantOptionValue> VariantOptionValues { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<ProductSpecification> ProductSpecifications { get; }
    DbSet<ProductMaterial> ProductMaterials { get; }
    DbSet<ProductBadge> ProductBadges { get; }
    DbSet<ProductRelation> ProductRelations { get; }

    DbSet<Category> Categories { get; }
    DbSet<Collection> Collections { get; }

    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<User> Users { get; }
    DbSet<Address> Addresses { get; }
    DbSet<WishlistItem> WishlistItems { get; }

    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<ShippingMethod> ShippingMethods { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns a stored image key into a URL the browser can fetch. Implemented in
/// Infrastructure, so switching from local disk to a CDN never reaches the
/// Application layer.
/// </summary>
public interface IImageUrlResolver
{
    string Resolve(string storageKey);
}

/// <summary>Injected rather than calling DateTimeOffset.UtcNow directly, so
/// time-dependent rules (scheduled collections) are testable.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// The language the current request is being served in.
/// <para>
/// Resolved once per request in the API from <c>?lang=</c> or the
/// Accept-Language header, then injected wherever text has to be picked. That
/// keeps every service signature free of a language parameter that would
/// otherwise have to be threaded through all of them.
/// </para>
/// </summary>
public interface ICurrentLanguage
{
    Language Value { get; }
}

/// <summary>A stored image file, as the Application layer sees it.</summary>
public record StoredImage(string StorageKey, int Width, int Height, long SizeBytes);

/// <summary>
/// Where uploaded images live.
/// <para>
/// Abstracted so the admin's upload endpoint works the same whether files
/// land on local disk, S3 or Azure Blob — only the implementation in
/// Infrastructure changes.
/// </para>
/// </summary>
public interface IImageStorage
{
    /// <summary>
    /// Persists an image and reports its real pixel dimensions. The caller
    /// supplies a folder prefix ("products/halo-round-mirror"); the store
    /// decides the filename to avoid collisions.
    /// </summary>
    Task<StoredImage> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken ct = default);

    /// <summary>Removes a file. Missing files are not an error.</summary>
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}

/// <summary>Turns a name into a URL-safe slug, and keeps it unique.</summary>
public interface ISlugGenerator
{
    string Generate(string input);
}

/// <summary>
/// One page of results, with the metadata a "load more" control needs.
/// </summary>
public record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }

    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public static PagedResult<T> Empty(int page, int pageSize) =>
        new() { Items = [], Page = page, PageSize = pageSize, TotalItems = 0 };
}

/// <summary>
/// Thrown when a requested resource does not exist. Translated to a 404
/// ProblemDetails response by the API's exception middleware — use cases
/// never construct HTTP results themselves.
/// </summary>
public sealed class NotFoundException(string message) : Exception(message)
{
    public static NotFoundException For(string entity, string identifier) =>
        new($"{entity} '{identifier}' could not be found.");
}

/// <summary>Thrown when a request is well-formed but breaks a business rule.
/// Becomes a 400 or 409 depending on <see cref="IsConflict"/>.</summary>
public sealed class BusinessRuleException(string message, bool isConflict = false) : Exception(message)
{
    public bool IsConflict { get; } = isConflict;
}
