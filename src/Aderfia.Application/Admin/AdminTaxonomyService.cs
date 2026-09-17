using Aderfia.Application.Common;
using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Aderfia.Application.Admin;

public interface IAdminTaxonomyService
{
    Task<IReadOnlyList<AdminCategoryDto>> ListCategoriesAsync(CancellationToken ct = default);
    Task<AdminCategoryDto> CreateCategoryAsync(CategoryWriteRequest request, CancellationToken ct = default);
    Task<AdminCategoryDto> UpdateCategoryAsync(Guid id, CategoryWriteRequest request, CancellationToken ct = default);
    Task DeleteCategoryAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<AdminCollectionDto>> ListCollectionsAsync(CancellationToken ct = default);
    Task<AdminCollectionDto> CreateCollectionAsync(CollectionWriteRequest request, CancellationToken ct = default);
    Task<AdminCollectionDto> UpdateCollectionAsync(Guid id, CollectionWriteRequest request, CancellationToken ct = default);
    Task DeleteCollectionAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Categories and collections, from the admin's side — both languages at once.
/// <para>
/// This is what makes the catalogue genuinely extensible: adding "Lighting"
/// here makes it appear in the navigation, the mega-menu, the homepage grid,
/// the shop filters and the footer with no code change anywhere, in whichever
/// language the visitor is reading.
/// </para>
/// </summary>
public sealed class AdminTaxonomyService(
    IAderfiaDbContext db,
    IImageUrlResolver urls,
    ISlugGenerator slugs) : IAdminTaxonomyService
{
    /* ---- Categories ------------------------------------------------------ */

    public async Task<IReadOnlyList<AdminCategoryDto>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var categories = await db.Categories
            .OrderBy(c => c.Position).ThenBy(c => c.Name.En)
            .Include(c => c.Image)
            .AsNoTracking()
            .ToListAsync(ct);

        var counts = await db.Products
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);

