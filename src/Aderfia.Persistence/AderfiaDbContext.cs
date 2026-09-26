using System.Reflection;
using Aderfia.Application.Common;
using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;
using Aderfia.Domain.Customers;
using Aderfia.Domain.Ordering;
using Aderfia.Domain.Site;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aderfia.Persistence;

public class AderfiaDbContext(DbContextOptions<AderfiaDbContext> options)
    : DbContext(options), IAderfiaDbContext
{
    // ---- Catalog ----
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductOption> ProductOptions => Set<ProductOption>();
    public DbSet<ProductOptionValue> ProductOptionValues => Set<ProductOptionValue>();
    public DbSet<VariantOptionValue> VariantOptionValues => Set<VariantOptionValue>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductSpecification> ProductSpecifications => Set<ProductSpecification>();
    public DbSet<ProductMaterial> ProductMaterials => Set<ProductMaterial>();
    public DbSet<ProductBadge> ProductBadges => Set<ProductBadge>();
    public DbSet<ProductRelation> ProductRelations => Set<ProductRelation>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Collection> Collections => Set<Collection>();

    // ---- Customers ----
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();

    // ---- Ordering ----
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();

    // ---- Site ----
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        /* Soft-deleted rows are filtered out globally. Anything that genuinely
           needs them (an admin restore screen, an order's historical product)
           opts back in with IgnoreQueryFilters(). */
        builder.Entity<Product>().HasQueryFilter(p => !p.IsDeleted);
        builder.Entity<Category>().HasQueryFilter(c => !c.IsDeleted);
        builder.Entity<Collection>().HasQueryFilter(c => !c.IsDeleted);
        builder.Entity<User>().HasQueryFilter(u => !u.IsDeleted);

        /* A filter on the principal does NOT propagate to its dependants.
           Without matching filters here, `db.ProductVariants` would still
           return variants belonging to a withdrawn product — which is exactly
           how a soft-deleted piece ends up back in someone's cart. Each
           dependant repeats the parent's condition. */
        builder.Entity<ProductVariant>().HasQueryFilter(v => !v.Product.IsDeleted);
        builder.Entity<ProductOption>().HasQueryFilter(o => !o.Product.IsDeleted);
        builder.Entity<ProductImage>().HasQueryFilter(i => i.Product == null || !i.Product.IsDeleted);
        builder.Entity<ProductSpecification>().HasQueryFilter(s => !s.Product.IsDeleted);
        builder.Entity<ProductMaterial>().HasQueryFilter(m => !m.Product.IsDeleted);
        builder.Entity<ProductBadge>().HasQueryFilter(b => !b.Product.IsDeleted);

        // Both ends of a relation must still exist for the edge to be valid.
        builder.Entity<ProductRelation>()
            .HasQueryFilter(r => !r.Product.IsDeleted && !r.RelatedProduct.IsDeleted);

        builder.Entity<WishlistItem>()
            .HasQueryFilter(w => !w.Product.IsDeleted && !w.User.IsDeleted);

        // Second-level dependants, reached through their own parent.
        builder.Entity<ProductOptionValue>()
            .HasQueryFilter(v => !v.Option.Product.IsDeleted);

        builder.Entity<VariantOptionValue>()
            .HasQueryFilter(v => !v.Variant.Product.IsDeleted);

        /* A cart line pointing at a withdrawn product must disappear from the
           bag rather than render as a broken row. */
        builder.Entity<CartItem>()
            .HasQueryFilter(i => !i.ProductVariant.Product.IsDeleted);

        /* SQLite has no native date type and stores DateTimeOffset as TEXT,
           which makes comparison operators untranslatable — a scheduled
           collection's `StartsAt <= now` throws at runtime rather than
           filtering. Storing it in the binary form keeps ordering and
           comparison intact, offset included. SQL Server has a real
           datetimeoffset type and is left alone. */
        if (Database.IsSqlite())
        {
            var converter = new DateTimeOffsetToBinaryConverter();

            foreach (var property in builder.Model.GetEntityTypes()
                         .SelectMany(t => t.GetProperties())
                         .Where(p => p.ClrType == typeof(DateTimeOffset)
                                     || p.ClrType == typeof(DateTimeOffset?)))
            {
                property.SetValueConverter(converter);
            }
        }

        /* Every string column is bounded by default. Without this, providers
           fall back to nvarchar(max), which cannot be indexed. */
        foreach (var property in builder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is null))
        {
            property.SetMaxLength(512);
        }
    }

    /// <summary>
    /// Stamps <c>UpdatedAt</c> and turns deletes into soft deletes.
    /// Doing it here means no use case can forget to.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var softDeleted = false;

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Deleted when entry.Entity is ISoftDeletable soft:
                    entry.State = EntityState.Modified;
                    soft.IsDeleted = true;
                    soft.DeletedAt = now;
                    entry.Entity.UpdatedAt = now;
                    softDeleted = true;
                    break;
            }
        }

        /* Remove() also cascades Deleted onto the entity's OWNED types —
           Product.Dimensions, ProductVariant.Inventory. Those live in their
           owner's own table, so once the owner is flipped back to Modified,
           an owned entry left in Deleted makes EF write NULL into columns
           that are NOT NULL, and the update is rejected outright.

           Resetting them is safe even alongside a genuine hard delete: every
           owned type here shares its owner's table, so it is removed by the
           owner's DELETE rather than by one of its own. */
        if (softDeleted)
        {
            foreach (var owned in ChangeTracker.Entries()
                         .Where(e => e.State == EntityState.Deleted && e.Metadata.IsOwned()))
            {
                owned.State = EntityState.Unchanged;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
