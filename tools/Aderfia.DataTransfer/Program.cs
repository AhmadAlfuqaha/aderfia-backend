using Aderfia.Domain.Catalog;
using Aderfia.Domain.Ordering;
using Aderfia.Persistence;
using Microsoft.EntityFrameworkCore;

/* =============================================================================
   ADERFIA — one-time catalogue transfer, SQLite -> SQL Server
   -----------------------------------------------------------------------------
   Reads the live catalogue out of the development SQLite database and writes it
   into Azure SQL through the SAME EF model.

   WHY EF RATHER THAN A GENERATED INSERT SCRIPT
   AderfiaDbContext applies DateTimeOffsetToBinaryConverter, but only under
   `if (Database.IsSqlite())`. Every timestamp in the SQLite file is a packed
   integer — 1309187496793282560 is 2026-09-16 15:52:21 +00:00 — while SQL
   Server uses a native datetimeoffset. A hand-written export would copy the
   integer straight across and silently destroy every date in the catalogue.
   Reading through the SQLite context decodes them; writing through the SQL
   Server context stores them natively. The same applies to the Arabic text,
   the GUID keys and the decimal dimensions: no value is ever reformatted here.

   WHAT IT DOES NOT DO
   - never writes __EFMigrationsHistory (that is the schema's business)
   - never creates, alters or drops anything
   - never deletes a row
   - never carries orders, users, carts, wishlists or soft-deleted records

   Everything happens inside ONE transaction, so a failure at any stage leaves
   the target exactly as it was. Rows whose Id already exists are skipped, so a
   re-run after a partial success is safe.

   Usage:
     set ADERFIA_TARGET_CONNECTION=<Azure SQL connection string>
     dotnet run --project tools/Aderfia.DataTransfer             # dry run
     dotnet run --project tools/Aderfia.DataTransfer -- --apply  # writes

   The connection string is read from the environment and is never written to
   disk, logged, or echoed back.
   ============================================================================= */

var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
var sourcePath = ArgValue("--source") ?? "src/Aderfia.Api/aderfia.dev.db";

if (!File.Exists(sourcePath))
{
    Console.Error.WriteLine($"Source database not found: {Path.GetFullPath(sourcePath)}");
    return 1;
}

var target = Environment.GetEnvironmentVariable("ADERFIA_TARGET_CONNECTION");
if (apply && string.IsNullOrWhiteSpace(target))
{
    Console.Error.WriteLine("--apply needs ADERFIA_TARGET_CONNECTION set to the Azure SQL connection string.");
    return 1;
}

Console.WriteLine($"Source : {Path.GetFullPath(sourcePath)}");
Console.WriteLine($"Target : {(apply ? "Azure SQL (from ADERFIA_TARGET_CONNECTION)" : "none — DRY RUN, nothing will be written")}");
Console.WriteLine();

/* ---- Read ---------------------------------------------------------------- */

var sourceOptions = new DbContextOptionsBuilder<AderfiaDbContext>()
    .UseSqlite($"Data Source={sourcePath}")
    .Options;

await using var src = new AderfiaDbContext(sourceOptions);

// IgnoreQueryFilters throughout: the filters hide soft-deleted rows, and this
// tool decides what "live" means explicitly rather than inheriting it.
var shippingMethods = await src.ShippingMethods.AsNoTracking().ToListAsync();

var categories = await src.Categories.IgnoreQueryFilters()
    .Where(c => !c.IsDeleted).AsNoTracking().ToListAsync();

var collections = await src.Collections.IgnoreQueryFilters()
    .Where(c => !c.IsDeleted).AsNoTracking().ToListAsync();

var products = await src.Products.IgnoreQueryFilters()
    .Where(p => !p.IsDeleted).AsNoTracking().ToListAsync();

var productIds = products.Select(p => p.Id).ToHashSet();
var collectionIds = collections.Select(c => c.Id).ToHashSet();

var variants = await src.ProductVariants.IgnoreQueryFilters()
    .Where(v => productIds.Contains(v.ProductId)).AsNoTracking().ToListAsync();

/* CollectionId is a SHADOW property on ProductImage — the entity has no such
   member — so it is read through EF.Property rather than off the object. */
var images = await src.ProductImages.IgnoreQueryFilters()
    .Where(i => (i.ProductId != null && productIds.Contains(i.ProductId.Value))
             || (EF.Property<Guid?>(i, "CollectionId") != null
                 && collectionIds.Contains(EF.Property<Guid?>(i, "CollectionId")!.Value)))
    .AsNoTracking().ToListAsync();

