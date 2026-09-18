using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aderfia.Persistence.Configurations;

/* =========================================================================
   Catalog mapping.

   Shared conventions applied throughout:
     • Money  → a COMPLEX property, stored as two columns on the owner's table
                (…_Amount, …_Currency). No join, and it can be filtered and
                ordered in SQL, which the shop's price sort depends on.
     • Dimensions → an OWNED reference type, optional on variants.
     • Computed properties are ignored explicitly rather than relying on EF
       to skip them.
   ========================================================================= */

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.Property(c => c.Slug).HasMaxLength(160).IsRequired();

        builder
            .Localized(c => c.Name, "Name", 160)
            .Localized(c => c.Tagline, "Tagline", 240)
            .Localized(c => c.Description, "Description", 2000)
            .Localized(c => c.MetaTitle, "MetaTitle", 200)
            .Localized(c => c.MetaDescription, "MetaDescription", 400);

        // Slugs are the public identifier, so uniqueness is a hard constraint.
        builder.HasIndex(c => c.Slug).IsUnique();
        builder.HasIndex(c => c.Position);

        builder.HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            // Deleting a parent must not silently delete a whole sub-tree.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Image)
            .WithMany()
            .HasForeignKey(c => c.ImageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("Collections");

        builder.Property(c => c.Slug).HasMaxLength(160).IsRequired();

        builder
            .Localized(c => c.Title, "Title", 160)
            .Localized(c => c.Subtitle, "Subtitle", 240)
            .Localized(c => c.Description, "Description", 2000)
            .Localized(c => c.MetaTitle, "MetaTitle", 200)
            .Localized(c => c.MetaDescription, "MetaDescription", 400);

        builder.HasIndex(c => c.Slug).IsUnique();

        /* ClientSetNull, not SetNull — the difference is the DATABASE clause,
           not the behaviour.

           Collections and ProductImages reference each other: a collection
           cascade-deletes its Images below, and this FK pointed back the other
           way with ON DELETE SET NULL. SQL Server refuses to create a schema
           where two tables have cascading paths in both directions ("may cause
           cycles or multiple cascade paths") and rejected the whole migration.
           SQLite is permissive and never complained.

           ClientSetNull emits ON DELETE NO ACTION, which breaks the cycle, and
           keeps EF nulling HeroImageId on any Collection in the change tracker.
           Nothing is lost: AdminMediaService.RemoveFileAndRowAsync already
           loads the referencing collection and nulls HeroImageId by hand before
           deleting an image, so the database clause was never what made this
           work. HeroImageId is nullable, so a hero image can still be deleted
           and its collection survives with no hero.

           NOT Restrict (would block deleting an image in use), and emphatically
           not ClientCascade, which would delete the COLLECTION when its hero
           image is removed. */
        builder.HasOne(c => c.HeroImage)
            .WithMany()
            .HasForeignKey(c => c.HeroImageId)
            .OnDelete(DeleteBehavior.ClientSetNull);

        // Supporting studies. A shadow FK keeps ProductImage free of a
        // CollectionId it only sometimes needs. This is the direction that
        // KEEPS its cascade: deleting a collection should take its studies
        // with it.
        builder.HasMany(c => c.Images)
            .WithOne()
            .HasForeignKey("CollectionId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Products)
            .WithMany(p => p.Collections)
            .UsingEntity(join => join.ToTable("CollectionProducts"));
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.Property(p => p.Slug).HasMaxLength(200).IsRequired();

        builder
            .Localized(p => p.Name, "Name", 200)
            .Localized(p => p.Tagline, "Tagline", 240)
            .Localized(p => p.Description, "Description", 4000)
            .Localized(p => p.Story, "Story", 4000)
            .Localized(p => p.CareInstructions, "Care", 1000)
            .Localized(p => p.MetaTitle, "MetaTitle", 200)
            .Localized(p => p.MetaDescription, "MetaDescription", 400);

        builder.HasIndex(p => p.Slug).IsUnique();
        // Covers the shop's default listing: published, by category, featured.
        builder.HasIndex(p => new { p.IsPublished, p.CategoryId, p.IsFeatured });
        builder.HasIndex(p => p.CreatedAt);

        builder.OwnsOne(p => p.Dimensions, dimensions =>
        {
            dimensions.Property(d => d.Unit).HasMaxLength(8);
            dimensions.Property(d => d.Width).HasPrecision(9, 2);
            dimensions.Property(d => d.Height).HasPrecision(9, 2);
            dimensions.Property(d => d.Depth).HasPrecision(9, 2);
            dimensions.Property(d => d.Diameter).HasPrecision(9, 2);
            dimensions.Property(d => d.FrameWidth).HasPrecision(9, 2);
            dimensions.Property(d => d.WeightKg).HasPrecision(9, 3);
        });

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            // A category with products in it cannot be deleted out from under them.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Images)
            .WithOne(i => i.Product!)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Options)
            .WithOne(o => o.Product)
            .HasForeignKey(o => o.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Variants)
            .WithOne(v => v.Product)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Specifications)
            .WithOne(s => s.Product)
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Materials)
            .WithOne(m => m.Product)
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Badges)
            .WithOne(b => b.Product)
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // The default variant is a pointer INTO the owned collection, so it
        // must not cascade — that would try to delete the product with it.
        builder.HasOne(p => p.DefaultVariant)
            .WithMany()
            .HasForeignKey(p => p.DefaultVariantId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(p => p.RelatedProducts)
            .WithOne(r => r.Product)
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("ProductVariants");

        builder.Property(v => v.Sku).HasMaxLength(64).IsRequired();
        builder.Localized(v => v.Name, "Name", 200);

        builder.HasIndex(v => v.Sku).IsUnique();
        builder.HasIndex(v => v.ProductId);

        builder.ComplexProperty(v => v.Price, price =>
        {
            price.Property(m => m.Amount).HasColumnName("PriceAmount");
            price.Property(m => m.Currency).HasColumnName("PriceCurrency").HasMaxLength(3);
        });

        builder.OwnsOne(v => v.Inventory, inventory =>
        {
            inventory.Property(i => i.Quantity).HasColumnName("StockQuantity");
            inventory.Property(i => i.LowStockThreshold).HasColumnName("LowStockThreshold");
            inventory.Property(i => i.IsMadeToOrder).HasColumnName("IsMadeToOrder");
            inventory.Property(i => i.AllowBackorder).HasColumnName("AllowBackorder");
            inventory.Property(i => i.LeadTimeAr).HasColumnName("LeadTimeAr").HasMaxLength(120);
            inventory.Property(i => i.LeadTimeEn).HasColumnName("LeadTimeEn").HasMaxLength(120);

            // A facade over the two columns above, not a column of its own.
            inventory.Ignore(i => i.LeadTime);

            // Derived from the columns above; never stored.
            inventory.Ignore(i => i.Status);
        });

        builder.OwnsOne(v => v.Dimensions, dimensions =>
        {
            /* Unit is marked required so EF has a non-shared column it can use
               to tell "this variant has no size of its own" from "it has one
               whose measurements all happen to be null". The column itself is
               still nullable — required here means "if Unit is null, there is
               no Dimensions object", which is the discriminator an optional
               dependent sharing a table needs. */
            dimensions.Property(d => d.Unit).HasMaxLength(8).IsRequired();
            dimensions.Property(d => d.Width).HasPrecision(9, 2);
            dimensions.Property(d => d.Height).HasPrecision(9, 2);
            dimensions.Property(d => d.Depth).HasPrecision(9, 2);
            dimensions.Property(d => d.Diameter).HasPrecision(9, 2);
            dimensions.Property(d => d.FrameWidth).HasPrecision(9, 2);
            dimensions.Property(d => d.WeightKg).HasPrecision(9, 3);
        });

        // Projections over stored columns.
        builder.Ignore(v => v.CompareAtPrice);
        builder.Ignore(v => v.IsPurchasable);
        builder.Ignore(v => v.IsOnSale);
    }
}

