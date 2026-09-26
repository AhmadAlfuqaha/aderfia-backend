using Aderfia.Application.Catalog;
using Aderfia.Application.Common;
using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Aderfia.Application.Admin;

/// <summary>What an uploaded file is attached to.</summary>
public enum MediaOwner
{
    Product,
    Category,
    CollectionHero,
    CollectionStudy
}

public interface IAdminMediaService
{
    Task<AdminImageDto> UploadAsync(
        MediaOwner owner,
        Guid ownerId,
        Stream content,
        string fileName,
        string contentType,
        ImageAttachRequest details,
        CancellationToken ct = default);

    Task<IReadOnlyList<AdminImageDto>> ListForProductAsync(Guid productId, CancellationToken ct = default);
    Task<AdminImageDto> UpdateAsync(Guid imageId, ImageUpdateRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid imageId, CancellationToken ct = default);
    Task<IReadOnlyList<AdminImageDto>> ReorderAsync(Guid productId, List<Guid> orderedIds, CancellationToken ct = default);
}

/// <summary>
/// Image upload and arrangement.
/// <para>
/// The file goes to <see cref="IImageStorage"/>; the database records only a
/// storage key, its real pixel dimensions and a role. Dimensions matter more
/// than they look: the storefront reserves each image's box from that ratio
/// before the file arrives, so a wrong value causes visible layout shift.
/// </para>
/// </summary>
public sealed class AdminMediaService(
    IAderfiaDbContext db,
    IImageStorage storage,
    IImageUrlResolver urls) : IAdminMediaService
{
    public async Task<AdminImageDto> UploadAsync(
        MediaOwner owner,
        Guid ownerId,
        Stream content,
        string fileName,
        string contentType,
        ImageAttachRequest details,
        CancellationToken ct = default)
    {
        var folder = await ResolveFolderAsync(owner, ownerId, ct);
        var stored = await storage.SaveAsync(content, fileName, contentType, folder, ct);

        var image = new ProductImage
        {
            StorageKey = stored.StorageKey,
            AltText = details.AltText.ToDomain(),
            Role = ParseRole(details.Role),
            Width = stored.Width,
            Height = stored.Height,
            Position = details.Position
        };

        switch (owner)
        {
            case MediaOwner.Product:
                image.ProductId = ownerId;
                // Only one image may hold the primary or hover slot.
                await DemoteConflictingRoleAsync(ownerId, image.Role, ct);
                db.ProductImages.Add(image);
                break;

            case MediaOwner.Category:
            {
                var category = await db.Categories.Include(c => c.Image)
                    .FirstOrDefaultAsync(c => c.Id == ownerId, ct)
                    ?? throw NotFoundException.For("Category", ownerId.ToString());

                var previous = category.Image;
                db.ProductImages.Add(image);
                category.Image = image;

                // A category holds exactly one picture; replacing it should not
                // leave the old file orphaned on disk.
                if (previous is not null) await RemoveFileAndRowAsync(previous, ct);
                break;
            }

            case MediaOwner.CollectionHero:
            {
                var collection = await db.Collections.Include(c => c.HeroImage)
                    .FirstOrDefaultAsync(c => c.Id == ownerId, ct)
                    ?? throw NotFoundException.For("Collection", ownerId.ToString());

                var previous = collection.HeroImage;
                db.ProductImages.Add(image);
                collection.HeroImage = image;

                if (previous is not null) await RemoveFileAndRowAsync(previous, ct);
                break;
            }

            case MediaOwner.CollectionStudy:
            {
                var collection = await db.Collections.Include(c => c.Images)
                    .FirstOrDefaultAsync(c => c.Id == ownerId, ct)
                    ?? throw NotFoundException.For("Collection", ownerId.ToString());

                image.Role = ImageRole.Lifestyle;
                image.Position = collection.Images.Count;
                db.ProductImages.Add(image);
                collection.Images.Add(image);
                break;
            }
        }

        await db.SaveChangesAsync(ct);
        return ToDto(image);
    }

    public async Task<IReadOnlyList<AdminImageDto>> ListForProductAsync(
        Guid productId,
        CancellationToken ct = default)
    {
        var images = await db.ProductImages
            .Where(i => i.ProductId == productId)
            .OrderBy(i => i.Position)
            .AsNoTracking()
            .ToListAsync(ct);

        return images.Select(ToDto).ToList();
    }

    public async Task<AdminImageDto> UpdateAsync(
        Guid imageId,
        ImageUpdateRequest request,
        CancellationToken ct = default)
    {
        var image = await db.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId, ct)
                    ?? throw NotFoundException.For("Image", imageId.ToString());

        if (request.AltText is not null) image.AltText = request.AltText.ToDomain();
        if (request.Position is { } position) image.Position = position;

        if (request.Role is { Length: > 0 } role)
        {
            var parsed = ParseRole(role);
            if (parsed != image.Role && image.ProductId is { } productId)
                await DemoteConflictingRoleAsync(productId, parsed, ct, exceptImageId: imageId);

            image.Role = parsed;
        }

        await db.SaveChangesAsync(ct);
        return ToDto(image);
    }

    public async Task DeleteAsync(Guid imageId, CancellationToken ct = default)
    {
        var image = await db.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId, ct)
                    ?? throw NotFoundException.For("Image", imageId.ToString());

        await RemoveFileAndRowAsync(image, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AdminImageDto>> ReorderAsync(
        Guid productId,
        List<Guid> orderedIds,
        CancellationToken ct = default)
    {
        var images = await db.ProductImages
            .Where(i => i.ProductId == productId)
            .ToListAsync(ct);

        for (var i = 0; i < orderedIds.Count; i++)
        {
            var image = images.FirstOrDefault(x => x.Id == orderedIds[i]);
            if (image is not null) image.Position = i;
        }

        await db.SaveChangesAsync(ct);
        return await ListForProductAsync(productId, ct);
    }

    /* ---- Helpers --------------------------------------------------------- */

    /// <summary>
    /// Primary and hover are single-occupancy slots — the card reads exactly
    /// one of each. Assigning a new one moves the previous holder to gallery
    /// rather than silently leaving two.
    /// </summary>
    private async Task DemoteConflictingRoleAsync(
        Guid productId,
        ImageRole role,
        CancellationToken ct,
        Guid? exceptImageId = null)
    {
        if (role is not (ImageRole.Primary or ImageRole.Hover)) return;

        var existing = await db.ProductImages
            .Where(i => i.ProductId == productId && i.Role == role)
            .ToListAsync(ct);

        foreach (var image in existing.Where(i => i.Id != exceptImageId))
            image.Role = ImageRole.Gallery;
    }

    private async Task RemoveFileAndRowAsync(ProductImage image, CancellationToken ct)
    {
        // Clear references first, or the FK stops the row from going.
        var category = await db.Categories.FirstOrDefaultAsync(c => c.ImageId == image.Id, ct);
        if (category is not null) category.ImageId = null;

        var collection = await db.Collections.FirstOrDefaultAsync(c => c.HeroImageId == image.Id, ct);
        if (collection is not null) collection.HeroImageId = null;

        // The homepage hero card points at an image too, and its FK is
        // NoAction like the two above — so it has to be released the same way.
        var settings = await db.SiteSettings.FirstOrDefaultAsync(s => s.HomeHeroImageId == image.Id, ct);
        if (settings is not null) settings.HomeHeroImageId = null;

        await storage.DeleteAsync(image.StorageKey, ct);
        db.ProductImages.Remove(image);
    }

    private async Task<string> ResolveFolderAsync(MediaOwner owner, Guid ownerId, CancellationToken ct) =>
        owner switch
        {
            MediaOwner.Product =>
                $"products/{await SlugOfAsync(db.Products.Where(p => p.Id == ownerId).Select(p => p.Slug), "Product", ownerId, ct)}",

            MediaOwner.Category =>
                $"categories/{await SlugOfAsync(db.Categories.Where(c => c.Id == ownerId).Select(c => c.Slug), "Category", ownerId, ct)}",

            _ =>
                $"collections/{await SlugOfAsync(db.Collections.Where(c => c.Id == ownerId).Select(c => c.Slug), "Collection", ownerId, ct)}"
        };

    private static async Task<string> SlugOfAsync(
        IQueryable<string> query,
        string entity,
        Guid id,
        CancellationToken ct)
        => await query.FirstOrDefaultAsync(ct) ?? throw NotFoundException.For(entity, id.ToString());

    private AdminImageDto ToDto(ProductImage image) => new()
    {
        Id = image.Id.ToString(),
        StorageKey = image.StorageKey,
        Url = urls.Resolve(image.StorageKey),
        Role = image.Role.ToWire(),
        AltText = LocalizedTextDto.From(image.AltText),
        Width = image.Width,
        Height = image.Height,
        Position = image.Position
    };

    private static ImageRole ParseRole(string role) => role.Trim().ToLowerInvariant() switch
    {
        "primary" => ImageRole.Primary,
        "hover" => ImageRole.Hover,
        "detail" => ImageRole.Detail,
        "lifestyle" => ImageRole.Lifestyle,
        _ => ImageRole.Gallery
    };
}
