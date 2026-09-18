using Aderfia.Domain.Customers;
using Aderfia.Domain.Ordering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aderfia.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.FirstName).HasMaxLength(100);
        builder.Property(u => u.LastName).HasMaxLength(100);
        builder.Property(u => u.Phone).HasMaxLength(40);
        builder.Property(u => u.IdentityUserId).HasMaxLength(128);

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.IdentityUserId);

        builder.HasMany(u => u.Addresses)
            .WithOne(a => a.User!)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.Wishlist)
            .WithOne(w => w.User)
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(u => u.DisplayName);
    }
}

public class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("Addresses");

        builder.Property(a => a.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.LastName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Line1).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Line2).HasMaxLength(200);
        builder.Property(a => a.City).HasMaxLength(120).IsRequired();
        builder.Property(a => a.Region).HasMaxLength(120);
        builder.Property(a => a.PostalCode).HasMaxLength(24).IsRequired();
        builder.Property(a => a.Country).HasMaxLength(120).IsRequired();
        builder.Property(a => a.Phone).HasMaxLength(40);
    }
}

public class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.ToTable("WishlistItems");

        // Saving the same product twice is a no-op, not two rows.
        builder.HasIndex(w => new { w.UserId, w.ProductId }).IsUnique();

        builder.HasOne(w => w.Product)
            .WithMany()
            .HasForeignKey(w => w.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");

        builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.AnonymousId).HasMaxLength(64);

        // A signed-in customer has exactly one active cart; guests are keyed
        // by their cookie value instead.
        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.AnonymousId);
        builder.HasIndex(c => c.ExpiresAt);

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Items)
            .WithOne(i => i.Cart)
            .HasForeignKey(i => i.CartId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItems");

        // One row per variant; adding again increments the quantity.
        builder.HasIndex(i => new { i.CartId, i.ProductVariantId }).IsUnique();

        builder.ComplexProperty(i => i.UnitPrice, price =>
        {
            price.Property(m => m.Amount).HasColumnName("UnitPriceAmount");
            price.Property(m => m.Currency).HasColumnName("UnitPriceCurrency").HasMaxLength(3);
        });

        builder.HasOne(i => i.ProductVariant)
            .WithMany()
            .HasForeignKey(i => i.ProductVariantId)
            // A variant that is still in someone's cart cannot be deleted;
            // it is withdrawn from sale instead.
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ShippingMethodConfiguration : IEntityTypeConfiguration<ShippingMethod>
{
    public void Configure(EntityTypeBuilder<ShippingMethod> builder)
    {
        builder.ToTable("ShippingMethods");

        builder
            .Localized(m => m.Name, "Name", 120)
            .Localized(m => m.Description, "Description", 400)
            .Localized(m => m.Estimate, "Estimate", 120);

        builder.ComplexProperty(m => m.Price, price =>
        {
            price.Property(p => p.Amount).HasColumnName("PriceAmount");
            price.Property(p => p.Currency).HasColumnName("PriceCurrency").HasMaxLength(3);
        });
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.Property(o => o.Reference).HasMaxLength(32).IsRequired();
        builder.Property(o => o.Email).HasMaxLength(320).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();
        builder.Property(o => o.ShippingMethodName).HasMaxLength(120);
        builder.Property(o => o.PaymentIntentId).HasMaxLength(128);
        builder.Property(o => o.PaymentProvider).HasMaxLength(64);
        builder.Property(o => o.TrackingNumber).HasMaxLength(120);
        builder.Property(o => o.CustomerNote).HasMaxLength(1000);
        builder.Property(o => o.InternalNote).HasMaxLength(2000);

        builder.HasIndex(o => o.Reference).IsUnique();
        builder.HasIndex(o => o.Email);
        builder.HasIndex(o => new { o.Status, o.PlacedAt });

        // Enums are stored as strings: a database dump stays readable, and
        // inserting a new enum member cannot renumber the existing rows.
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(32);

        // Each total is a complex property flattened onto two columns of the
        // Orders table. Written out rather than looped: EF's model builder
        // wants literal member expressions, not ones handed to it in a variable.
        builder.ComplexProperty(o => o.Subtotal, money =>
        {
            money.Property(m => m.Amount).HasColumnName("SubtotalAmount");
            money.Property(m => m.Currency).HasColumnName("SubtotalCurrency").HasMaxLength(3);
        });

        builder.ComplexProperty(o => o.ShippingTotal, money =>
        {
            money.Property(m => m.Amount).HasColumnName("ShippingTotalAmount");
            money.Property(m => m.Currency).HasColumnName("ShippingTotalCurrency").HasMaxLength(3);
        });

        builder.ComplexProperty(o => o.DiscountTotal, money =>
        {
            money.Property(m => m.Amount).HasColumnName("DiscountTotalAmount");
            money.Property(m => m.Currency).HasColumnName("DiscountTotalCurrency").HasMaxLength(3);
        });

        builder.ComplexProperty(o => o.TaxTotal, money =>
        {
            money.Property(m => m.Amount).HasColumnName("TaxTotalAmount");
            money.Property(m => m.Currency).HasColumnName("TaxTotalCurrency").HasMaxLength(3);
        });

        builder.ComplexProperty(o => o.GrandTotal, money =>
        {
            money.Property(m => m.Amount).HasColumnName("GrandTotalAmount");
            money.Property(m => m.Currency).HasColumnName("GrandTotalCurrency").HasMaxLength(3);
        });

        ConfigureAddress(builder.OwnsOne(o => o.ShippingAddress), "Shipping");
        ConfigureAddress(builder.OwnsOne(o => o.BillingAddress), "Billing");

        builder.HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            // Deleting a customer must never destroy their order history.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.ShippingMethod)
            .WithMany()
            .HasForeignKey(o => o.ShippingMethodId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureAddress(OwnedNavigationBuilder<Order, OrderAddress> address, string prefix)
    {
        address.Property(a => a.FirstName).HasColumnName($"{prefix}FirstName").HasMaxLength(100);
        address.Property(a => a.LastName).HasColumnName($"{prefix}LastName").HasMaxLength(100);
        address.Property(a => a.Line1).HasColumnName($"{prefix}Line1").HasMaxLength(200);
        address.Property(a => a.Line2).HasColumnName($"{prefix}Line2").HasMaxLength(200);
        address.Property(a => a.City).HasColumnName($"{prefix}City").HasMaxLength(120);
        address.Property(a => a.Region).HasColumnName($"{prefix}Region").HasMaxLength(120);
        address.Property(a => a.PostalCode).HasColumnName($"{prefix}PostalCode").HasMaxLength(24);
        address.Property(a => a.Country).HasColumnName($"{prefix}Country").HasMaxLength(120);
        address.Property(a => a.Phone).HasColumnName($"{prefix}Phone").HasMaxLength(40);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");

        builder.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.VariantName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Sku).HasMaxLength(64).IsRequired();
        builder.Property(i => i.ImageStorageKey).HasMaxLength(512);

        builder.ComplexProperty(i => i.UnitPrice, price =>
        {
            price.Property(m => m.Amount).HasColumnName("UnitPriceAmount");
            price.Property(m => m.Currency).HasColumnName("UnitPriceCurrency").HasMaxLength(3);
        });

        /* Both links are optional and non-cascading: an order line survives
           the withdrawal of the product it refers to, because the descriptive
           fields above are a snapshot.

           ClientSetNull on the product link rather than SetNull, for the same
           SQL Server reason as Collection.HeroImage. Deleting a Product could
           reach OrderItems two ways — directly through this FK, and through
           ProductVariants, which cascades from Products and is SET NULL from
           here. SQL Server counts that as multiple cascade paths and refuses
           to create the constraint.

           Costs nothing: Product is ISoftDeletable, so SaveChangesAsync turns
           every delete into IsDeleted = true and the row is never physically
           removed. The database clause could never fire. The variant link
           below keeps SetNull, which leaves exactly one path. */
        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.ClientSetNull);

        builder.HasOne(i => i.ProductVariant)
            .WithMany()
            .HasForeignKey(i => i.ProductVariantId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