public class ProductOptionConfiguration : IEntityTypeConfiguration<ProductOption>
{
    public void Configure(EntityTypeBuilder<ProductOption> builder)
    {
        builder.ToTable("ProductOptions");

        builder.Localized(o => o.Name, "Name", 80);

        /* No unique index on (ProductId, Name): the name is now two columns,
           and a composite index across a complex property is not expressible.
           Uniqueness is enforced in AdminProductService, which matches axes by
           name when reconciling anyway. */
        builder.HasIndex(o => o.ProductId);

        builder.HasMany(o => o.Values)
            .WithOne(v => v.Option)
            .HasForeignKey(v => v.OptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductOptionValueConfiguration : IEntityTypeConfiguration<ProductOptionValue>
{
    public void Configure(EntityTypeBuilder<ProductOptionValue> builder)
    {
        builder.ToTable("ProductOptionValues");

        builder.Localized(v => v.Value, "Value", 120);
        builder.Property(v => v.Swatch).HasMaxLength(9);

        // Same reasoning as the option above.
        builder.HasIndex(v => v.OptionId);
    }
}

public class VariantOptionValueConfiguration : IEntityTypeConfiguration<VariantOptionValue>
{
    public void Configure(EntityTypeBuilder<VariantOptionValue> builder)
    {
        builder.ToTable("VariantOptionValues");

        // A pure join table: the pair IS the key.
        builder.HasKey(v => new { v.VariantId, v.OptionValueId });

        builder.HasOne(v => v.Variant)
            .WithMany(v => v.OptionValues)
            .HasForeignKey(v => v.VariantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.OptionValue)
            .WithMany(v => v.Variants)
            .HasForeignKey(v => v.OptionValueId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImages");

        builder.Property(i => i.StorageKey).HasMaxLength(512).IsRequired();
        builder.Localized(i => i.AltText, "AltText", 400);
        // A base64 preview is long by nature; give it real room.
        builder.Property(i => i.BlurDataUrl).HasMaxLength(4000);

        builder.HasIndex(i => new { i.ProductId, i.Role, i.Position });

        // Computed from Width and Height.
        builder.Ignore(i => i.AspectRatio);
    }
}

public class ProductSpecificationConfiguration : IEntityTypeConfiguration<ProductSpecification>
{
    public void Configure(EntityTypeBuilder<ProductSpecification> builder)
    {
        builder.ToTable("ProductSpecifications");

        builder
            .Localized(s => s.Group, "Group", 80)
            .Localized(s => s.Label, "Label", 120)
            .Localized(s => s.Value, "Value", 600);

        builder.HasIndex(s => new { s.ProductId, s.Position });
    }
}

public class ProductMaterialConfiguration : IEntityTypeConfiguration<ProductMaterial>
{
    public void Configure(EntityTypeBuilder<ProductMaterial> builder)
    {
        builder.ToTable("ProductMaterials");

        builder.Localized(m => m.Name, "Name", 120);

        // Position 0 is the primary material, and that is what facets count.
        builder.HasIndex(m => m.Position);
        builder.HasIndex(m => new { m.ProductId, m.Position }).IsUnique();
    }
}

public class ProductBadgeConfiguration : IEntityTypeConfiguration<ProductBadge>
{
    public void Configure(EntityTypeBuilder<ProductBadge> builder)
    {
        builder.ToTable("ProductBadges");
        builder.Localized(b => b.Label, "Label", 40);
    }
}

public class ProductRelationConfiguration : IEntityTypeConfiguration<ProductRelation>
{
    public void Configure(EntityTypeBuilder<ProductRelation> builder)
    {
        builder.ToTable("ProductRelations");

        builder.HasIndex(r => new { r.ProductId, r.RelatedProductId }).IsUnique();

        builder.HasOne(r => r.RelatedProduct)
            .WithMany()
            .HasForeignKey(r => r.RelatedProductId)
            // Both ends point at Products; cascading from both would create
            // multiple cascade paths, which SQL Server rejects outright.
            .OnDelete(DeleteBehavior.Restrict);
    }
}
