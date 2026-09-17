# Deploying Aderfia

> Covers **both** repositories, `aderfia-frontend` and `aderfia-backend`.
> Kept identical in each; edit it in one and copy it across.

Two artifacts, deployed separately:

| Part | What it is | Where it goes |
|---|---|---|
| `frontend/` | Vite SPA, static files after `npm run build` | **Netlify** — `aderfia-jo.com` |
| `backend/` | ASP.NET Core 9 API + image files | A host with a **persistent disk** — `api.aderfia-jo.com` |

**The API must stay on a subdomain of `aderfia-jo.com`.** The guest-cart cookie is
`SameSite=Lax`, which travels between `aderfia-jo.com` and `api.aderfia-jo.com` because
they share a registrable domain. Leave the storefront on `aderfia.netlify.app` instead
and the browser drops the cookie on every request — shoppers won't notice (the bag is
mirrored in `localStorage`) but the database grows a new orphan cart row per request.

## DNS (at your registrar)

| Type | Name | Value |
|---|---|---|
| `A` / `ALIAS` | `@` | Netlify's load balancer — Netlify shows the exact value |
| `CNAME` | `www` | `<your-site>.netlify.app` |
| `CNAME` | `api` | your API host's domain |

Add the `api` record **before** deploying the storefront, or the first visitor gets a
site whose every request fails.

---

## 1. Persistent storage

Two things must live on a volume that survives a release. Both default to paths **inside
the deploy folder**, which is replaced on every deploy.

| What | Default (wrong for production) | Set to |
|---|---|---|
| Database | `aderfia.db`, relative | `/data/aderfia.db` via `ConnectionStrings__Default` |
| Product photos | `wwwroot/media/` | a volume mounted at `wwwroot/media`, or object storage + CDN |

Photos are written to `WebRootPath/media` by the admin uploader. There is no config knob
for the write path — only for the URL they're served from (`Media__BaseUrl`), so the
volume has to be mounted over `wwwroot/media` itself.

The 53 MB of existing photos in `backend/src/Aderfia.Api/wwwroot/media/` are in git. Copy
them onto the volume on first deploy; after that the volume is the source of truth.

## 2. Configuration

Fill in from the templates — every value is documented there:

- `backend/.env.production.example`
- `frontend/.env.production.example`

The four that break the site silently if wrong:

1. `ADERFIA_ADMIN_KEY` — unset means the admin API refuses everything. Generate fresh
   (`openssl rand -base64 32`); never reuse the development key.
2. `Media__BaseUrl` — must be an **absolute URL**, or every product image 404s.
3. `Cors__AllowedOrigins__0` — must be the real storefront origin, or every API call fails.
4. `VITE_USE_MOCK_API=false` — must be set **in the build environment**. Left true, the
   site builds against the mock catalog: it looks like a working shop that takes no orders.

Set `ForwardedHeaders__Enabled=true` if anything terminates TLS in front of the app.
Without it `Request.IsHttps` is false, HTTPS redirection can loop, and the cart cookie
ships without `Secure`.

## 3. Database migrations

Migrations do **not** run on start in Production (deliberate — `Program.cs`). Run them as
an explicit deploy step, before the new version serves traffic:

```bash
cd backend
dotnet ef database update --project src/Aderfia.Persistence --startup-project src/Aderfia.Api
```

Seeding is also skipped in Production, so a fresh database is **empty**. Either copy the
catalog up, or re-enter it through the admin UI:

```bash
# Copying the existing catalogue (stop the API first — SQLite is single-writer)
sqlite3 backend/src/Aderfia.Api/aderfia.dev.db ".backup '/tmp/aderfia.db'"
# then upload /tmp/aderfia.db to the volume as /data/aderfia.db
```

Product rows reference photos by storage key, so the database and `wwwroot/media` must be
copied **together** or images resolve to nothing.

## 4. Storefront — Netlify

`frontend/netlify.toml` already carries the build command, the SPA catch-all rewrite, the
cache headers and both `VITE_` variables. You should not need to set anything in the
Netlify dashboard beyond connecting the repo.

1. Push this repository to GitHub.
2. Netlify → **Add new site → Import an existing project**, pick the repo.
3. Set **Base directory** to `frontend`. Netlify reads `netlify.toml` from there and fills
   in the rest.
4. Deploy, and check the preview URL works before touching DNS.
5. **Domain management → Add custom domain** → `aderfia-jo.com`, then follow Netlify's DNS
   instructions. TLS is automatic once the records resolve.

The catch-all rewrite matters more than it looks: react-router owns the URL, so without it
a refresh on `/product/haven-mirror` returns Netlify's own 404 page.

If you change `VITE_API_BASE_URL`, you must **redeploy** — Vite inlines it into the
bundle at build time. Changing it in a dashboard without rebuilding does nothing.

## 5. Backups

SQLite, so a backup is one command — but use `.backup`, not `cp`, which can capture a
torn file mid-write:

```bash
sqlite3 /data/aderfia.db ".backup '/backups/aderfia-$(date +%F).db'"
```

Nightly, copied **off the machine**, with the media directory alongside it. Restore one
into a scratch environment before you rely on it; an untested backup isn't a backup.

## 6. Post-deploy checks

Ordered so each failure points at a specific cause:

1. `GET /health` → `{"status":"healthy"}`
2. Storefront home page renders products — if empty, the API is unreachable or
   `VITE_USE_MOCK_API` is wrong
3. Product images load — a broken image means `Media__BaseUrl`
4. Hard-refresh a deep link like `/product/haven-mirror` — a 404 means the SPA rewrite
5. Add to bag, reload the page, bag survives
6. **Place a real order end to end** and confirm the WhatsApp message arrives with the
   right items and total
7. Sign in to `/admin` with the production key; change something and see it on the site
8. Hit an admin route ~70 times in a minute and confirm a `429`

## 7. Rollback

Redeploy the previous commit. Note that migrations do not roll back automatically — if
the release included one, revert it deliberately with `dotnet ef migrations script` or
restore the pre-deploy backup.

---

## Known gaps

Accepted for launch, recorded so they aren't rediscovered as surprises:

- **No customer accounts.** Deliberate. Every visitor is a guest on a cookie; the `Users`
  table and `ListForUserAsync` are unused.
- **Admin auth is a single shared key** in `localStorage`, with no expiry, no rotation and
  no audit trail. Rate limited to 60/min per IP, which stops guessing, not sharing. If the
  key leaks, change the env var and redeploy — every admin session dies with it.
- **No automated tests.** The highest-value one to add first is order placement, covering
  that the server re-prices from its own variant records and ignores client-sent prices.
- **No order lookup for customers.** Checkout collects no email, so `GET /api/orders/{ref}`
  has no second factor and refuses orders placed without one. Customers keep the WhatsApp
  thread; the workshop matches orders by phone number.
