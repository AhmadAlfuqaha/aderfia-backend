using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;
using Aderfia.Domain.Customers;

namespace Aderfia.Domain.Ordering;

/// <summary>
/// A placed order.
/// <para>
/// An order is an immutable historical record. Every price, name and address
/// on it is COPIED at the moment of placement rather than referenced, so
/// renaming a product or editing an address years later cannot rewrite what
/// a customer actually bought.
/// </para>
/// </summary>
public class Order : Entity
{
    /// <summary>Customer-facing reference, e.g. "ADF-8K2M4P".</summary>
    public required string Reference { get; set; }

    /// <summary>Null for a guest checkout; the email is always captured.</summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public required string Email { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    public string Currency { get; set; } = Money.ShopCurrency;

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    // ---- Snapshotted addresses (owned, not foreign keys) ----
    public OrderAddress ShippingAddress { get; set; } = null!;
    public OrderAddress BillingAddress { get; set; } = null!;

    // ---- Delivery ----
    public Guid? ShippingMethodId { get; set; }
    public ShippingMethod? ShippingMethod { get; set; }

    /// <summary>Copied from the method, since rates change over time.</summary>
    public string ShippingMethodName { get; set; } = string.Empty;

    // ---- Totals, all frozen at placement ----
    public Money Subtotal { get; set; }
    public Money ShippingTotal { get; set; }
    public Money DiscountTotal { get; set; }
    public Money TaxTotal { get; set; }
    public Money GrandTotal { get; set; }

    // ---- Payment ----
    /// <summary>
    /// The provider's identifier for the payment attempt. No card data is
    /// ever stored by this application — only this reference.
    /// </summary>
    public string? PaymentIntentId { get; set; }
    public string? PaymentProvider { get; set; }

    public DateTimeOffset PlacedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public string? TrackingNumber { get; set; }
    public string? CustomerNote { get; set; }
    public string? InternalNote { get; set; }

    /// <summary>
    /// Recomputes and assigns the totals from the current lines. Called once,
    /// at placement — never afterwards.
    /// </summary>
    public void CalculateTotals()
    {
        /* A default(Money) has no currency, and adding one to a USD amount is
           rightly rejected. Rather than depend on callers assigning every
           component first, treat an unset component as zero in this order's
           currency — the totals are then correct whatever order they are set in. */
        ShippingTotal = Normalise(ShippingTotal);
        TaxTotal = Normalise(TaxTotal);
        DiscountTotal = Normalise(DiscountTotal);

        Subtotal = Items.Aggregate(
            Money.Zero(Currency),
            (total, item) => total + item.LineTotal());

        GrandTotal = Subtotal + ShippingTotal + TaxTotal - DiscountTotal;
    }

    private Money Normalise(Money value) =>
        string.IsNullOrEmpty(value.Currency) ? Money.Zero(Currency) : value;

    public bool CanBeCancelled() =>
        Status is OrderStatus.Pending or OrderStatus.Paid or OrderStatus.InProduction;
}

/// <summary>
/// A purchased line. Product and variant ids are kept for reporting, but the
/// descriptive fields are snapshots — the order must still render correctly
/// after a product is renamed, re-finished or withdrawn.
/// </summary>
public class OrderItem : Entity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    public Guid? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    // ---- Snapshot ----
    public required string ProductName { get; set; }
    public required string VariantName { get; set; }
    public required string Sku { get; set; }
    public string? ImageStorageKey { get; set; }

    public int Quantity { get; set; }
    public Money UnitPrice { get; set; }

    public Money LineTotal() => UnitPrice * Quantity;
}

/// <summary>A frozen copy of an address as it was when the order was placed.</summary>
public class OrderAddress
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Line1 { get; set; }
    public string? Line2 { get; set; }
    public required string City { get; set; }
    public string? Region { get; set; }
    public required string PostalCode { get; set; }
    public required string Country { get; set; }
    public string? Phone { get; set; }
}

/// <summary>
/// A delivery option. Rates are configuration, held in the database so they
/// can be changed from an admin screen without a deployment.
/// </summary>
public class ShippingMethod : Entity
{
    public LocalizedText Name { get; set; } = LocalizedText.Empty;

    public LocalizedText Description { get; set; } = LocalizedText.Empty;

    public Money Price { get; set; }

    /// <summary>Customer-facing estimate, e.g. "5–8 working days".</summary>
    public LocalizedText Estimate { get; set; } = LocalizedText.Empty;

    /// <summary>
    /// Order subtotal in minor units above which this method is free.
    /// Null means it is never free.
    /// </summary>
    public long? FreeAboveSubtotal { get; set; }

    public bool IsActive { get; set; } = true;

    public int Position { get; set; }

    public Money PriceFor(Money subtotal) =>
        FreeAboveSubtotal is { } threshold && subtotal.Amount >= threshold
            ? Money.Zero(Price.Currency)
            : Price;
}
