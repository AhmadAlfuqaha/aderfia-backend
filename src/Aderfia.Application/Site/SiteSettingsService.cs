using Aderfia.Application.Admin;
using Aderfia.Application.Catalog;
using Aderfia.Application.Common;
using Aderfia.Domain.Common;
using Aderfia.Domain.Site;
using Microsoft.EntityFrameworkCore;

namespace Aderfia.Application.Site;

/* ---- Wire shapes ---------------------------------------------------------- */

/// <summary>
/// The homepage hero card, as the storefront reads it.
/// <para>
/// <c>null</c> from the endpoint means "nothing configured" — the storefront
/// then falls back to the newest product, which is what it did before this
/// setting existed.
/// </para>
/// </summary>
public record HomeHeroDto
{
    /// <summary>The picture to show. Null when only a product was chosen.</summary>
    public ProductImageDto? Image { get; init; }

    /// <summary>Slug the card links to. Null when only an image was chosen.</summary>
    public string? ProductSlug { get; init; }
}

/// <summary>The same setting as the admin edits it.</summary>
public record AdminHomeHeroDto
{
    public AdminImageDto? Image { get; init; }
    public string? ProductId { get; init; }
    public string? ProductName { get; init; }
    public string? ProductSlug { get; init; }
}

/// <summary>Which product the card links to. Null clears it.</summary>
public record HomeHeroWriteRequest
{
    public string? ProductId { get; init; }
}

/* ---- Service -------------------------------------------------------------- */

