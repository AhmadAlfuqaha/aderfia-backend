using Aderfia.Domain.Catalog;
using Aderfia.Domain.Common;
using Aderfia.Domain.Ordering;
using Microsoft.EntityFrameworkCore;

namespace Aderfia.Persistence.Seed;

/// <summary>
/// Development seed data, written in both languages.
/// <para>
/// The Arabic is real copy, not a machine gloss of the English — the two read
/// as the same brand rather than one being a translation of the other. It is
/// a starting point: every word here is editable in the admin.
/// </para>
/// <para>
/// Idempotent: does nothing if products already exist.
/// </para>
/// </summary>
public static class CatalogSeeder
{
    private const string Currency = Money.ShopCurrency;

    private static Money Price(decimal major) => new((long)(major * 100), Currency);

    /// <summary>Arabic first, matching the store's default language.</summary>
    private static LocalizedText T(string ar, string en) => new(ar, en);

    public static async Task SeedAsync(AderfiaDbContext db, CancellationToken ct = default)
    {
        if (await db.Products.IgnoreQueryFilters().AnyAsync(ct)) return;

        var now = DateTimeOffset.UtcNow;

        /* ---- Shipping methods -------------------------------------------
           Retained though checkout no longer picks one: delivery is agreed
           over WhatsApp. They stay so a rate table can return without a
           migration. */
        if (!await db.ShippingMethods.AnyAsync(ct))
        {
            db.ShippingMethods.AddRange(
                new ShippingMethod
                {
                    Name = T("توصيل عادي", "Standard delivery"),
                    Description = T("متتبَّع، مع توقيع عند الاستلام", "Tracked, signature on arrival"),
                    Price = Price(20),
                    Estimate = T("5–8 أيام عمل", "5–8 working days"),
                    FreeAboveSubtotal = 42500,
                    Position = 0
                },
                new ShippingMethod
                {
                    Name = T("توصيل سريع", "Express delivery"),
                    Description = T("أولوية في التجهيز والشحن", "Priority handling and dispatch"),
                    Price = Price(30),
                    Estimate = T("2–3 أيام عمل", "2–3 working days"),
                    Position = 1
                },
                new ShippingMethod
                {
                    Name = T("توصيل إلى الغرفة", "Room of choice"),
                    Description = T("شخصان، مع فكّ التغليف والتركيب", "Two-person delivery, unpacked and placed"),
                    Price = Price(85),
                    Estimate = T("7–12 يوم عمل", "7–12 working days"),
                    Position = 2
                });
        }

        /* ---- Categories -------------------------------------------------- */
        var categories = new[]
        {
            NewCategory("mirrors",
                T("المرايا", "Mirrors"),
                T("ضوءٌ في إطارٍ من الخشب الصلب", "Light, framed in solid timber"),
                T("المرآة لا تعكس فحسب — بل تستعير الضوء من جدارٍ وتُعيره لآخر. مراياتنا تُقَصّ من ألواحٍ مفردة وتُشكَّل باليد.",
                  "A mirror does more than reflect — it borrows light from one wall and lends it to another."),
                0),

            NewCategory("wall-clocks",
                T("ساعات الحائط", "Wall Clocks"),
                T("الوقت يُروى بهدوء", "Time, told quietly"),
                T("حركاتٌ صامتة داخل أقراصٍ مخروطة ومنحوتة، مصمَّمة لتُقرأ من طرف الغرفة.",
                  "Silent movements set into turned and carved faces, designed to be read across a room."),
                1),

            NewCategory("shelves",
                T("الأرفف", "Shelves"),
                T("بناءٌ يكاد يختفي", "Structure that disappears"),
                T("أرففٌ جدارية ومستندة، مصنوعة من خشبٍ سميكٍ صادق، بتثبيتاتٍ مخفيّة.",
                  "Wall-mounted and leaning storage built from thick, honest stock."),
                2),

            NewCategory("decorative",
                T("قطع زخرفية", "Decorative Pieces"),
                T("قطعٌ لها وزنها ومعناها", "Objects with weight and intent"),
                T("أوعيةٌ مخروطة ومنحوتاتٌ وأدوات تقديم. أشياء صغيرة، تُصنع ببطء.",
                  "Turned vessels, carved sculpture and serving pieces. Small things, made slowly."),
                3)
        };
        AttachCategoryImages(categories);
        db.Categories.AddRange(categories);
        var byCategory = categories.ToDictionary(c => c.Slug);

        /* ---- Collections -------------------------------------------------- */
        var collections = new[]
        {
            NewCollection("quiet-rooms",
                T("غرفٌ هادئة", "Quiet Rooms"),
                T("دردارٌ فاتح وعروقٌ مفتوحة", "Pale ash and open grain"),
                T("قطعٌ اختيرت لغرفٍ لا تطلب الكثير. أخشابٌ فاتحة وحوافّ ناعمة.",
                  "Pieces chosen for rooms that ask for very little."),
                0, featured: true),

            NewCollection("the-dark-grain",
                T("العرق الداكن", "The Dark Grain"),
                T("جوزٌ ودخانٌ وعمق", "Walnut, smoke and depth"),
                T("أعمق تشطيباتنا، مجموعةً في مكانٍ واحد.", "Our deepest finishes, gathered."),
                1, featured: true),

            NewCollection("the-entrance",
                T("المدخل", "The Entrance"),
                T("انطباعٌ أوّل مدروس", "First impressions, considered"),
                T("القطع التي تجعل العتبة تعمل: مرآةٌ على الارتفاع الصحيح، ورفٌّ لما تحمله.",
                  "The pieces that make a threshold work."),
                2, featured: true)
        };
        AttachCollectionImages(collections);
        db.Collections.AddRange(collections);
        var byCollection = collections.ToDictionary(c => c.Slug);

        /* ---- Finishes, shared across products ----------------------------- */
        var oak = ("Natural Oak", T("بلوط طبيعي", "Natural Oak"), "#C8A97E");
        var walnut = ("European Walnut", T("جوز أوروبي", "European Walnut"), "#6B4A2F");
        var blackOak = ("Blackened Oak", T("بلوط أسود", "Blackened Oak"), "#3A322A");
        var smokedOak = ("Smoked Oak", T("بلوط مدخّن", "Smoked Oak"), "#7A5F42");
        var paleAsh = ("Pale Ash", T("دردار فاتح", "Pale Ash"), "#DCCDB4");
        var teak = ("Teak", T("ساج", "Teak"), "#A9793F");
        var cherry = ("Cherry", T("كرز", "Cherry"), "#8B4A34");

        /* ---- Products ------------------------------------------------------ */
        var products = new List<Product>
        {
            BuildProduct("halo-round-mirror",
                T("مرآة هالة المستديرة", "Halo Round Mirror"),
                T("دائرةٌ كاملة بحوافّ ناعمة", "A full circle, softly bevelled"),
                T("حلقةٌ متّصلة من الخشب الصلب، مثنيّة بالبخار لا مُوصَّلة، فيجري العرق دون انقطاع حول الإطار كلّه.",
                  "A single continuous ring of solid timber, steam-bent rather than jointed, so the grain runs unbroken around the whole frame."),
                T("ثني حلقةٍ بهذا الحجم دون وصلةٍ ظاهرة يستغرق ثلاثة أيام في المكبس ورابعاً كي تستقرّ.",
                  "Bending a ring this size without a visible joint takes three days in the press and a fourth to settle."),
                byCategory["mirrors"], [byCollection["quiet-rooms"], byCollection["the-entrance"]],
                490m,
                [oak, walnut, blackOak],
                [(T("قطر 60", "Ø 60"), 0m, Dim(diameter: 60, depth: 4, weight: 5.2m)),
                 (T("قطر 80", "Ø 80"), 170m, Dim(diameter: 80, depth: 4, weight: 8.1m)),
                 (T("قطر 100", "Ø 100"), 370m, Dim(diameter: 100, depth: 4.5m, weight: 12.4m))],
                [T("بلوط صلب", "Solid Oak"), T("زجاج مرايا", "Mirror glass"), T("تشطيب زيتي طبيعي", "Natural oil finish")],
                [(T("التصنيع", "Construction"), T("الإطار", "Frame"), T("خشب صلب مثنيّ بالبخار، بلا وصلات", "Steam-bent solid timber, jointless")),
                 (T("التصنيع", "Construction"), T("الزجاج", "Glass"), T("زجاج مفضَّض 4 مم خالٍ من النحاس", "4mm silvered float glass, copper-free")),
                 (T("التثبيت", "Mounting"), T("طريقة التعليق", "Fixing"), T("سكّة تعليق مخفيّة، مرفقة", "Concealed French cleat, included")),
                 (T("العناية", "Care"), T("التنظيف", "Cleaning"), T("قماش ميكروفايبر جاف، وإعادة تزييت الإطار سنوياً", "Dry microfibre; re-oil the frame annually"))],
                (T("الأكثر مبيعاً", "Bestseller"), BadgeTone.Bestseller),
                featured: true, stock: 14, rating: (4.8, 64), publishedDaysAgo: 210, now: now),

            BuildProduct("arc-arched-mirror",
                T("مرآة قوس", "Arc Arched Mirror"),
                T("بابٌ ليس بباب", "A doorway that isn't one"),
                T("القوس مستعارٌ من العمارة لا من الأثاث — طويلٌ بما يكفي ليُقرأ كنافذة.",
                  "The arched profile borrows from architecture rather than furniture — tall enough to read as a window."),
                T("نصف القطر هو نفسه قوس الباب الشمالي في الورشة.",
                  "The radius is the same as the arch in the workshop's north door."),
                byCategory["mirrors"], [byCollection["the-entrance"], byCollection["the-dark-grain"]],
                630m,
                [oak, walnut, smokedOak],
                [(T("60 × 110", "60 × 110"), 0m, Dim(width: 60, height: 110, depth: 4, weight: 11m)),
                 (T("75 × 150", "75 × 150"), 270m, Dim(width: 75, height: 150, depth: 4.5m, weight: 17.5m))],
                [T("بلوط صلب", "Solid Oak"), T("زجاج مرايا", "Mirror glass"), T("تشطيب زيتي طبيعي", "Natural oil finish")],
                [(T("التصنيع", "Construction"), T("الإطار", "Frame"), T("قوس مُصفَّح ومُشكَّل يدوياً", "Laminated and hand-shaped arch")),
                 (T("التثبيت", "Mounting"), T("طريقة التعليق", "Fixing"), T("يُعلَّق على الجدار أو يُستند إليه", "Wall-mounted or leaning")),
                 (T("التثبيت", "Mounting"), T("المرفقات", "Included"), T("سكّة تعليق وحزام أمان", "Cleat and anti-tip strap"))],
                (T("جديد", "New"), BadgeTone.New),
                featured: true, stock: 6, rating: (4.9, 31), publishedDaysAgo: 24, now: now),

            BuildProduct("solstice-wall-clock",
                T("ساعة الانقلاب", "Solstice Wall Clock"),
                T("وجهٌ مخروطٌ من لوحٍ واحد", "A face turned from one board"),
                T("القرص لوحٌ مفرد من خشبٍ مقطوعٍ ربعياً، مخروطٌ على هيئة صحنٍ ضحل فتستقرّ العقارب تحت الحافة قليلاً.",
                  "The dial is a single disc of quarter-sawn timber, turned to a shallow dish so the hands sit slightly below the rim."),
                T("العلامات المُطعَّمة تأخذ بعد ظهيرةٍ كاملة لكل ساعة، وستبقى بعد أن نصير نحن أقدم منها.",
                  "Inlaid markers take an afternoon per clock and will still be there when the piece is older than we are."),
                byCategory["wall-clocks"], [byCollection["quiet-rooms"], byCollection["the-entrance"]],
                240m,
                [oak, walnut, paleAsh],
                [(T("قطر 30", "Ø 30"), 0m, Dim(diameter: 30, depth: 4.5m, weight: 1.4m)),
                 (T("قطر 45", "Ø 45"), 85m, Dim(diameter: 45, depth: 5, weight: 2.6m))],
                [T("بلوط صلب", "Solid Oak"), T("عقارب نحاسية", "Brass hands"), T("تشطيب زيتي طبيعي", "Natural oil finish")],
                [(T("الحركة", "Movement"), T("النوع", "Type"), T("حركة كوارتز صامتة ومنسابة", "Silent continuous sweep, quartz")),
                 (T("الحركة", "Movement"), T("الطاقة", "Power"), T("بطارية AA واحدة، مرفقة", "1 × AA battery, included")),
                 (T("التصنيع", "Construction"), T("القرص", "Dial"), T("خشب صلب مخروط بعلامات مطعَّمة", "Turned solid timber, inlaid markers")),
                 (T("التثبيت", "Mounting"), T("طريقة التعليق", "Fixing"), T("صفيحة تعليق بمسمار واحد", "Keyhole plate, single screw"))],
                (T("الأكثر مبيعاً", "Bestseller"), BadgeTone.Bestseller),
                featured: true, stock: 31, rating: (4.9, 118), publishedDaysAgo: 265, now: now),

            BuildProduct("corona-sunburst-clock",
                T("ساعة إكليل الشمس", "Corona Sunburst Clock"),
                T("ستون شعاعاً تُقَصّ وتُركَّب يدوياً", "Sixty rays, cut and set by hand"),
                T("أشعّةٌ متناوبة الطول تنطلق من مركزٍ مخروط، كلٌّ منها يُقَصّ ويُشذَّب ويُركَّب على حدة.",
                  "Alternating long and short spokes radiate from a turned centre, each individually cut, tapered and set into the hub."),
                LocalizedText.Empty,
                byCategory["wall-clocks"], [byCollection["the-dark-grain"]],
                555m,
                [walnut, teak, smokedOak],
                [],
                [T("جوز صلب", "Solid Walnut"), T("أشعّة من خشب الساج", "Teak spokes"), T("عقارب نحاسية", "Brass hands")],
                [(T("الحركة", "Movement"), T("النوع", "Type"), T("حركة كوارتز صامتة ومنسابة", "Silent continuous sweep, quartz")),
                 (T("التصنيع", "Construction"), T("الأشعّة", "Spokes"), T("60 شعاعاً مقصوصاً ومشذَّباً يدوياً", "60 hand-cut and tapered rays")),
                 (T("مدّة التنفيذ", "Lead time"), T("الإنتاج", "Production"), T("يُصنع حسب الطلب، 2–3 أسابيع", "Made to order, 2–3 weeks"))],
                (T("إصدار محدود", "Limited"), BadgeTone.Limited),
                featured: true, stock: 0, madeToOrder: true,
                leadTime: T("يُشحن خلال 2–3 أسابيع", "Ships in 2–3 weeks"),
                rating: (4.8, 19), publishedDaysAgo: 60, now: now,
                dimensions: Dim(diameter: 78, depth: 6, weight: 4.8m)),

            BuildProduct("ledge-floating-shelf",
                T("رفّ ليدج المعلَّق", "Ledge Floating Shelf"),
                T("أربعون مليمتراً من الخشب الصلب", "Forty millimetres of solid stock"),
                T("لوحٌ سميك مفرد بمجرى محفور في أسفله ينزلق على قضيبٍ فولاذي مخفيّ.",
                  "A single thick board with a routed channel on the underside that drops onto a concealed steel bar."),
                T("معظم الأرفف المعلّقة صناديق مجوّفة ملفوفة بقشرة. رفّنا قطعةٌ واحدة من الخشب.",
                  "Most floating shelves are hollow boxes with a veneer wrapped round them. Ours is one piece of timber."),
                byCategory["shelves"], [byCollection["quiet-rooms"], byCollection["the-entrance"]],
                220m,
                [oak, walnut, blackOak],
                [(T("60 سم", "60 cm"), 0m, Dim(width: 60, height: 4, depth: 20, weight: 3.1m)),
                 (T("90 سم", "90 cm"), 65m, Dim(width: 90, height: 4, depth: 22, weight: 5m)),
                 (T("120 سم", "120 cm"), 135m, Dim(width: 120, height: 4, depth: 24, weight: 7.4m))],
                [T("بلوط صلب", "Solid Oak"), T("قضيب تثبيت فولاذي", "Steel mounting bar"), T("تشطيب زيتي طبيعي", "Natural oil finish")],
                [(T("التصنيع", "Construction"), T("اللوح", "Board"), T("خشب صلب 40 مم، قطعة واحدة", "40mm solid timber, single piece")),
                 (T("التحميل", "Load"), T("الطاقة الاستيعابية", "Capacity"), T("حتى 25 كغم على الجدران المبنية، موزَّعة", "Up to 25 kg on masonry, evenly distributed")),
                 (T("العناية", "Care"), T("التنظيف", "Cleaning"), T("قماش رطب، وإعادة تزييت سنوياً", "Damp cloth; re-oil annually"))],
                (T("الأكثر مبيعاً", "Bestseller"), BadgeTone.Bestseller),
                featured: true, stock: 52, rating: (4.8, 143), publishedDaysAgo: 290, now: now),

            BuildProduct("ascent-ladder-shelf",
                T("رفّ أسنت السُلَّمي", "Ascent Ladder Shelf"),
                T("يستند، يحمل، وينتقل معك", "Leans, holds, moves with you"),
                T("رفٌّ مستند بأربع طبقات، بقوائم مشذَّبة وأعماقٍ متدرّجة — الأعمق عند الأرض والأضيق في الأعلى.",
                  "A four-tier leaning shelf with tapered uprights and graduated depths — deepest at the floor, shallowest at the top."),
                LocalizedText.Empty,
                byCategory["shelves"], [byCollection["the-entrance"]],
                580m,
                [oak, walnut, smokedOak],
                [],
                [T("بلوط صلب", "Solid Oak"), T("تشطيب زيتي طبيعي", "Natural oil finish"), T("حزام جلدي للجدار", "Leather wall strap")],
                [(T("التصنيع", "Construction"), T("التعشيق", "Joinery"), T("نقر ولسان، مثبَّت بالوتد", "Mortise and tenon, draw-bored")),
                 (T("التحميل", "Load"), T("الطاقة الاستيعابية", "Capacity"), T("حتى 12 كغم لكل طبقة", "Up to 12 kg per tier")),
                 (T("مدّة التنفيذ", "Lead time"), T("الإنتاج", "Production"), T("يُصنع حسب الطلب، 2–3 أسابيع", "Made to order, 2–3 weeks"))],
                null,
                featured: false, stock: 0, madeToOrder: true,
                leadTime: T("يُشحن خلال 2–3 أسابيع", "Ships in 2–3 weeks"),
                rating: (4.9, 24), publishedDaysAgo: 110, now: now,
                dimensions: Dim(width: 60, height: 180, depth: 38, weight: 19m)),

            BuildProduct("vessel-carved-bowl",
                T("وعاء منحوت", "Vessel Carved Bowl"),
                T("يُخرَط رطباً ويجفّ ببطء", "Turned wet, dried slow"),
                T("يُخرَط من خشبٍ أخضر ويُترك ليجفّ على مدى أشهر، فتتحرّك الحافة قليلاً عن الاستدارة التامّة.",
                  "Turned from green timber and left to dry over several months, which lets the rim move slightly out of round."),
                T("الخرط رطباً والتجفيف البطيء يتخلّيان عن السيطرة — ترتفع الحافة ملّيمترات مع خروج الرطوبة.",
                  "Turning wet and drying slowly gives up control — the rim lifts a few millimetres as the moisture leaves."),
                byCategory["decorative"], [byCollection["the-dark-grain"], byCollection["quiet-rooms"]],
                130m,
                [walnut, cherry, oak],
                [(T("صغير", "Small"), 0m, Dim(diameter: 18, height: 9, weight: 0.6m)),
                 (T("كبير", "Large"), 65m, Dim(diameter: 28, height: 13, weight: 1.3m))],
                [T("جوز صلب", "Solid Walnut"), T("تشطيب زيتي آمن غذائياً", "Food-safe oil finish")],
                [(T("التصنيع", "Construction"), T("الطريقة", "Method"), T("يُخرَط أخضر ويجفّ بالهواء ببطء", "Green-turned, slow air-dried")),
                 (T("الاستخدام", "Use"), T("الملاءمة", "Suitability"), T("للمواد الجافة والفاكهة، لا للسوائل", "Dry goods and fruit; not for liquids")),
                 (T("العناية", "Care"), T("التنظيف", "Cleaning"), T("يُغسل يدوياً ويُجفَّف فوراً، ولا يُغمر أبداً", "Hand wash, dry immediately. Never immersed."))],
                (T("صناعة يدوية", "Handmade"), BadgeTone.Handmade),
                featured: true, stock: 24, rating: (4.9, 87), publishedDaysAgo: 195, now: now),

            BuildProduct("plateau-serving-tray",
                T("صينية بلاتو", "Plateau Serving Tray"),
                T("لوحٌ واحد ومقبضان، دون تكلّف", "One board, two handles, no fuss"),
                T("تُقَصّ من لوحٍ عريض واحد، والمقبضان مشغولان في طرفيه لا مُضافان إليه.",
                  "Cut from a single wide board with the handles worked into the ends rather than added on."),
                LocalizedText.Empty,
                byCategory["decorative"], [byCollection["quiet-rooms"], byCollection["the-entrance"]],
                140m,
                [oak, cherry, walnut],
                [(T("قطر 34", "Ø 34"), 0m, Dim(diameter: 34, height: 4, weight: 1.1m)),
                 (T("قطر 45", "Ø 45"), 55m, Dim(diameter: 45, height: 4.5m, weight: 1.9m))],
                [T("بلوط صلب", "Solid Oak"), T("تشطيب زيتي آمن غذائياً", "Food-safe oil finish")],
                [(T("التصنيع", "Construction"), T("الطريقة", "Method"), T("لوح واحد بتجويف محفور", "Single board, carved dish")),
                 (T("العناية", "Care"), T("التنظيف", "Cleaning"), T("يُغسل يدوياً ويُجفَّف فوراً", "Hand wash, dry immediately"))],
                null,
                featured: false, stock: 33, rating: (4.8, 64), publishedDaysAgo: 250, now: now)
        };

        db.Products.AddRange(products);

        /* ---- Related products ---------------------------------------------
           Derived rather than authored: same category first, then anything
           sharing a collection. Stays correct as the catalogue grows. */
        foreach (var product in products)
        {
            var related = products
                .Where(other => other.Id != product.Id && other.CategoryId == product.CategoryId)
                .Concat(products.Where(other =>
                    other.Id != product.Id
                    && other.CategoryId != product.CategoryId
                    && other.Collections.Any(c => product.Collections.Contains(c))))
                .Distinct()
                .Take(4)
                .ToList();

            for (var i = 0; i < related.Count; i++)
            {
                product.RelatedProducts.Add(new ProductRelation
                {
                    ProductId = product.Id,
                    RelatedProductId = related[i].Id,
                    Position = i
                });
            }
        }

        /* First save: products and variants, DefaultVariantId left null.
           Setting it now would ask EF to insert a Product pointing at a
           ProductVariant that points back at it — a cycle it cannot order. */
        await db.SaveChangesAsync(ct);

        foreach (var product in products)
        {
            product.DefaultVariantId = product.Variants.OrderBy(v => v.Position).First().Id;
        }

        await db.SaveChangesAsync(ct);
    }

