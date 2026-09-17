using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;

namespace Aderfia.Domain.Customers;

/// <summary>
/// A registered customer.
/// <para>
/// Deliberately holds no password field. Credentials belong to the identity
/// provider (ASP.NET Core Identity or an external IdP); this entity carries
/// only the commerce-facing profile, joined by <see cref="IdentityUserId"/>.
/// </para>
/// </summary>
public class User : Entity, ISoftDeletable
{
    public required string Email { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }

    /// <summary>Subject id issued by the identity provider.</summary>
    public string? IdentityUserId { get; set; }

    public bool AcceptsMarketing { get; set; }

    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    public ICollection<WishlistItem> Wishlist { get; set; } = new List<WishlistItem>();

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public string DisplayName =>
        string.Join(' ', new[] { FirstName, LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
}

/// <summary>
/// A postal address. Also embedded (as an owned type) on orders, where it is
/// a permanent snapshot rather than a reference — editing a saved address
/// must never rewrite the address a past order shipped to.
/// </summary>
public class Address : Entity
{
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Line1 { get; set; }
    public string? Line2 { get; set; }
    public required string City { get; set; }
    public string? Region { get; set; }
    public required string PostalCode { get; set; }
    public required string Country { get; set; }
    public string? Phone { get; set; }

    public bool IsDefaultShipping { get; set; }
    public bool IsDefaultBilling { get; set; }
}

/// <summary>
/// A shopping cart. Supports both signed-in customers (<see cref="UserId"/>)
/// and guests (<see cref="AnonymousId"/>), so a cart built before signing in
/// can be merged rather than lost.
/// </summary>
public class Cart : Entity
{
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Opaque cookie value identifying a guest's cart.</summary>
    public string? AnonymousId { get; set; }

    public string Currency { get; set; } = Money.ShopCurrency;

    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();

    /// <summary>Abandoned carts are swept after this point.</summary>
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30);

    public Money Subtotal() =>
        Items.Aggregate(
            Money.Zero(Currency),
            (total, item) => total + (item.UnitPrice * item.Quantity));

    public int ItemCount() => Items.Sum(i => i.Quantity);
}

/// <summary>
/// One line in a cart.
/// <para>
/// <see cref="UnitPrice"/> is captured when the line is added so a price
/// change mid-session cannot silently alter what the shopper agreed to. It is
/// re-validated against the live variant price at checkout, and the customer
/// is told if it moved.
/// </para>
/// </summary>
public class CartItem : Entity
{
    public Guid CartId { get; set; }
    public Cart Cart { get; set; } = null!;

    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public int Quantity { get; set; }

    public Money UnitPrice { get; set; }

    public Money LineTotal() => UnitPrice * Quantity;
}

/// <summary>A saved product. Keyed on the product, not a variant — a shopper
/// saves "the Halo mirror", then chooses a finish when they buy.</summary>
public class WishlistItem : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
}
