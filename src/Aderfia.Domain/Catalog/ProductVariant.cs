using Aderfia.Domain.Common;

namespace Aderfia.Domain.Catalog;

/// <summary>
/// A named axis of choice — "Finish", "Size", "Orientation".
/// <para>
/// Options are data, not code. Introducing a "Movement" axis for a new clock
/// range needs no migration and no frontend change: the storefront renders
/// whatever axes a product declares, using swatches where values carry a
/// colour and pills otherwise.
/// </para>
/// </summary>
public class ProductOption : Entity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public LocalizedText Name { get; set; } = LocalizedText.Empty;

    public int Position { get; set; }

    public ICollection<ProductOptionValue> Values { get; set; } = new List<ProductOptionValue>();
}

public class ProductOptionValue : Entity
{
    public Guid OptionId { get; set; }
    public ProductOption Option { get; set; } = null!;

    public LocalizedText Value { get; set; } = LocalizedText.Empty;

    /// <summary>Hex colour for finish/material axes. Null renders as a pill.</summary>
    public string? Swatch { get; set; }

    public int Position { get; set; }

    public ICollection<VariantOptionValue> Variants { get; set; } = new List<VariantOptionValue>();
}

/// <summary>
/// One concrete, purchasable combination — "Natural Oak / Ø 80".
/// <para>
/// Price and stock live HERE, never on the product. A product is a listing;
/// a variant is the thing that is actually bought, shipped and counted.
/// </para>
/// </summary>
public class ProductVariant : Entity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public required string Sku { get; set; }

    /// <summary>Human label, derived from the chosen option values.</summary>
    public LocalizedText Name { get; set; } = LocalizedText.Empty;

    public Money Price { get; set; }

    /// <summary>
    /// Original price when on offer, in minor units; null when not on offer.
    /// <para>
    /// Stored as a bare amount rather than a nullable <see cref="Money"/>
    /// because EF Core does not support optional complex properties. The
    /// currency is always the variant's own, so nothing is lost.
    /// </para>
    /// </summary>
    public long? CompareAtAmount { get; set; }

    /// <summary>Convenience projection over <see cref="CompareAtAmount"/>.</summary>
    public Money? CompareAtPrice =>
        CompareAtAmount is { } amount ? new Money(amount, Price.Currency) : null;

    public bool IsOnSale => CompareAtAmount is { } amount && amount > Price.Amount;

    public Inventory Inventory { get; set; } = new();

    /// <summary>
    /// Set when an option changes the physical object — a Ø 100 mirror is not
    /// a Ø 60 mirror. Null means "same as the product".
    /// </summary>
    public Dimensions? Dimensions { get; set; }

    public ICollection<VariantOptionValue> OptionValues { get; set; } = new List<VariantOptionValue>();

    public int Position { get; set; }

    public bool IsPurchasable => Inventory.Status != InventoryStatus.OutOfStock;
}

/// <summary>Join between a variant and the option values that define it.</summary>
public class VariantOptionValue
{
    public Guid VariantId { get; set; }
    public ProductVariant Variant { get; set; } = null!;

    public Guid OptionValueId { get; set; }
    public ProductOptionValue OptionValue { get; set; } = null!;
}

/// <summary>
/// Stock for one variant. An owned value object rather than its own table —
/// inventory has no identity or lifetime of its own apart from the variant.
/// </summary>
public class Inventory
{
    public int Quantity { get; set; }

    /// <summary>At or below this, the storefront shows "only N left".</summary>
    public int LowStockThreshold { get; set; } = 3;

    /// <summary>Produced on demand; <see cref="Quantity"/> is not meaningful.</summary>
    public bool IsMadeToOrder { get; set; }

    /* Customer-facing copy such as "Ships in 2–3 weeks".

       Stored as two plain columns rather than a complex property: EF cannot
       nest a complex type inside an owned one, and Inventory is owned by the
       variant. The facade below keeps the rest of the codebase working in
       LocalizedText like everywhere else. */
    public string LeadTimeAr { get; set; } = string.Empty;
    public string LeadTimeEn { get; set; } = string.Empty;

    public LocalizedText LeadTime
    {
        get => new(LeadTimeAr, LeadTimeEn);
        set
        {
            LeadTimeAr = value.Ar ?? string.Empty;
            LeadTimeEn = value.En ?? string.Empty;
        }
    }

    /// <summary>Lets a variant be sold past zero (pre-order).</summary>
    public bool AllowBackorder { get; set; }

    /// <summary>
    /// Derived rather than stored, so status can never disagree with the
    /// quantity it is supposed to describe.
    /// </summary>
    public InventoryStatus Status =>
        IsMadeToOrder ? InventoryStatus.MadeToOrder
        : Quantity <= 0 ? (AllowBackorder ? InventoryStatus.LowStock : InventoryStatus.OutOfStock)
        : Quantity <= LowStockThreshold ? InventoryStatus.LowStock
        : InventoryStatus.InStock;

    public bool CanFulfil(int quantity) =>
        IsMadeToOrder || AllowBackorder || Quantity >= quantity;
}
