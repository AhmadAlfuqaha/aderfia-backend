# Aderfia

A premium e-commerce storefront for wooden interior products — mirrors, wall
clocks, shelving and decorative objects.

React + TypeScript on the front, ASP.NET Core (clean architecture) on the back,
joined by a REST API that either side can be developed against independently.

> **This is the `aderfia-backend` repository.** The storefront that consumes
> this API lives in `aderfia-frontend`. The two deploy separately; `DEPLOYMENT.md` covers both.
> Clone them as siblings to run the whole thing locally.

```
aderfia-frontend/      React 19 + TypeScript + Vite   →  aderfia-jo.com
aderfia-backend/       ASP.NET Core 9                 →  api.aderfia-jo.com
  └── src/
      ├── Aderfia.Domain/          entities, value objects, rules
      ├── Aderfia.Application/     use cases, DTOs, abstractions
      ├── Aderfia.Infrastructure/  clock, image URLs, external services
      ├── Aderfia.Persistence/     EF Core, configurations, seeding
      └── Aderfia.Api/             controllers, DI, middleware
```

---

## Running it

### Frontend

```bash
cd frontend
npm install
npm run dev
```

Opens on <http://localhost:5173>. **No backend is required** — the app ships
with an in-memory adapter that implements the full catalog API, so every page,
filter, cart and checkout step works immediately.