        return categories.Select(c => ToDto(c, counts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<AdminCategoryDto> CreateCategoryAsync(
        CategoryWriteRequest request,
        CancellationToken ct = default)
    {
        var name = request.Name.ToDomain();
        if (name.IsEmpty) throw new BusinessRuleException("A category needs a name in at least one language.");

        var category = new Category
        {
            Name = name,
            Slug = await UniqueSlugAsync(db.Categories, request.Slug ?? SlugSource(name), null, ct, "category")
        };

        db.Categories.Add(category);
        ApplyCategory(category, request);

        await db.SaveChangesAsync(ct);
        return ToDto(category, 0);
    }

    public async Task<AdminCategoryDto> UpdateCategoryAsync(
        Guid id,
        CategoryWriteRequest request,
        CancellationToken ct = default)
    {
        var category = await db.Categories.Include(c => c.Image)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw NotFoundException.For("Category", id.ToString());

        if (request.ParentId == id)
            throw new BusinessRuleException("A category cannot be its own parent.");

        var name = request.Name.ToDomain();
        if (name.IsEmpty) throw new BusinessRuleException("A category needs a name in at least one language.");

        category.Name = name;
        category.Slug = await UniqueSlugAsync(db.Categories, request.Slug ?? SlugSource(name), id, ct, "category");
        ApplyCategory(category, request);

        await db.SaveChangesAsync(ct);

        var count = await db.Products.CountAsync(p => p.CategoryId == id, ct);
        return ToDto(category, count);
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw NotFoundException.For("Category", id.ToString());

        /* Products point at a category with a Restrict FK, so deleting one
           that still holds stock would fail deep in the database. Catch it
           here and say something useful instead. */
        var productCount = await db.Products.CountAsync(p => p.CategoryId == id, ct);
        if (productCount > 0)
        {
            throw new BusinessRuleException(
                $"'{MatchKey(category.Name)}' still holds {productCount} product(s). Move them to another category first.",
                isConflict: true);
        }

        if (await db.Categories.AnyAsync(c => c.ParentId == id, ct))
        {
            throw new BusinessRuleException(
                $"'{MatchKey(category.Name)}' has sub-categories. Remove those first.", isConflict: true);
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    /* ---- Collections ----------------------------------------------------- */

    public async Task<IReadOnlyList<AdminCollectionDto>> ListCollectionsAsync(CancellationToken ct = default)
    {
        var collections = await db.Collections
            .OrderBy(c => c.Position).ThenBy(c => c.Title.En)
            .Include(c => c.HeroImage)
            .Include(c => c.Images)
            .Include(c => c.Products)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(ct);

        return collections.Select(ToDto).ToList();
    }

    public async Task<AdminCollectionDto> CreateCollectionAsync(
        CollectionWriteRequest request,
        CancellationToken ct = default)
    {
        var title = request.Title.ToDomain();
        if (title.IsEmpty) throw new BusinessRuleException("A collection needs a title in at least one language.");

        var collection = new Collection
        {
            Title = title,
            Slug = await UniqueSlugAsync(db.Collections, request.Slug ?? SlugSource(title), null, ct, "collection")
        };

        db.Collections.Add(collection);
        await ApplyCollectionAsync(collection, request, ct);

        await db.SaveChangesAsync(ct);
        return ToDto(collection);
    }

    public async Task<AdminCollectionDto> UpdateCollectionAsync(
        Guid id,
        CollectionWriteRequest request,
        CancellationToken ct = default)
    {
        var collection = await db.Collections
            .Include(c => c.HeroImage)
            .Include(c => c.Images)
            .Include(c => c.Products)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw NotFoundException.For("Collection", id.ToString());

        var title = request.Title.ToDomain();
        if (title.IsEmpty) throw new BusinessRuleException("A collection needs a title in at least one language.");

        collection.Title = title;
        collection.Slug = await UniqueSlugAsync(db.Collections, request.Slug ?? SlugSource(title), id, ct, "collection");
        await ApplyCollectionAsync(collection, request, ct);

        await db.SaveChangesAsync(ct);
        return ToDto(collection);
    }

    public async Task DeleteCollectionAsync(Guid id, CancellationToken ct = default)
    {
        var collection = await db.Collections
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw NotFoundException.For("Collection", id.ToString());

        // Membership is a join, so clearing it removes the links, not the products.
        collection.Products.Clear();

        db.Collections.Remove(collection);
        await db.SaveChangesAsync(ct);
    }

    /* ---- Mapping --------------------------------------------------------- */

    private AdminCategoryDto ToDto(Category category, int productCount) => new()
    {
        Id = category.Id.ToString(),
        Slug = category.Slug,
        Name = LocalizedTextDto.From(category.Name),
        Tagline = LocalizedTextDto.From(category.Tagline),
        Description = LocalizedTextDto.From(category.Description),
        Image = category.Image is null ? null : ToImageDto(category.Image),
        ParentId = category.ParentId?.ToString(),
        Position = category.Position,
        ProductCount = productCount,
        IsFeatured = category.IsFeatured
    };

    private AdminCollectionDto ToDto(Collection collection) => new()
    {
        Id = collection.Id.ToString(),
        Slug = collection.Slug,
        Title = LocalizedTextDto.From(collection.Title),
        Subtitle = LocalizedTextDto.From(collection.Subtitle),
        Description = LocalizedTextDto.From(collection.Description),
        HeroImage = collection.HeroImage is null ? null : ToImageDto(collection.HeroImage),
        ProductIds = collection.Products.Select(p => p.Id.ToString()).ToList(),
        Position = collection.Position,
        IsFeatured = collection.IsFeatured,
        IsPublished = collection.IsPublished,
        StartsAt = collection.StartsAt,
        EndsAt = collection.EndsAt
    };

    private AdminImageDto ToImageDto(ProductImage image) => new()
    {
        Id = image.Id.ToString(),
        StorageKey = image.StorageKey,
        Url = urls.Resolve(image.StorageKey),
        Role = image.Role.ToString().ToLowerInvariant(),
        AltText = LocalizedTextDto.From(image.AltText),
        Width = image.Width,
        Height = image.Height,
        Position = image.Position
    };

    /* ---- Helpers --------------------------------------------------------- */

    private static void ApplyCategory(Category category, CategoryWriteRequest request)
    {
        category.Tagline = request.Tagline.ToDomain();
        category.Description = request.Description.ToDomain();
        category.ParentId = request.ParentId;
        category.Position = request.Position;
        category.IsFeatured = request.IsFeatured;
    }

    private async Task ApplyCollectionAsync(
        Collection collection,
        CollectionWriteRequest request,
        CancellationToken ct)
    {
        collection.Subtitle = request.Subtitle.ToDomain();
        collection.Description = request.Description.ToDomain();
        collection.Position = request.Position;
        collection.IsFeatured = request.IsFeatured;
        collection.IsPublished = request.IsPublished;
        collection.StartsAt = request.StartsAt;
        collection.EndsAt = request.EndsAt;

        if (request.StartsAt is { } start && request.EndsAt is { } end && end < start)
            throw new BusinessRuleException("The end date cannot fall before the start date.");

        var wanted = request.ProductIds.Distinct().ToList();
        var products = wanted.Count == 0
            ? []
            : await db.Products.Where(p => wanted.Contains(p.Id)).ToListAsync(ct);

        collection.Products.Clear();
        foreach (var product in products) collection.Products.Add(product);
    }

    /// <summary>English where written, Arabic otherwise — for messages and slugs.</summary>
    private static string MatchKey(LocalizedText text) =>
        string.IsNullOrWhiteSpace(text.En) ? text.Ar.Trim() : text.En.Trim();

    private static string SlugSource(LocalizedText text) => MatchKey(text);

    /// <summary>
    /// Slugs are the public identifier, so uniqueness is a hard constraint.
    /// A clash gets a numeric suffix rather than an error the editor has to
    /// resolve by hand.
    /// </summary>
    private async Task<string> UniqueSlugAsync<T>(
        IQueryable<T> set,
        string source,
        Guid? excludeId,
        CancellationToken ct,
        string fallback)
        where T : Entity, ISluggable
    {
        var baseSlug = slugs.Generate(source);
        // An Arabic-only name slugs to nothing, so fall back to a stable stem.
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = fallback;

        var candidate = baseSlug;
        var suffix = 2;

        while (await set.IgnoreQueryFilters()
                   .AnyAsync(x => x.Slug == candidate && (excludeId == null || x.Id != excludeId), ct))
        {
            candidate = $"{baseSlug}-{suffix++}";
        }

        return candidate;
    }
}
