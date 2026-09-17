using System.Linq.Expressions;
using Aderfia.Domain.Common;
// HasColumnName on a complex-type property is a RELATIONAL extension,
// so this using is load-bearing rather than incidental.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aderfia.Persistence.Configurations;

/// <summary>
/// Maps a <see cref="LocalizedText"/> onto two columns — <c>NameAr</c> and
/// <c>NameEn</c> — as an EF complex property.
/// <para>
/// Complex rather than owned: the text has no identity of its own, shares the
/// owner's table, and both columns stay directly filterable and sortable in
/// SQL. That last part matters — sorting the shop alphabetically and searching
/// across both languages both have to happen in the database.
/// </para>
/// <para>
/// Note there is no overload for owned types: EF cannot nest a complex
/// property inside one. Where a localized field lives on an owned type
/// (<c>Inventory.LeadTime</c>) it is stored as two plain string columns with a
/// computed <see cref="LocalizedText"/> facade over them instead.
/// </para>
/// </summary>
internal static class LocalizedTextExtensions
{
    public static EntityTypeBuilder<TEntity> Localized<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, LocalizedText>> selector,
        string columnPrefix,
        int maxLength)
        where TEntity : class
    {
        // Type argument stated explicitly: inside a generic method the
        // compiler otherwise fails to infer the complex type from the
        // forwarded expression and binds the wrong overload.
        builder.ComplexProperty<LocalizedText>(selector, text =>
        {
            /* PropertyAccessMode.Property is load-bearing. EF prefers the
               BACKING FIELD when it can find one, and LocalizedText backs its
               accessors with nullable fields so that `default(LocalizedText)`
               still reads as two empty strings. Reading the field directly
               would hand EF the raw null and violate the NOT NULL column. */
            text.Property(t => t.Ar)
                .HasColumnName($"{columnPrefix}Ar")
                .HasMaxLength(maxLength)
                .UsePropertyAccessMode(PropertyAccessMode.Property);

            text.Property(t => t.En)
                .HasColumnName($"{columnPrefix}En")
                .HasMaxLength(maxLength)
                .UsePropertyAccessMode(PropertyAccessMode.Property);
        });

        return builder;
    }
}