### Backend

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet restore
dotnet run --project src/Aderfia.Api
```

Serves on <http://localhost:5099>, with Swagger at `/swagger`. On first run it
applies migrations, creates a SQLite database and seeds the catalogue. Switch
to SQL Server by setting `Database:Provider` and `ConnectionStrings:Default`.

The `InitialCreate` migration (21 tables) is committed. To reset the dev
database, delete `src/Aderfia.Api/aderfia.dev.db` and run again.

#### The admin key

The admin API is guarded by a shared secret, and **no key is committed** — a
tracked credential is a leaked credential. Set one per machine with .NET user
secrets, which stores it outside the repository:

```bash
cd src/Aderfia.Api
dotnet user-secrets init
dotnet user-secrets set "Admin:ApiKey" "$(openssl rand -base64 32)"
```

Or export `ADERFIA_ADMIN_KEY` in your shell instead. Paste the same value into
the sign-in box at `/admin` on the storefront.

With neither set, `/api/admin/*` returns **503** and logs why: an unconfigured
secret fails closed rather than defaulting to open. Production supplies it as
an environment variable — see `DEPLOYMENT.md`.


### Connecting the two

```bash
cd frontend
cp .env.example .env.local
```

Set `VITE_USE_MOCK_API=false`. That is the only change needed — see
*The service seam* below. This path is verified: the whole storefront
(homepage, shop, filters, product pages, cart, checkout) has been driven
end-to-end against the running API.

---

## Managing the shop

Everything below is done in a browser at **<http://localhost:5173/admin>** —
no SQL, no redeploys, no editing seed files.

Sign in with the key from `backend/src/Aderfia.Api/appsettings.Development.json`
(`Admin:ApiKey`). In production, set `ADERFIA_ADMIN_KEY` as an environment
variable instead.

> **The admin key is not real authentication.** It is one shared secret with
> no user identity, roles, rotation or audit trail — fine while one person
> runs the shop, and the first thing to replace when accounts land. The seam
> is narrow on purpose: swap `[RequireAdminKey]` for
> `[Authorize(Roles = "Admin")]` and nothing else changes.

### Products

`/admin/products` lists everything, drafts included, with stock and image
counts so gaps are visible at a glance. Publish, unpublish and delete happen
inline; the rest opens the editor.

The editor is one page covering name and slug, tagline, description and
story, category and collections, materials, dimensions, specifications,
badges, and the featured/published switches.

### Prices, variants and stock

Price and stock live on **variants**, never on the product — a variant is the
thing someone actually buys.

Define your option axes first (Finish, Size, or anything else), then press
**Generate** and every missing combination is created at once, seeded from
the last row so you are not retyping a price per size. Each row carries its
own SKU, price, was-price, stock count and optional dimensions. Tick
**made to order** and the stock count is replaced by a lead time.

Prices are typed in dollars; the API stores integer cents. That conversion
happens in one component and nowhere else.

### Images

Drag files onto the product's **Images** panel, or browse. The API reads each
file's real pixel dimensions from its header and stores them — the storefront
reserves every image's box from that ratio, so nothing jumps as photos load.

Roles decide where a photo appears:

| Role | Where it shows |
|---|---|
| `primary` | Product card and gallery. One per product. |
| `hover` | What the card cross-fades to. One per product. |
| `gallery` | Extra shots in the gallery. |
| `detail` | Close study of grain and finish. |
| `lifestyle` | Full-width band. Landscape works best. |
| — | Portrait 4:5 suits everything except lifestyle (3:2). |

Assigning a new primary or hover automatically demotes the previous holder to
gallery, so the slot never has two occupants. Files that are not real images
are rejected by reading the header, not by trusting the extension.

Images attach to a product that already exists, so save a new product once
before uploading — the folder is named after its slug.

### Categories and collections

`/admin/categories` is where the catalogue actually grows. Create "Lighting"
there and it appears in the navigation, the mega-menu, the homepage grid, the
shop filters and the footer immediately, with no code change. (This is
verified, not asserted.)

A category holding products refuses to be deleted, and says how many are in
the way.

Collections are editorial groupings that cut across categories, optionally
scheduled with start and end dates. Membership is set from each product's own
page, under **Badges & collections**.

### What deleting means

Products are **soft deleted**. They disappear from the shop instantly, but the
row and its variants survive so a two-year-old order can still render what it
contains. Nothing is destroyed.

---

## Architecture

### The service seam

The React app never calls `fetch` from a component. Everything goes through one
interface:

```
components / pages
        ↓
    hooks/useCatalog.ts
        ↓
    services/index.ts        ← the single switch
        ↓
  ┌─────┴─────┐
mockCatalogApi   httpCatalogApi
(fixtures)       (ASP.NET Core)
```

Both adapters implement `CatalogApi` (`services/catalogApi.ts`) identically.
Swapping them is one environment variable, and no component, hook or page
changes. This is what "do not tightly couple the frontend to the backend"
means in practice — and it also lets the storefront be built, demoed and
tested with no server running.

### Scalable catalog model

Nothing in the UI knows that mirrors, clocks or shelves exist. The model is
generic all the way down:

| Concept | How it scales |
|---|---|
| **Category** | Self-referencing `parentId`. "Lighting → Pendants" needs no schema or UI change. |
| **Collection** | Editorial grouping that cuts across categories. Drives the storytelling sections. |
| **Product options** | Any named axis — Finish, Size, Orientation, Movement. Values with a colour render as swatches, others as pills. Entirely data-driven. |
| **Variants** | One row per real combination. Price, SKU, stock and dimensions live here, never on the product. |
| **Specifications** | Grouped key/value rows. A clock declares "Movement / Type", a shelf "Load / Capacity"; neither needs a column. |
| **Facets** | The shop's filter panel renders whatever the API reports. A new category or material appears in the sidebar automatically, with its count. |

Adding a whole new product type is a data change. To prove it, add an entry to
`CATEGORY_SEED` in `frontend/src/data/catalog.ts` and give a product that
category — it appears in the nav, the mega-menu, the homepage grid, the shop
filters and the footer with no code edits.

### Frontend structure

```
src/
├── components/
│   ├── ui/          Button, Icon, Media, Overlay, Field, Price, States…
│   ├── product/     ProductCard, ProductGrid, Gallery, VariantSelector, QuickView
│   ├── cart/        CartDrawer, CartLineItem
│   ├── layout/      Header, Footer, MobileMenu, SearchOverlay
│   ├── home/        Hero and the homepage bands
│   └── shop/        Filters
├── pages/           one file per route, lazily loaded
├── layouts/         RootLayout — the app shell
├── hooks/           useAsync, useCatalog, useShopQuery, usePurchase, useUi
├── services/        the API seam
├── context/         Cart, Wishlist, UI (overlays + toasts)
├── data/            seed fixtures for the mock adapter
├── styles/          tokens.css, base.css, motion.css
├── types/           domain contracts, mirroring the API
└── utils/           formatting, product helpers, placeholder artwork
```

### Design system

`src/styles/tokens.css` is the single source of truth. No component hard-codes
a colour, size or duration.

| Token | Value |
|---|---|
| Background | `#FEFEFE` |
| Off-white sections | `#F4F2EF` |
| Primary text | `#18120B` |
| Secondary text | `#695747` |
| Forest green | `#33473E` |
| Wood brown | `#4F3E2F` |

Type is **Fraunces** (display) + **Inter** (body) — two families only. The
scale is fluid (`clamp()`), so mobile gets its own proportions rather than a
shrunken desktop.

Motion is restricted to `transform` and `opacity` so every animation stays on
the compositor. `prefers-reduced-motion` collapses all durations to 1ms, and
every animation is authored so its *end state* is the correct visual state —
if JavaScript never runs, content is simply there.

### Responsive approach

Mobile-first, with intentional layouts rather than reflowed ones:

- **Product cards** — hover actions on pointer devices; on touch the wishlist
  control is always visible and the action bar is dropped, because tapping the
  card should open the product.
- **Gallery** — thumbnail rail + cursor-tracked zoom on desktop; a snap-scrolling
  carousel with dots on mobile. Two implementations, one component.
- **Filters** — a sticky sidebar on desktop, the same panel inside a drawer on
  mobile. One `Filters` component, no duplication.
- **Checkout** — the order summary moves *above* the form on phones, so
  shoppers see what they are paying for before they start typing.
- Every interactive target clears 44px on coarse pointers.

---

## API

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/products` | Filtered, sorted, paged listing |
| `GET` | `/api/products/{slug}` | Single product |
| `GET` | `/api/products/batch?ids=` | Batch lookup for cart and wishlist |
| `GET` | `/api/products/facets` | Filter facets with counts |
| `GET` | `/api/products/suggest?q=` | Search typeahead |
| `GET` | `/api/categories`, `/api/categories/{slug}` | Taxonomy |
| `GET` | `/api/collections`, `/api/collections/{slug}` | Editorial groupings |
| `GET` | `/api/shipping-methods` | Delivery options |
| `GET/POST/PATCH/DELETE` | `/api/cart`, `/api/cart/items` | Server-side cart |
| `POST` | `/api/orders` | Place an order |
| `GET` | `/api/orders/{reference}?email=` | Guest order lookup |
| `GET/POST/DELETE` | `/api/wishlist` | Saved pieces (requires auth) |

Errors are RFC 7807 problem details. Application code throws
`NotFoundException` / `BusinessRuleException`; one middleware turns those into
404 / 400 / 409, and never leaks internals in production.

### Payments

**No card data touches this application.** The checkout's payment step is a
mount point for a provider's hosted element (Stripe Payment Element, Adyen
Drop-in, Checkout.com Frames). `POST /api/orders` creates the order as
`Pending`; the provider's webhook moves it to `Paid`. Only the payment intent
id is ever stored.

---

## Admin readiness

The backend already carries what an admin dashboard needs:

- **Soft deletes** — catalog rows are never destroyed, so a two-year-old order
  can still render the product it contains.
- **Order snapshots** — product names, prices and addresses are *copied* at
  placement. Renaming a product cannot rewrite history.
- **Publishing** — `IsPublished` / `PublishedAt` on products, `StartsAt` /
  `EndsAt` on collections for scheduled drops.
- **Image indirection** — records store storage *keys*, not URLs. Moving to a
  CDN is a `Media:BaseUrl` change, not a data migration.
- **Audit stamps** — `CreatedAt` / `UpdatedAt` applied centrally in
  `SaveChangesAsync`, so no use case can forget them.

---

## Brand assets

The master artwork is `LOGO.JPEG` at the repository root — the interlocking-A
monogram, white with an olive accent, on a dark green-black ground.

Because it is a JPEG it carries no transparency, and its ground would sit as a
heavy dark square on the white header, footer and admin. So two derivatives
are generated from it and committed:

| File | Used for |
|---|---|
| `frontend/src/assets/logo-mark.png` | Ink monogram on transparency — light surfaces |
| `frontend/src/assets/logo-mark-invert.png` | White monogram on transparency — dark surfaces |
| `frontend/public/favicon-32/180/512.png` | The original dark tile, which is exactly what an icon wants |

`Logo.tsx` renders both tones and CSS picks one, rather than choosing in
JavaScript — the header flips tone as it scrolls past the hero, and a class
change is instant where a re-render would lag the transition.

The olive accent (`#7B8350`) is preserved untouched in both tones. The
monogram reads as "AA" on its own, so the wordmark beside it carries the name.

To regenerate after changing `LOGO.JPEG`, re-run the extraction: key the
background colour out to alpha, classify each pixel as mark or accent by
saturation, and export at 256px tall.

---

## Known gaps

Called out plainly rather than left to be discovered:

1. **Imagery is generated.** `src/utils/artwork.ts` produces original SVG
   compositions from the brand palette so layouts read as intentional rather
   than broken. Images are consumed as plain URL strings, so real photographs
   drop in by changing `url` values — then delete that file.

2. **Product listings return full products, not summaries.**
   `ProductSummaryDto` exists and is the base type, but `ListProductsAsync`
   returns the complete `ProductDto` so the storefront can keep one `Product`
   shape everywhere — which is what makes the two adapters interchangeable.
   A card does need variant data (price range, stock, whether to offer "Add to
   bag" or "Select options"), but not *all* of it. The optimisation is to add
   `defaultVariantId` and `variantCount` to the summary and have quick view
   fetch the full product when it opens.

3. **Accounts are not implemented.** `/account` says so honestly and routes to
   what works without signing in. `WishlistController` already requires an
   authenticated subject and `CartService.MergeAsync` handles guest→customer
   cart merging, so the seam is in place.

4. **Payments are not wired.** Orders are created as `Pending`; the provider's
   hosted element and its webhook are the remaining work.

5. **Seed data lives in the main JS bundle** (~120 kB gzipped total). That is
   the cost of the mock adapter being the default. Setting
   `VITE_USE_MOCK_API=false` and removing the mock import from
   `services/index.ts` drops `data/catalog.ts` and `utils/artwork.ts` entirely.

---

## Commands

```bash
# Frontend
npm run dev            # dev server
npm run build          # typecheck + production build
npx tsc --noEmit       # typecheck only
npx oxlint .           # lint

# Backend
dotnet build
dotnet run --project src/Aderfia.Api
dotnet ef migrations add <Name> --project src/Aderfia.Persistence --startup-project src/Aderfia.Api
```

## Notes for whoever works on this next

Four things cost real time to find, and are easy to reintroduce:

- **`Entity` assigns its own `Id`.** So when you add a child to an
  *already-tracked* parent's collection, EF's change detector sees a set key
  and marks it `Modified` — an UPDATE that matches nothing. Always
  `db.Set<T>().Add(child)` explicitly, and let relationship fixup populate the
  navigation (adding to both duplicates the row in memory).
- **Owned types cannot be shared.** Handing one `Dimensions` instance to three
  variants leaves two of them with no dimensions. Clone with `with { }`.
- **`Product` and `ProductVariant` reference each other.** `DefaultVariantId`
  has to be assigned in a second save, after both rows exist.
- **SQLite cannot compare `DateTimeOffset`.** A converter is applied in
  `OnModelCreating` for that provider; without it any date predicate throws at
  runtime rather than at build time.
