# Media root

Images are served from here. The database stores a **storage key**, never a
URL — the API builds the URL by prefixing the key with `Media:BaseUrl`.

So a product image row holding:

    products/halo-round-mirror/1.jpg

maps to the file:

    wwwroot/media/products/halo-round-mirror/1.jpg

and is served to the browser as:

    http://localhost:5099/media/products/halo-round-mirror/1.jpg

## Why BaseUrl is absolute in development

The storefront runs on `:5173` and the API on `:5099`. A root-relative
`/media/...` would be resolved by the browser against the *storefront* origin
and 404. `appsettings.Development.json` therefore sets an absolute
`Media:BaseUrl`. In production, point it at your CDN origin — no data
migration, because only keys are stored.

## Adding photographs

Drop files at the key paths above. Nothing needs rebuilding; the next page
load picks them up.

Each product expects five slots, in this order:

| File | Role | Used by | Suggested size |
|---|---|---|---|
| `1.jpg` | primary | card + gallery | 1600 × 2000 (4:5) |
| `2.jpg` | hover | card cross-fade | 1600 × 2000 (4:5) |
| `3.jpg` | gallery | product gallery | 1600 × 2000 (4:5) |
| `4.jpg` | detail | close grain study | 1600 × 2000 (4:5) |
| `5.jpg` | lifestyle | full-width band | 2400 × 1600 (3:2) |

Categories use `categories/{slug}.jpg`, and collections use
`collections/{slug}/hero.jpg` plus `study-1.jpg` and `study-2.jpg`.

**Set `Width` and `Height` on the image row to the real pixel dimensions.**
The storefront reserves the box from that ratio before the file arrives; a
wrong ratio causes layout shift as the image lands.
