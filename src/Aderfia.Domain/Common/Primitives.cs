namespace Aderfia.Domain.Common;

/// <summary>
/// Base type for every persisted entity. Ids are GUIDs generated in the
/// domain rather than by the database, so an aggregate can be fully built
/// (and its relationships wired) before anything is saved.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// An entity that is reachable by a stable, human-readable URL segment.
/// Slugs are unique per entity type and are what the storefront routes on —
/// ids never appear in a public URL.
/// </summary>
public interface ISluggable
{
    string Slug { get; }
}

/// <summary>
/// Soft deletion. Catalog rows are never hard-deleted: an order placed last
/// year still has to be able to render the product it contains.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>
/// A monetary amount held in MINOR UNITS (cents, fils) as a whole number.
/// <para>
/// Deliberately not <c>decimal</c> for arithmetic: integer minor units make
/// rounding explicit at the one place it belongs — the point of division —
/// instead of letting fractional cents accumulate through discounts and tax.
/// </para>
/// </summary>
public record struct Money
{
    /// <summary>
    /// The currency the shop actually prices in.
    /// <para>
    /// Every amount in the database is stored in this currency, in minor
    /// units. Anything a shopper sees in another currency is converted at
    /// display time and never persisted — which is why there is one constant
    /// here rather than a rate table: the store has one set of prices, and
    /// the second currency is a way of reading them, not a second price.
    /// </para>
    /// </summary>
    public const string ShopCurrency = "JOD";

    /// <summary>
    /// Settable auto-properties with an implicit parameterless constructor:
    /// EF Core maps this as a complex type, and materialising into init-only
    /// positional members is brittle across providers.
    /// </summary>
    public long Amount { get; set; }

    public string Currency { get; set; }

    public Money(long amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Zero(string currency) => new(0, currency);

    /// <summary>A method, not a property: EF maps every property on a complex
    /// type, and a computed one would need explicit exclusion.</summary>
    public bool IsZero() => Amount == 0;

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left with { Amount = left.Amount + right.Amount };
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left with { Amount = left.Amount - right.Amount };
    }

    public static Money operator *(Money money, int quantity) =>
        money with { Amount = money.Amount * quantity };

    /// <summary>Applies a rate (tax, discount) with away-from-zero rounding.</summary>
    public Money Multiply(decimal rate) =>
        this with { Amount = (long)Math.Round(Amount * rate, MidpointRounding.AwayFromZero) };

    public decimal ToMajorUnits() => Amount / 100m;

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot combine {left.Currency} with {right.Currency}.");
        }
    }
}

/// <summary>
/// Physical size. Every field is optional because the shapes vary: a shelf
/// has width/height/depth, a bowl has diameter/height, a clock has diameter
/// and depth. Modelling this as one nullable set beats a table per shape.
/// </summary>
public record Dimensions
{
    public string Unit { get; init; } = "cm";
    public decimal? Width { get; init; }
    public decimal? Height { get; init; }
    public decimal? Depth { get; init; }
    public decimal? Diameter { get; init; }
    /* How wide the wooden frame itself is — the visible border around a
       mirror or a picture, as opposed to the outer Width of the piece. */
    public decimal? FrameWidth { get; init; }
    public decimal? WeightKg { get; init; }
}

/// <summary>
/// The two languages the storefront is published in.
/// </summary>
public enum Language
{
    /// <summary>Arabic — the default. Right-to-left.</summary>
    Ar = 0,

    English = 1
}

/// <summary>
/// A piece of text that exists in both languages.
/// <para>
/// Stored as two columns rather than a translations table. For exactly two
/// languages that is the simpler design by a wide margin: no joins, every
/// field is still sortable and filterable in SQL, and the admin form is two
/// inputs side by side. A translations table only starts paying for itself
/// once languages are added at runtime.
/// </para>
/// <para>
/// Empty means "not written yet", not "deliberately blank" — see
/// <see cref="Get"/>, which falls back rather than rendering a hole in the page.
/// </para>
/// </summary>
public record struct LocalizedText
{
    /* Backed by nullable fields with non-nullable accessors, so that
       `default(LocalizedText)` — which every struct is reachable through, and
       which `FirstOrDefault` over a tuple hands back — reads as two empty
       strings rather than two nulls. Every localized column in the schema is
       NOT NULL; without this, one unset field aborts an entire SaveChanges. */
    private string? ar;
    private string? en;

    public string Ar
    {
        readonly get => ar ?? string.Empty;
        set => ar = value;
    }

    public string En
    {
        readonly get => en ?? string.Empty;
        set => en = value;
    }

    public LocalizedText(string ar, string en)
    {
        this.ar = ar;
        this.en = en;
    }

    /* Equality compares the ACCESSORS, not the fields: the compiler-generated
       version would call default(LocalizedText) and Empty different values
       even though both render identically. */
    public readonly bool Equals(LocalizedText other) =>
        Ar == other.Ar && En == other.En;

    public override readonly int GetHashCode() => HashCode.Combine(Ar, En);

    /// <summary>Both languages the same — for values that do not translate.</summary>
    public static LocalizedText Same(string value) => new(value, value);

    public static LocalizedText Empty => new(string.Empty, string.Empty);

    /// <summary>
    /// The text for a language, falling back to the other when it is missing.
    /// <para>
    /// A half-translated catalogue is normal while content is being written,
    /// and showing the English name is far better than showing nothing at all.
    /// </para>
    /// </summary>
    public readonly string Get(Language language)
    {
        var wanted = language == Language.Ar ? Ar : En;
        if (!string.IsNullOrWhiteSpace(wanted)) return wanted;

        var other = language == Language.Ar ? En : Ar;
        return other ?? string.Empty;
    }

    public readonly bool IsEmpty => string.IsNullOrWhiteSpace(Ar) && string.IsNullOrWhiteSpace(En);

    /// <summary>Null when nothing is written in either language, so optional
    /// fields serialise as absent instead of an empty string.</summary>
    public readonly string? GetOrNull(Language language)
    {
        var value = Get(language);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
