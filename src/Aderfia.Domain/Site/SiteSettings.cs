using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;

namespace Aderfia.Domain.Site;

/// <summary>
/// The handful of things an admin can change about the storefront itself
/// rather than about a product — held as ONE row.
///
/// <para>
/// A single row rather than a key/value table on purpose: every setting here
/// is a typed relationship the database can enforce, and a key/value store
/// would turn each of them into a string that nothing checks. The row is
/// found with "the first one" rather than a well-known id, so nothing has to
/// seed it — <see cref="Aderfia.Application.Site.SiteSettingsService"/>
/// creates it on first write.
/// </para>
/// </summary>
public class SiteSettings : Entity
{
    /* ---- Homepage hero card -------------------------------------------------
       The picture on the phone homepage, under the headline.

       Image and product are deliberately SEPARATE columns. The card has
       always linked through to a product and still does, but the photograph
       shown no longer has to be one of that product's own gallery images —
       which is the whole point of managing it from its own admin page rather
       than by editing the product.

       Both are nullable, and null means "fall back to what the site did
       before this table existed": the newest product and its lifestyle shot.
       So an empty row behaves exactly like no row at all. */

    /// <summary>
    /// Picture shown in the homepage hero card. Its <c>ProductId</c> is null —
    /// it belongs to the site, not to a product — which is why
    /// <see cref="ProductImage.ProductId"/> is nullable.
    /// </summary>
    public Guid? HomeHeroImageId { get; set; }
    public ProductImage? HomeHeroImage { get; set; }

    /// <summary>Product the hero card opens when it is tapped.</summary>
    public Guid? HomeHeroProductId { get; set; }
    public Product? HomeHeroProduct { get; set; }
}