var imageCollectionId = new Dictionary<Guid, Guid?>();
foreach (var img in images)
{
    var owner = await src.ProductImages.IgnoreQueryFilters()
        .Where(i => i.Id == img.Id)
        .Select(i => EF.Property<Guid?>(i, "CollectionId"))
        .FirstAsync();
    imageCollectionId[img.Id] = owner;
}

var materials = await src.ProductMaterials.IgnoreQueryFilters()
    .Where(m => productIds.Contains(m.ProductId)).AsNoTracking().ToListAsync();

var specifications = await src.ProductSpecifications.IgnoreQueryFilters()
    .Where(s => productIds.Contains(s.ProductId)).AsNoTracking().ToListAsync();

var badges = await src.ProductBadges.IgnoreQueryFilters()
    .Where(b => productIds.Contains(b.ProductId)).AsNoTracking().ToListAsync();

var options = await src.ProductOptions.IgnoreQueryFilters()
    .Where(o => productIds.Contains(o.ProductId)).AsNoTracking().ToListAsync();

var optionIds = options.Select(o => o.Id).ToHashSet();

var optionValues = await src.ProductOptionValues.IgnoreQueryFilters()
    .Where(v => optionIds.Contains(v.OptionId)).AsNoTracking().ToListAsync();

var variantIds = variants.Select(v => v.Id).ToHashSet();
var optionValueIds = optionValues.Select(v => v.Id).ToHashSet();

var variantOptionValues = await src.VariantOptionValues.IgnoreQueryFilters()
    .Where(v => variantIds.Contains(v.VariantId) && optionValueIds.Contains(v.OptionValueId))
    .AsNoTracking().ToListAsync();

// Both ends must survive, or the row points at something that was not carried.
var relations = await src.ProductRelations.IgnoreQueryFilters()
    .Where(r => productIds.Contains(r.ProductId) && productIds.Contains(r.RelatedProductId))
    .AsNoTracking().ToListAsync();

var collectionLinks = await src.Collections.IgnoreQueryFilters()
    .Where(c => collectionIds.Contains(c.Id))
    .Select(c => new { CollectionId = c.Id, ProductIds = c.Products.Select(p => p.Id).ToList() })
    .AsNoTracking().ToListAsync();

var links = collectionLinks
    .SelectMany(c => c.ProductIds.Where(productIds.Contains).Select(p => (c.CollectionId, ProductId: p)))
    .ToList();

Console.WriteLine("Selected for transfer (live rows only):");
Report("ShippingMethods", shippingMethods.Count);
Report("Categories", categories.Count);
Report("Collections", collections.Count);
Report("Products", products.Count);
Report("ProductVariants", variants.Count);
Report("ProductImages", images.Count);
Report("ProductMaterials", materials.Count);
Report("ProductSpecifications", specifications.Count);
Report("ProductBadges", badges.Count);
Report("ProductOptions", options.Count);
Report("ProductOptionValues", optionValues.Count);
Report("VariantOptionValues", variantOptionValues.Count);
Report("ProductRelations", relations.Count);
Report("CollectionProducts", links.Count);
Console.WriteLine();
Console.WriteLine("Excluded by design: Orders, OrderItems, Users, Carts, CartItems,");
Console.WriteLine("Addresses, WishlistItems, every soft-deleted row, __EFMigrationsHistory.");
Console.WriteLine();

if (!apply)
{
    Console.WriteLine("DRY RUN — nothing written. Re-run with --apply to transfer.");
    return 0;
}

/* ---- Write --------------------------------------------------------------- */

var targetOptions = new DbContextOptionsBuilder<AderfiaDbContext>()
    .UseSqlServer(target, sql => sql.EnableRetryOnFailure(maxRetryCount: 3))
    .Options;

await using var dst = new AderfiaDbContext(targetOptions);

/* EnableRetryOnFailure wraps calls in an execution strategy, and an explicit
   transaction has to be created inside one or EF refuses it. */
var strategy = dst.Database.CreateExecutionStrategy();

