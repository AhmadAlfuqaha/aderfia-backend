namespace Aderfia.Domain.Common;

/// <summary>
/// What an image is FOR, rather than where it happens to sit in a list.
/// The storefront asks for a role, so re-ordering images in the admin can
/// never accidentally change which picture a product card uses.
/// </summary>
public enum ImageRole
{
    Primary = 0,
    Hover = 1,
    Gallery = 2,
    Lifestyle = 3,
    Detail = 4
}

public enum InventoryStatus
{
    InStock = 0,
    LowStock = 1,
    OutOfStock = 2,

    /// <summary>Not held in stock; production starts when the order is placed.</summary>
    MadeToOrder = 3
}

public enum BadgeTone
{
    New = 0,
    Bestseller = 1,
    Limited = 2,
    Sale = 3,
    Handmade = 4
}

public enum OrderStatus
{
    /// <summary>Created, awaiting a successful payment authorisation.</summary>
    Pending = 0,
    Paid = 1,
    InProduction = 2,
    Packed = 3,
    Shipped = 4,
    Delivered = 5,
    Cancelled = 6,
    Refunded = 7
}

public enum PaymentStatus
{
    Unpaid = 0,
    Authorised = 1,
    Captured = 2,
    Failed = 3,
    Refunded = 4
}

/// <summary>Sort orders the storefront can request. Mirrors the UI's SortKey.</summary>
public enum ProductSort
{
    Featured = 0,
    Newest = 1,
    PriceAscending = 2,
    PriceDescending = 3,
    NameAscending = 4
}
