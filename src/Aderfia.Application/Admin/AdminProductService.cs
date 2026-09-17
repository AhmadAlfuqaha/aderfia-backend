using Aderfia.Application.Common;
using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Aderfia.Application.Admin;

public interface IAdminProductService
{
    Task<IReadOnlyList<AdminProductRow>> ListAsync(string? search, CancellationToken ct = default);
    Task<AdminProductDto> GetForEditAsync(Guid id, CancellationToken ct = default);
    Task<AdminProductDto> CreateAsync(ProductWriteRequest request, CancellationToken ct = default);
    Task<AdminProductDto> UpdateAsync(Guid id, ProductWriteRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<AdminProductDto> SetPublishedAsync(Guid id, bool isPublished, CancellationToken ct = default);
    Task<AdminProductDto> UpdateVariantPriceAsync(Guid variantId, VariantPriceUpdate update, CancellationToken ct = default);
    Task<AdminProductDto> UpdateVariantStockAsync(Guid variantId, VariantStockUpdate update, CancellationToken ct = default);
}

/// <summary>
/// Write-side use cases for the catalogue.
/// <para>
/// Kept apart from <see cref="Catalog.ICatalogService"/> because the two have
/// opposite needs: the storefront reads published rows in ONE language with
/// no tracking, the admin edits everything in BOTH with full tracking.
/// </para>
/// </summary>
public sealed class AdminProductService(
    IAderfiaDbContext db,
    IImageUrlResolver urls,
    ISlugGenerator slugs,
    IDateTimeProvider clock) : IAdminProductService
{
    private const string Currency = Money.ShopCurrency;

    /* =====================================================================
       Reads
       ===================================================================== */

    public async Task<IReadOnlyList<AdminProductRow>> ListAsync(
        string? search,
        CancellationToken ct = default)
    {
        // Unlike the storefront, this deliberately includes UNPUBLISHED rows —
        // a draft you cannot see is a draft you cannot finish.
        var query = db.Products.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.Name.Ar, $"%{term}%") ||
                EF.Functions.Like(p.Name.En, $"%{term}%") ||
                EF.Functions.Like(p.Slug, $"%{term}%") ||
                EF.Functions.Like(p.Category.Name.Ar, $"%{term}%") ||
                EF.Functions.Like(p.Category.Name.En, $"%{term}%"));
        }

        var products = await query
            .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(ct);

        return products.Select(p =>
        {
            var thumbnail = p.Images.FirstOrDefault(i => i.Role == ImageRole.Primary)
                            ?? p.Images.OrderBy(i => i.Position).FirstOrDefault();

            /* Surfaces half-translated products in the table, so gaps are
               visible without opening every one. Only the fields a shopper
               actually reads count. */
            var needsTranslation =
                HalfWritten(p.Name) || HalfWritten(p.Tagline) || HalfWritten(p.Description);

            return new AdminProductRow
            {
                Id = p.Id.ToString(),
                Slug = p.Slug,
                Name = LocalizedTextDto.From(p.Name),
                CategoryName = LocalizedTextDto.From(p.Category?.Name ?? LocalizedText.Empty),
                ThumbnailUrl = thumbnail is null ? null : urls.Resolve(thumbnail.StorageKey),
                LowestPriceAmount = p.Variants.Count == 0 ? 0 : p.Variants.Min(v => v.Price.Amount),
                Currency = p.Variants.FirstOrDefault()?.Price.Currency ?? Currency,
                VariantCount = p.Variants.Count,
                // Made-to-order variants have no meaningful count to sum.
                TotalStock = p.Variants.Where(v => !v.Inventory.IsMadeToOrder).Sum(v => v.Inventory.Quantity),
                IsPublished = p.IsPublished,
                IsFeatured = p.IsFeatured,
                ImageCount = p.Images.Count,
                NeedsTranslation = needsTranslation,
                UpdatedAt = p.UpdatedAt ?? p.CreatedAt
            };
        }).ToList();
    }

    /// <summary>One language filled in and the other blank.</summary>
    private static bool HalfWritten(LocalizedText text) =>
        string.IsNullOrWhiteSpace(text.Ar) != string.IsNullOrWhiteSpace(text.En);

    public async Task<AdminProductDto> GetForEditAsync(Guid id, CancellationToken ct = default)
    {
        var product = await FullGraph().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw NotFoundException.For("Product", id.ToString());

        return ToAdminDto(product);
    }

    /* =====================================================================
       Writes
       ===================================================================== */

    public async Task<AdminProductDto> CreateAsync(ProductWriteRequest request, CancellationToken ct = default)
    {
        await EnsureCategoryExistsAsync(request.CategoryId, ct);

        if (request.Variants.Count == 0)
            throw new BusinessRuleException("A product needs at least one variant — that is what carries its price and stock.");

        var name = request.Name.ToDomain();
        if (name.IsEmpty) throw new BusinessRuleException("A product needs a name in at least one language.");

        var product = new Product
        {
            Name = name,
            Slug = await UniqueSlugAsync(request.Slug ?? SlugSource(name), null, ct),
            CategoryId = request.CategoryId
        };

        db.Products.Add(product);

        ApplyScalars(product, request);
        ApplySpecifications(product, request);
        ApplyMaterials(product, request);
        ApplyBadges(product, request);
        await ApplyCollectionsAsync(product, request.CollectionIds, ct);

        var optionValues = ApplyOptions(product, request);
        SyncVariants(product, request, optionValues);

        /* Two saves, and it must be two: Product.DefaultVariantId points at a
           ProductVariant whose own FK points back at the Product, so assigning
           it before both rows exist gives EF an insert cycle it cannot order. */
        await db.SaveChangesAsync(ct);

        AssignDefaultVariant(product, request);
        await db.SaveChangesAsync(ct);

        return await GetForEditAsync(product.Id, ct);
    }

    public async Task<AdminProductDto> UpdateAsync(
        Guid id,
        ProductWriteRequest request,
        CancellationToken ct = default)
    {
        await EnsureCategoryExistsAsync(request.CategoryId, ct);

        if (request.Variants.Count == 0)
            throw new BusinessRuleException("A product needs at least one variant.");

        var product = await FullGraph().FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw NotFoundException.For("Product", id.ToString());

        var name = request.Name.ToDomain();
        if (name.IsEmpty) throw new BusinessRuleException("A product needs a name in at least one language.");

        product.Name = name;
        product.Slug = await UniqueSlugAsync(request.Slug ?? SlugSource(name), product.Id, ct);
        product.CategoryId = request.CategoryId;

        ApplyScalars(product, request);
        ApplySpecifications(product, request);
        ApplyMaterials(product, request);
        ApplyBadges(product, request);
        await ApplyCollectionsAsync(product, request.CollectionIds, ct);

        /* Order matters. Removing a ProductOptionValue that a VariantOptionValue
           still points at severs a required relationship and EF refuses the
           save. So: drop unwanted variants, then tear down every remaining
           option link, and only then reconcile the option values themselves. */
        await RemoveDroppedVariantsAsync(product, request, ct);
        ClearVariantOptionLinks(product);

        var optionValues = ApplyOptions(product, request);
        SyncVariants(product, request, optionValues);

        /* Clear the pointer before saving. The variant it referenced may be
           one of the rows about to be deleted, and a dangling FK fails. */
        product.DefaultVariantId = null;
        await db.SaveChangesAsync(ct);

        AssignDefaultVariant(product, request);
        await db.SaveChangesAsync(ct);

        return await GetForEditAsync(product.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw NotFoundException.For("Product", id.ToString());

        /* Soft delete. Orders placed last year still reference this row, and
           the global query filter hides it from the storefront immediately. */
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AdminProductDto> SetPublishedAsync(
        Guid id,
        bool isPublished,
        CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw NotFoundException.For("Product", id.ToString());

        product.IsPublished = isPublished;
        // Stamp the first publication only, so "newest" can order honestly.
        if (isPublished) product.PublishedAt ??= clock.UtcNow;

        await db.SaveChangesAsync(ct);
        return await GetForEditAsync(id, ct);
    }

    public async Task<AdminProductDto> UpdateVariantPriceAsync(
        Guid variantId,
        VariantPriceUpdate update,
        CancellationToken ct = default)
    {
        var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct)
                      ?? throw NotFoundException.For("Variant", variantId.ToString());

        if (update.CompareAtAmount is { } compareAt && compareAt <= update.PriceAmount)
        {
            throw new BusinessRuleException(
                "The was-price must be higher than the current price, or the discount reads as an increase.");
        }

        variant.Price = new Money(update.PriceAmount, Currency);
        variant.CompareAtAmount = update.CompareAtAmount;

        await db.SaveChangesAsync(ct);
        return await GetForEditAsync(variant.ProductId, ct);
    }

    public async Task<AdminProductDto> UpdateVariantStockAsync(
        Guid variantId,
        VariantStockUpdate update,
        CancellationToken ct = default)
    {
        var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, ct)
                      ?? throw NotFoundException.For("Variant", variantId.ToString());

        variant.Inventory.Quantity = update.Quantity;
        if (update.IsMadeToOrder is { } madeToOrder) variant.Inventory.IsMadeToOrder = madeToOrder;
        if (update.LeadTime is not null) variant.Inventory.LeadTime = update.LeadTime.ToDomain();

        await db.SaveChangesAsync(ct);
        return await GetForEditAsync(variant.ProductId, ct);
    }

    /* =====================================================================
       Read mapping
       ===================================================================== */

    private AdminProductDto ToAdminDto(Product product) => new()
    {
        Id = product.Id.ToString(),
        Slug = product.Slug,
        Name = LocalizedTextDto.From(product.Name),
        Tagline = LocalizedTextDto.From(product.Tagline),
        Description = LocalizedTextDto.From(product.Description),
        Story = LocalizedTextDto.From(product.Story),
        CareInstructions = LocalizedTextDto.From(product.CareInstructions),
        CategoryId = product.CategoryId.ToString(),
        CollectionIds = product.Collections.Select(c => c.Id.ToString()).ToList(),
        Materials = product.Materials
            .OrderBy(m => m.Position)
            .Select(m => LocalizedTextDto.From(m.Name))
            .ToList(),
        Dimensions = ToInput(product.Dimensions),
        Options = product.Options
            .OrderBy(o => o.Position)
            .Select(o => new AdminOptionDto
            {
                Id = o.Id.ToString(),
                Name = LocalizedTextDto.From(o.Name),
                Position = o.Position,
                Values = o.Values.OrderBy(v => v.Position).Select(v => new AdminOptionValueDto
                {
                    Id = v.Id.ToString(),
                    Value = LocalizedTextDto.From(v.Value),
                    Swatch = v.Swatch
                }).ToList()
            })
            .ToList(),
        Variants = product.Variants
            .OrderBy(v => v.Position)
            .Select(v => new AdminVariantDto
            {
                Id = v.Id.ToString(),
                Sku = v.Sku,
                Name = LocalizedTextDto.From(v.Name),
                // Keyed on English, matching what the write side expects back.
                Selections = v.OptionValues
                    .Where(x => x.OptionValue?.Option is not null)
                    .OrderBy(x => x.OptionValue.Option.Position)
                    .ToDictionary(x => x.OptionValue.Option.Name.En, x => x.OptionValue.Value.En),
                PriceAmount = v.Price.Amount,
                CompareAtAmount = v.CompareAtAmount,
                Quantity = v.Inventory.Quantity,
                LowStockThreshold = v.Inventory.LowStockThreshold,
                IsMadeToOrder = v.Inventory.IsMadeToOrder,
                AllowBackorder = v.Inventory.AllowBackorder,
                LeadTime = LocalizedTextDto.From(v.Inventory.LeadTime),
                Dimensions = v.Dimensions is null ? null : ToInput(v.Dimensions),
                Position = v.Position,
                InventoryStatus = v.Inventory.Status.ToString()
            })
            .ToList(),
        DefaultVariantSku = product.Variants.FirstOrDefault(v => v.Id == product.DefaultVariantId)?.Sku,
        Specifications = product.Specifications
            .OrderBy(s => s.Position)
            .Select(s => new AdminSpecificationDto
            {
                Group = LocalizedTextDto.From(s.Group),
                Label = LocalizedTextDto.From(s.Label),
                Value = LocalizedTextDto.From(s.Value)
            })
            .ToList(),
        Badges = product.Badges
            .OrderBy(b => b.Position)
            .Select(b => new AdminBadgeDto
            {
                Label = LocalizedTextDto.From(b.Label),
                Tone = b.Tone.ToString().ToLowerInvariant()
            })
            .ToList(),
        Images = product.Images
            .OrderBy(i => i.Position)
            .Select(i => new AdminImageDto
            {
                Id = i.Id.ToString(),
                StorageKey = i.StorageKey,
                Url = urls.Resolve(i.StorageKey),
                Role = i.Role.ToString().ToLowerInvariant(),
                AltText = LocalizedTextDto.From(i.AltText),
                Width = i.Width,
                Height = i.Height,
                Position = i.Position
            })
            .ToList(),
        IsFeatured = product.IsFeatured,
        IsPublished = product.IsPublished,
        MetaTitle = LocalizedTextDto.From(product.MetaTitle),
        MetaDescription = LocalizedTextDto.From(product.MetaDescription)
    };

    private static DimensionsInput ToInput(Dimensions d) => new()
    {
        Unit = d.Unit,
        Width = d.Width,
        Height = d.Height,
        Depth = d.Depth,
        Diameter = d.Diameter,
        FrameWidth = d.FrameWidth,
        WeightKg = d.WeightKg
    };

    /* =====================================================================
       Write mapping
       ===================================================================== */

    private IQueryable<Product> FullGraph() =>
        db.Products
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Badges)
            .Include(p => p.Materials)
            .Include(p => p.Specifications)
            .Include(p => p.Collections)
            .Include(p => p.RelatedProducts)
            .Include(p => p.Options).ThenInclude(o => o.Values)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValues)
                .ThenInclude(vov => vov.OptionValue).ThenInclude(ov => ov.Option)
            .AsSplitQuery();

    private void ApplyScalars(Product product, ProductWriteRequest request)
    {
        product.Tagline = request.Tagline.ToDomain();
        product.Description = request.Description.ToDomain();
        product.Story = request.Story.ToDomain();
        product.CareInstructions = request.CareInstructions.ToDomain();
        product.IsFeatured = request.IsFeatured;
        product.MetaTitle = request.MetaTitle.ToDomain();
        product.MetaDescription = request.MetaDescription.ToDomain();
        product.Dimensions = ToDimensions(request.Dimensions) ?? new Dimensions();

        if (request.IsPublished && !product.IsPublished) product.PublishedAt ??= clock.UtcNow;
        product.IsPublished = request.IsPublished;
    }

    private void ApplySpecifications(Product product, ProductWriteRequest request)
    {
        // Replaced wholesale: specs have no external references, so rebuilding
        // is simpler and safer than diffing.
        db.ProductSpecifications.RemoveRange(product.Specifications);
        product.Specifications.Clear();

        var position = 0;
        foreach (var spec in request.Specifications)
        {
            var label = spec.Label.ToDomain();
            var value = spec.Value.ToDomain();
            if (label.IsEmpty || value.IsEmpty) continue;

            db.ProductSpecifications.Add(new ProductSpecification
            {
                ProductId = product.Id,
                Group = spec.Group.ToDomain(),
                Label = label,
                Value = value,
                Position = position++
            });
        }
    }

    private void ApplyMaterials(Product product, ProductWriteRequest request)
    {
        db.ProductMaterials.RemoveRange(product.Materials);
        product.Materials.Clear();

        var position = 0;
        foreach (var material in request.Materials)
        {
            var name = material.ToDomain();
            if (name.IsEmpty) continue;

            db.ProductMaterials.Add(new ProductMaterial
            {
                ProductId = product.Id,
                Name = name,
                Position = position++
            });
        }
    }

    private void ApplyBadges(Product product, ProductWriteRequest request)
    {
        db.ProductBadges.RemoveRange(product.Badges);
        product.Badges.Clear();

        var position = 0;
        foreach (var badge in request.Badges)
        {
            var label = badge.Label.ToDomain();
            if (label.IsEmpty) continue;

            db.ProductBadges.Add(new ProductBadge
            {
                ProductId = product.Id,
                Label = label,
                Tone = ParseTone(badge.Tone),
                Position = position++
            });
        }
    }

    private async Task ApplyCollectionsAsync(Product product, List<Guid> collectionIds, CancellationToken ct)
    {
        var wanted = collectionIds.Distinct().ToList();

        var collections = wanted.Count == 0
            ? []
            : await db.Collections.Where(c => wanted.Contains(c.Id)).ToListAsync(ct);

        product.Collections.Clear();
        foreach (var collection in collections) product.Collections.Add(collection);
    }

    /// <summary>
    /// Reconciles the option axes and returns a lookup keyed on the ENGLISH
    /// text, which is how variants address their selections.
    /// <para>
    /// Values are matched by that same English text so that editing an Arabic
    /// label does not silently recreate every option value — which would break
    /// the join rows pointing at them.
    /// </para>
    /// </summary>
    private Dictionary<(string Option, string Value), ProductOptionValue> ApplyOptions(
        Product product,
        ProductWriteRequest request)
    {
        var lookup = new Dictionary<(string, string), ProductOptionValue>();
        var keptOptions = new List<ProductOption>();
        var seenAxes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < request.Options.Count; i++)
        {
            var input = request.Options[i];
            var name = input.Name.ToDomain();
            if (name.IsEmpty) continue;

            var key = MatchKey(name);

            /* The unique index on (ProductId, Name) had to go when the name
               became two columns, so the rule is enforced here instead. */
            if (!seenAxes.Add(key))
                throw new BusinessRuleException($"The option '{key}' is listed twice.");

            var option = product.Options.FirstOrDefault(o =>
                string.Equals(MatchKey(o.Name), key, StringComparison.OrdinalIgnoreCase));

            if (option is null)
            {
                option = new ProductOption { ProductId = product.Id, Name = name, Position = i };
                db.ProductOptions.Add(option);
                product.Options.Add(option);
            }
            else
            {
                option.Name = name;
                option.Position = i;
            }

            keptOptions.Add(option);

            var keptValues = new List<ProductOptionValue>();
            for (var v = 0; v < input.Values.Count; v++)
            {
                var valueInput = input.Values[v];
                var text = valueInput.Value.ToDomain();
                if (text.IsEmpty) continue;

                var valueKey = MatchKey(text);

                var value = option.Values.FirstOrDefault(existing =>
                    string.Equals(MatchKey(existing.Value), valueKey, StringComparison.OrdinalIgnoreCase));

                if (value is null)
                {
                    value = new ProductOptionValue { OptionId = option.Id, Value = text, Position = v };
                    db.ProductOptionValues.Add(value);
                    option.Values.Add(value);
                }
                else
                {
                    value.Value = text;
                    value.Position = v;
                }

                value.Swatch = Blank(valueInput.Swatch);
                keptValues.Add(value);
                lookup[(key.ToLowerInvariant(), valueKey.ToLowerInvariant())] = value;
            }

            // Values the editor removed.
            foreach (var orphan in option.Values.Except(keptValues).ToList())
            {
                db.ProductOptionValues.Remove(orphan);
                option.Values.Remove(orphan);
            }
        }

        // Whole axes the editor removed.
        foreach (var orphan in product.Options.Except(keptOptions).ToList())
        {
            db.ProductOptions.Remove(orphan);
            product.Options.Remove(orphan);
        }

        return lookup;
    }

    /// <summary>
    /// The stable identity of a piece of localized text: English when written,
    /// Arabic otherwise. Selections address option values by this, so an
    /// English-only or Arabic-only catalogue both work.
    /// </summary>
    private static string MatchKey(LocalizedText text) =>
        string.IsNullOrWhiteSpace(text.En) ? text.Ar.Trim() : text.En.Trim();

    /// <summary>
    /// Deletes variants no longer present in the request, first detaching them
    /// from any carts — a withdrawn variant should leave a shopper's bag, and
    /// the FK is Restrict precisely so it cannot vanish silently underneath one.
    /// </summary>
    private async Task RemoveDroppedVariantsAsync(
        Product product,
        ProductWriteRequest request,
        CancellationToken ct)
    {
        var keptIds = request.Variants
            .Where(v => v.Id.HasValue)
            .Select(v => v.Id!.Value)
            .ToHashSet();

        var keptSkus = request.Variants
            .Select(v => v.Sku.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dropped = product.Variants
            .Where(v => !keptIds.Contains(v.Id) && !keptSkus.Contains(v.Sku))
            .ToList();

        if (dropped.Count == 0) return;

        var droppedIds = dropped.Select(v => v.Id).ToList();

        var affectedCartLines = await db.CartItems
            .Where(i => droppedIds.Contains(i.ProductVariantId))
            .ToListAsync(ct);

        db.CartItems.RemoveRange(affectedCartLines);

        foreach (var variant in dropped)
        {
            db.VariantOptionValues.RemoveRange(variant.OptionValues);
            variant.OptionValues.Clear();

            db.ProductVariants.Remove(variant);
            product.Variants.Remove(variant);
        }
    }

    private void SyncVariants(
        Product product,
        ProductWriteRequest request,
        Dictionary<(string, string), ProductOptionValue> optionValues)
    {
        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < request.Variants.Count; i++)
        {
            var input = request.Variants[i];
            var sku = input.Sku.Trim();

            if (!seenSkus.Add(sku))
                throw new BusinessRuleException($"SKU '{sku}' appears twice. Each variant needs its own.");

            if (input.CompareAtAmount is { } compareAt && compareAt <= input.PriceAmount)
                throw new BusinessRuleException($"The was-price on '{sku}' must exceed its current price.");

            var variant = input.Id is { } variantId
                ? product.Variants.FirstOrDefault(v => v.Id == variantId)
                : product.Variants.FirstOrDefault(v => string.Equals(v.Sku, sku, StringComparison.OrdinalIgnoreCase));

            if (variant is null)
            {
                variant = new ProductVariant
                {
                    ProductId = product.Id,
                    Sku = sku,
                    Price = new Money(input.PriceAmount, Currency)
                };

                // Explicit Add: this product may already be tracked, and a child
                // discovered on a tracked parent with a pre-set key is marked
                // Modified rather than Added.
                db.ProductVariants.Add(variant);
                product.Variants.Add(variant);
            }

            variant.Sku = sku;
            variant.Price = new Money(input.PriceAmount, Currency);
            variant.CompareAtAmount = input.CompareAtAmount;
            variant.Position = i;
            variant.Dimensions = ToDimensions(input.Dimensions);

            variant.Inventory.Quantity = input.Quantity;
            variant.Inventory.LowStockThreshold = input.LowStockThreshold;
            variant.Inventory.IsMadeToOrder = input.IsMadeToOrder;
            variant.Inventory.AllowBackorder = input.AllowBackorder;
            variant.Inventory.LeadTime = input.LeadTime?.ToDomain() ?? LocalizedText.Empty;

            // Links were already torn down by ClearVariantOptionLinks; this
            // rebuilds them from the submitted selections.
            var labelsAr = new List<string>();
            var labelsEn = new List<string>();

            foreach (var (optionName, chosen) in input.Selections)
            {
                if (string.IsNullOrWhiteSpace(chosen)) continue;

                if (!optionValues.TryGetValue(
                        (optionName.Trim().ToLowerInvariant(), chosen.Trim().ToLowerInvariant()),
                        out var value))
                {
                    throw new BusinessRuleException(
                        $"Variant '{sku}' selects {optionName} = '{chosen}', which is not one of that option's values.");
                }

                variant.OptionValues.Add(new VariantOptionValue
                {
                    VariantId = variant.Id,
                    OptionValueId = value.Id,
                    OptionValue = value
                });

                labelsAr.Add(value.Value.Get(Language.Ar));
                labelsEn.Add(value.Value.Get(Language.English));
            }

            /* A blank name is derived from the chosen values IN BOTH LANGUAGES,
               which is what the cart, the order and the WhatsApp message show. */
            var supplied = input.Name?.ToDomain() ?? LocalizedText.Empty;

            variant.Name = supplied.IsEmpty
                ? new LocalizedText(
                    labelsAr.Count > 0 ? string.Join(" / ", labelsAr) : product.Name.Get(Language.Ar),
                    labelsEn.Count > 0 ? string.Join(" / ", labelsEn) : product.Name.Get(Language.English))
                : supplied;
        }
    }

    /// <summary>
    /// Removes every variant-to-option-value link on the product, so option
    /// values can be freely added or removed without severing a required
    /// relationship. SyncVariants recreates them immediately afterwards.
    /// </summary>
    private void ClearVariantOptionLinks(Product product)
    {
        foreach (var variant in product.Variants.Where(v => v.OptionValues.Count > 0))
        {
            db.VariantOptionValues.RemoveRange(variant.OptionValues);
            variant.OptionValues.Clear();
        }
    }

    private static void AssignDefaultVariant(Product product, ProductWriteRequest request)
    {
        var chosen = request.DefaultVariantSku is { Length: > 0 } sku
            ? product.Variants.FirstOrDefault(v => string.Equals(v.Sku, sku, StringComparison.OrdinalIgnoreCase))
            : null;

        product.DefaultVariantId = (chosen ?? product.Variants.OrderBy(v => v.Position).FirstOrDefault())?.Id;
    }

    /* ---- Small helpers ------------------------------------------------- */

    private async Task EnsureCategoryExistsAsync(Guid categoryId, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
            throw new BusinessRuleException("Choose a category that exists.");
    }

    /// <summary>
    /// Slugs are generated from the ENGLISH name where there is one. An
    /// Arabic slug percent-encodes into unreadable noise the moment anyone
    /// shares the link, so Latin is the safer public identifier.
    /// </summary>
    private static string SlugSource(LocalizedText name) =>
        string.IsNullOrWhiteSpace(name.En) ? name.Ar : name.En;

    private async Task<string> UniqueSlugAsync(string source, Guid? excludeId, CancellationToken ct)
    {
        var baseSlug = slugs.Generate(source);
        // An Arabic-only name slugs to nothing, so fall back to a stable stem.
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = "product";

        var candidate = baseSlug;
        var suffix = 2;

        while (await db.Products.IgnoreQueryFilters()
                   .AnyAsync(p => p.Slug == candidate && (excludeId == null || p.Id != excludeId), ct))
        {
            candidate = $"{baseSlug}-{suffix++}";
        }

        return candidate;
    }

    private static Dimensions? ToDimensions(DimensionsInput? input)
    {
        if (input is null) return null;

        // All-empty means "no distinct size", not "a size of nothing".
        var hasAny = input.Width is not null || input.Height is not null || input.Depth is not null
                     || input.Diameter is not null || input.FrameWidth is not null
                     || input.WeightKg is not null;
        if (!hasAny) return null;

        return new Dimensions
        {
            Unit = string.IsNullOrWhiteSpace(input.Unit) ? "cm" : input.Unit.Trim(),
            Width = input.Width,
            Height = input.Height,
            Depth = input.Depth,
            Diameter = input.Diameter,
            FrameWidth = input.FrameWidth,
            WeightKg = input.WeightKg
        };
    }

    private static BadgeTone ParseTone(string tone) => tone.Trim().ToLowerInvariant() switch
    {
        "bestseller" => BadgeTone.Bestseller,
        "limited" => BadgeTone.Limited,
        "sale" => BadgeTone.Sale,
        "handmade" => BadgeTone.Handmade,
        _ => BadgeTone.New
    };

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