await strategy.ExecuteAsync(async () =>
{
    await using var tx = await dst.Database.BeginTransactionAsync();

    var existing = await ExistingIds(dst);

    /* Three foreign keys point "backwards" and cannot be satisfied at insert
       time: Products.DefaultVariantId -> ProductVariants, Categories.ImageId
       and Collections.HeroImageId -> ProductImages, whose own rows depend on
       Products and Collections. Each is nulled on the way in and restored by
       an UPDATE once every table is populated. The originals are kept here
       first, because the entity objects are about to be mutated. */
    var defaultVariants = products.ToDictionary(p => p.Id, p => p.DefaultVariantId);
    var categoryImages = categories.ToDictionary(c => c.Id, c => c.ImageId);
    var collectionHeroes = collections.ToDictionary(c => c.Id, c => c.HeroImageId);

    foreach (var p in products) p.DefaultVariantId = null;
    foreach (var c in categories) c.ImageId = null;
    foreach (var c in collections) c.HeroImageId = null;

    Add(dst, shippingMethods, s => s.Id, existing.ShippingMethods);
    Add(dst, categories, c => c.Id, existing.Categories);
    Add(dst, collections, c => c.Id, existing.Collections);
    Add(dst, products, p => p.Id, existing.Products);
    await dst.SaveChangesAsync();

    Add(dst, variants, v => v.Id, existing.ProductVariants);
    await dst.SaveChangesAsync();

    foreach (var img in images.Where(i => !existing.ProductImages.Contains(i.Id)))
    {
        var entry = dst.ProductImages.Add(img);
        entry.Property("CollectionId").CurrentValue = imageCollectionId[img.Id];
    }
    await dst.SaveChangesAsync();

    Add(dst, materials, m => m.Id, existing.ProductMaterials);
    Add(dst, specifications, s => s.Id, existing.ProductSpecifications);
    Add(dst, badges, b => b.Id, existing.ProductBadges);
    Add(dst, options, o => o.Id, existing.ProductOptions);
    await dst.SaveChangesAsync();

    Add(dst, optionValues, v => v.Id, existing.ProductOptionValues);
    await dst.SaveChangesAsync();

    foreach (var vov in variantOptionValues)
        dst.VariantOptionValues.Add(vov);
    foreach (var r in relations.Where(x => !existing.ProductRelations.Contains(x.Id)))
        dst.ProductRelations.Add(r);
    await dst.SaveChangesAsync();

    // The join table has no entity of its own; going through the navigation
    // lets EF write the row without naming its columns.
    foreach (var (collectionId, productId) in links)
    {
        var collection = await dst.Collections.IgnoreQueryFilters()
            .Include(c => c.Products).FirstAsync(c => c.Id == collectionId);
        if (collection.Products.Any(p => p.Id == productId)) continue;

        var product = await dst.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == productId);
        collection.Products.Add(product);
    }
    await dst.SaveChangesAsync();

    // Deferred foreign keys, now that every target row exists.
    foreach (var p in await dst.Products.IgnoreQueryFilters()
                 .Where(p => productIds.Contains(p.Id)).ToListAsync())
        p.DefaultVariantId = defaultVariants[p.Id];

    foreach (var c in await dst.Categories.IgnoreQueryFilters()
                 .Where(c => categoryImages.Keys.Contains(c.Id)).ToListAsync())
        c.ImageId = categoryImages[c.Id];

    foreach (var c in await dst.Collections.IgnoreQueryFilters()
                 .Where(c => collectionHeroes.Keys.Contains(c.Id)).ToListAsync())
        c.HeroImageId = collectionHeroes[c.Id];

    await dst.SaveChangesAsync();

    await tx.CommitAsync();
});

Console.WriteLine("Transfer committed.");
return 0;

/* ---- helpers -------------------------------------------------------------- */

string? ArgValue(string name)
{
    var i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static void Report(string table, int count) => Console.WriteLine($"  {table,-24}{count,6}");

static void Add<T>(AderfiaDbContext db, List<T> rows, Func<T, Guid> id, HashSet<Guid> already)
    where T : class
{
    foreach (var row in rows.Where(r => !already.Contains(id(r))))
        db.Set<T>().Add(row);
}

static async Task<ExistingKeys> ExistingIds(AderfiaDbContext db) => new(
    (await db.ShippingMethods.Select(x => x.Id).ToListAsync()).ToHashSet(),
    (await db.Categories.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).ToHashSet(),
    (await db.Collections.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).ToHashSet(),
    (await db.Products.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).ToHashSet(),
    (await db.ProductVariants.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).ToHashSet(),
    (await db.ProductImages.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).ToHashSet(),
    (await db.ProductMaterials.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).ToHashSet(),
    (await db.ProductSpecifications.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).ToHashSet(),
    (await db.ProductBadges.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).ToHashSet(),
    (await db.ProductOptions.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).ToHashSet(),
    (await db.ProductOptionValues.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).ToHashSet(),
    (await db.ProductRelations.IgnoreQueryFilters().Select(x => x.Id).ToListAsync()).ToHashSet());

internal sealed record ExistingKeys(
    HashSet<Guid> ShippingMethods,
    HashSet<Guid> Categories,
    HashSet<Guid> Collections,
    HashSet<Guid> Products,
    HashSet<Guid> ProductVariants,
    HashSet<Guid> ProductImages,
    HashSet<Guid> ProductMaterials,
    HashSet<Guid> ProductSpecifications,
    HashSet<Guid> ProductBadges,
    HashSet<Guid> ProductOptions,
    HashSet<Guid> ProductOptionValues,
    HashSet<Guid> ProductRelations);