    /* =====================================================================
       Builders
       ===================================================================== */

    private static Category NewCategory(
        string slug, LocalizedText name, LocalizedText tagline, LocalizedText description, int position) =>
        new()
        {
            Name = name,
            Slug = slug,
            Tagline = tagline,
            Description = description,
            Position = position,
            IsFeatured = true
        };

    private static Collection NewCollection(
        string slug, LocalizedText title, LocalizedText subtitle, LocalizedText description,
        int position, bool featured) =>
        new()
        {
            Title = title,
            Slug = slug,
            Subtitle = subtitle,
            Description = description,
            Position = position,
            IsFeatured = featured,
            IsPublished = true
        };

    private static void AttachCategoryImages(IEnumerable<Category> categories)
    {
        foreach (var category in categories)
        {
            category.Image = new ProductImage
            {
                StorageKey = $"categories/{category.Slug}.jpg",
                AltText = new LocalizedText(
                    $"{category.Name.Ar} — {category.Tagline.Ar}",
                    $"{category.Name.En} — {category.Tagline.En}"),
                Role = ImageRole.Primary,
                Width = 1600,
                Height = 2050
            };
        }
    }

    private static void AttachCollectionImages(IEnumerable<Collection> collections)
    {
        foreach (var collection in collections)
        {
            collection.HeroImage = new ProductImage
            {
                StorageKey = $"collections/{collection.Slug}/hero.jpg",
                AltText = new LocalizedText(
                    $"{collection.Title.Ar} — {collection.Subtitle.Ar}",
                    $"{collection.Title.En} — {collection.Subtitle.En}"),
                Role = ImageRole.Lifestyle,
                Width = 2560,
                Height = 1600
            };

            for (var i = 0; i < 2; i++)
            {
                collection.Images.Add(new ProductImage
                {
                    StorageKey = $"collections/{collection.Slug}/study-{i + 1}.jpg",
                    AltText = new LocalizedText(
                        $"دراسة داخلية — {collection.Title.Ar}",
                        $"{collection.Title.En} interior study"),
                    Role = ImageRole.Lifestyle,
                    Width = 1700,
                    Height = 2000,
                    Position = i
                });
            }
        }
    }