public interface ISiteSettingsService
{
    Task<HomeHeroDto?> GetHomeHeroAsync(CancellationToken ct = default);
    Task<AdminHomeHeroDto> GetAdminHomeHeroAsync(CancellationToken ct = default);
    Task<AdminHomeHeroDto> SetHomeHeroProductAsync(HomeHeroWriteRequest request, CancellationToken ct = default);
    Task<AdminHomeHeroDto> SetHomeHeroImageAsync(
        Stream content, string fileName, string contentType, LocalizedTextDto altText, CancellationToken ct = default);
    Task<AdminHomeHeroDto> ClearHomeHeroImageAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads and writes the single <see cref="SiteSettings"/> row.
/// </summary>
public sealed class SiteSettingsService(
    IAderfiaDbContext db,
    IImageStorage storage,
    IImageUrlResolver urls,
    ICurrentLanguage language) : ISiteSettingsService
{
    /// <summary>Where a hero photograph is filed in blob storage.</summary>
    private const string HeroFolder = "site/home";

    public async Task<HomeHeroDto?> GetHomeHeroAsync(CancellationToken ct = default)
    {
        var settings = await db.SiteSettings
            .Include(s => s.HomeHeroImage)
            .Include(s => s.HomeHeroProduct)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (settings is null) return null;

        /* A product that has since been unpublished or soft-deleted must not
           keep a dead link on the homepage. The global query filter already
           hides deleted ones, so HomeHeroProduct comes back null for those;
           unpublished is checked here. */
        var product = settings.HomeHeroProduct is { IsPublished: true } p ? p : null;
        var image = settings.HomeHeroImage;

        if (image is null && product is null) return null;

        return new HomeHeroDto
        {
            Image = image?.ToDto(urls, language.Value),
            ProductSlug = product?.Slug
        };
    }

    public async Task<AdminHomeHeroDto> GetAdminHomeHeroAsync(CancellationToken ct = default)
    {
        var settings = await LoadAsync(track: false, ct);
        return ToAdminDto(settings);
    }

    public async Task<AdminHomeHeroDto> SetHomeHeroProductAsync(
        HomeHeroWriteRequest request,
        CancellationToken ct = default)
    {
        var settings = await LoadOrCreateAsync(ct);

        if (string.IsNullOrWhiteSpace(request.ProductId))
        {
            settings.HomeHeroProductId = null;
        }
        else
        {
            if (!Guid.TryParse(request.ProductId, out var productId))
                throw new BusinessRuleException("That product id is not a valid id.");

            var exists = await db.Products.AnyAsync(p => p.Id == productId, ct);
            if (!exists) throw NotFoundException.For("Product", request.ProductId);

            settings.HomeHeroProductId = productId;
        }

        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return await GetAdminHomeHeroAsync(ct);
    }

    public async Task<AdminHomeHeroDto> SetHomeHeroImageAsync(
        Stream content,
        string fileName,
        string contentType,
        LocalizedTextDto altText,
        CancellationToken ct = default)
    {
        var settings = await LoadOrCreateAsync(ct);
        var previous = settings.HomeHeroImage;

        var stored = await storage.SaveAsync(content, fileName, contentType, HeroFolder, ct);

        /* ProductId stays null: this picture belongs to the site, not to the
           product the card happens to link to. That is what keeps it out of
           the product's own gallery and off its PDP. */
        var image = new Domain.Catalog.ProductImage
        {
            StorageKey = stored.StorageKey,
            AltText = altText.ToDomain(),
            Role = Domain.Common.ImageRole.Lifestyle,
            Width = stored.Width,
            Height = stored.Height
        };

        db.ProductImages.Add(image);
        settings.HomeHeroImage = image;
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        /* Save BEFORE deleting the old one, so a failed write never leaves the
           row pointing at a file that is already gone. */
        await db.SaveChangesAsync(ct);

        if (previous is not null) await RemoveAsync(previous, ct);

        return await GetAdminHomeHeroAsync(ct);
    }

    public async Task<AdminHomeHeroDto> ClearHomeHeroImageAsync(CancellationToken ct = default)
    {
        var settings = await LoadOrCreateAsync(ct);
        var previous = settings.HomeHeroImage;

        if (previous is not null)
        {
            settings.HomeHeroImageId = null;
            settings.HomeHeroImage = null;
            settings.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            await RemoveAsync(previous, ct);
        }

        return await GetAdminHomeHeroAsync(ct);
    }

    /* ---- Helpers ---------------------------------------------------------- */

    private async Task<SiteSettings?> LoadAsync(bool track, CancellationToken ct)
    {
        var query = db.SiteSettings
            .Include(s => s.HomeHeroImage)
            .Include(s => s.HomeHeroProduct);

        return track
            ? await query.FirstOrDefaultAsync(ct)
            : await query.AsNoTracking().FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// The row is created on first write rather than seeded, so an existing
    /// database needs nothing but the table.
    /// </summary>
    private async Task<SiteSettings> LoadOrCreateAsync(CancellationToken ct)
    {
        var settings = await LoadAsync(track: true, ct);
        if (settings is not null) return settings;

        settings = new SiteSettings();
        db.SiteSettings.Add(settings);
        return settings;
    }

    private async Task RemoveAsync(Domain.Catalog.ProductImage image, CancellationToken ct)
    {
        await storage.DeleteAsync(image.StorageKey, ct);
        db.ProductImages.Remove(image);
        await db.SaveChangesAsync(ct);
    }

    private AdminHomeHeroDto ToAdminDto(SiteSettings? settings)
    {
        if (settings is null) return new AdminHomeHeroDto();

        var image = settings.HomeHeroImage;
        var product = settings.HomeHeroProduct;

        return new AdminHomeHeroDto
        {
            Image = image is null ? null : new AdminImageDto
            {
                Id = image.Id.ToString(),
                StorageKey = image.StorageKey,
                Url = urls.Resolve(image.StorageKey),
                Role = image.Role.ToWire(),
                AltText = LocalizedTextDto.From(image.AltText),
                Width = image.Width,
                Height = image.Height,
                Position = image.Position
            },
            ProductId = product?.Id.ToString(),
            ProductName = product?.Name.Get(language.Value),
            ProductSlug = product?.Slug
        };
    }
}
