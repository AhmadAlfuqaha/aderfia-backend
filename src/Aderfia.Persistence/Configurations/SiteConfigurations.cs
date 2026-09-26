using Aderfia.Domain.Site;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aderfia.Persistence.Configurations;

public class SiteSettingsConfiguration : IEntityTypeConfiguration<SiteSettings>
{
    public void Configure(EntityTypeBuilder<SiteSettings> builder)
    {
        builder.ToTable("SiteSettings");

        /* NoAction on both, for the same reason the collection hero and the
           order's product link already use it: SQL Server refuses a schema
           with more than one cascade path into a table, and ProductImages is
           reachable from Products both directly and through here.

           The trade is that a referenced row cannot simply be deleted, so
           each side clears its reference first:

             products  are soft-deleted, so they never reach a real DELETE
             images    are hard-deleted, and AdminMediaService.RemoveFileAndRowAsync
                       nulls this column before removing the row — the same
                       thing it already does for Category.ImageId and
                       Collection.HeroImageId. */

        builder
            .HasOne(s => s.HomeHeroImage)
            .WithMany()
            .HasForeignKey(s => s.HomeHeroImageId)
            .OnDelete(DeleteBehavior.NoAction);

        builder
            .HasOne(s => s.HomeHeroProduct)
            .WithMany()
            .HasForeignKey(s => s.HomeHeroProductId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