    private static Dimensions Dim(
        decimal? width = null, decimal? height = null, decimal? depth = null,
        decimal? diameter = null, decimal? weight = null) =>
        new()
        {
            Unit = "cm",
            Width = width,
            Height = height,
            Depth = depth,
            Diameter = diameter,
            WeightKg = weight
        };

    /// <summary>
    /// Expands a compact bilingual description into a full aggregate: options,
    /// the cartesian product of variants, images, specs, materials and badges.
    /// </summary>
    private static Product BuildProduct(
        string slug,
        LocalizedText name,
        LocalizedText tagline,
        LocalizedText description,
        LocalizedText story,
        Category category,
        Collection[] collections,
        decimal basePrice,
        (string Key, LocalizedText Label, string Swatch)[] finishes,
        (LocalizedText Label, decimal Delta, Dimensions Dims)[] sizes,
        LocalizedText[] materials,
        (LocalizedText Group, LocalizedText Label, LocalizedText Value)[] specs,
        (LocalizedText Label, BadgeTone Tone)? badge,
        bool featured,
        int stock,
        (double Average, int Count) rating,
        int publishedDaysAgo,
        DateTimeOffset now,
        bool madeToOrder = false,
        LocalizedText leadTime = default,
        Dimensions? dimensions = null)
    {
        var product = new Product
        {
            Name = name,
            Slug = slug,
            Tagline = tagline,
            Description = description,
            Story = story,
            CategoryId = category.Id,
            Category = category,
            IsFeatured = featured,
            IsPublished = true,
            PublishedAt = now.AddDays(-publishedDaysAgo),
            CreatedAt = now.AddDays(-publishedDaysAgo),
            RatingAverage = rating.Average,
            RatingCount = rating.Count,
            Dimensions = dimensions ?? (sizes.Length > 0 ? sizes[0].Dims with { } : new Dimensions()),
            CareInstructions = specs.FirstOrDefault(s => s.Group.En == "Care").Value
        };

        foreach (var collection in collections) product.Collections.Add(collection);

        for (var i = 0; i < materials.Length; i++)
            product.Materials.Add(new ProductMaterial { Name = materials[i], Position = i });

        for (var i = 0; i < specs.Length; i++)
        {
            var (group, label, value) = specs[i];
            product.Specifications.Add(new ProductSpecification
            {
                Group = group,
                Label = label,
                Value = value,
                Position = i
            });
        }

        if (badge is { } b)
            product.Badges.Add(new ProductBadge { Label = b.Label, Tone = b.Tone, Position = 0 });

        // ---- Options ----
        var finishOption = new ProductOption
        {
            Name = new LocalizedText("التشطيب", "Finish"),
            Position = 0,
            ProductId = product.Id
        };

        for (var i = 0; i < finishes.Length; i++)
        {
            finishOption.Values.Add(new ProductOptionValue
            {
                Value = finishes[i].Label,
                Swatch = finishes[i].Swatch,
                Position = i
            });
        }
        product.Options.Add(finishOption);

        ProductOption? sizeOption = null;
        if (sizes.Length > 0)
        {
            sizeOption = new ProductOption
            {
                Name = new LocalizedText("المقاس", "Size"),
                Position = 1,
                ProductId = product.Id
            };

            for (var i = 0; i < sizes.Length; i++)
                sizeOption.Values.Add(new ProductOptionValue { Value = sizes[i].Label, Position = i });

            product.Options.Add(sizeOption);
        }

        // ---- Variants: every finish × every size ----
        (LocalizedText Label, decimal Delta, Dimensions Dims)[] effectiveSizes = sizes.Length > 0
            ? sizes
            : [(LocalizedText.Empty, 0m, product.Dimensions)];

        var position = 0;
        var perVariantStock = Math.Max(0, stock / Math.Max(1, finishes.Length * effectiveSizes.Length));

        for (var f = 0; f < finishes.Length; f++)
        {
            for (var s = 0; s < effectiveSizes.Length; s++)
            {
                var finish = finishes[f];
                var size = effectiveSizes[s];
                var isDefault = f == 0 && s == 0;

                var variant = new ProductVariant
                {
                    ProductId = product.Id,
                    Sku = $"ADF-{slug[..Math.Min(3, slug.Length)].ToUpperInvariant()}-{f}{s}",
                    Name = size.Label.IsEmpty
                        ? finish.Label
                        : new LocalizedText(
                            $"{finish.Label.Ar} / {size.Label.Ar}",
                            $"{finish.Label.En} / {size.Label.En}"),
                    Price = Price(basePrice + size.Delta),
                    // `with { }` clones. The size tuples are shared across every
                    // finish, and an OWNED entity instance can belong to only
                    // one owner — handing the same object to three variants
                    // leaves two of them with no dimensions at all.
                    Dimensions = sizes.Length > 0 ? size.Dims with { } : null,
                    Position = position++,
                    Inventory = new Inventory
                    {
                        Quantity = madeToOrder ? 0 : perVariantStock + (isDefault ? 3 : 0),
                        IsMadeToOrder = madeToOrder,
                        LeadTime = leadTime
                    }
                };

                variant.OptionValues.Add(new VariantOptionValue
                {
                    VariantId = variant.Id,
                    OptionValue = finishOption.Values.ElementAt(f)
                });

                if (sizeOption is not null)
                {
                    variant.OptionValues.Add(new VariantOptionValue
                    {
                        VariantId = variant.Id,
                        OptionValue = sizeOption.Values.ElementAt(s)
                    });
                }

                product.Variants.Add(variant);
            }
        }

        // ---- Images ----
        // Storage keys only; the API turns these into URLs. Real photography
        // drops in by placing files at these paths.
        var roles = new[] { ImageRole.Primary, ImageRole.Hover, ImageRole.Gallery, ImageRole.Detail, ImageRole.Lifestyle };
        for (var i = 0; i < roles.Length; i++)
        {
            product.Images.Add(new ProductImage
            {
                StorageKey = $"products/{slug}/{i + 1}.jpg",
                AltText = roles[i] == ImageRole.Lifestyle
                    ? new LocalizedText($"{name.Ar} في مساحةٍ داخلية", $"{name.En} shown in a furnished interior")
                    : new LocalizedText($"{name.Ar} بتشطيب {finishes[0].Label.Ar}", $"{name.En} in {finishes[0].Label.En}"),
                Role = roles[i],
                Width = roles[i] == ImageRole.Lifestyle ? 2400 : 1600,
                Height = roles[i] == ImageRole.Lifestyle ? 1600 : 2000,
                Position = i
            });
        }

        return product;
    }
}
