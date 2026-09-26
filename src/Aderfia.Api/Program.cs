using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Aderfia.Api.Middleware;
using Aderfia.Application;
using Aderfia.Application.Common;
using Aderfia.Infrastructure;
using Aderfia.Persistence;

var builder = WebApplication.CreateBuilder(args);

/* =========================================================================
   Services
   ========================================================================= */

builder.Services
    .AddApplication()
    // WebRootPath is where static files are served from; uploads land in
    // its media/ folder so a new photo is reachable on the next request.
    .AddInfrastructure(builder.Configuration, builder.Environment.WebRootPath
        ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"))
    .AddPersistence(builder.Configuration);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // camelCase everywhere, matching the TypeScript contracts exactly.
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = null;
        // Nulls are meaningful in the client's types (compareAtPrice, story),
        // so they are serialised rather than omitted.
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

/* Language. Resolved per request from ?lang= or Accept-Language, then
   injected into any service that has to pick between the two stored columns.
   RequestLanguage needs the HttpContext, hence the accessor. */
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentLanguage, RequestLanguage>();

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

/* CORS. The storefront runs on its own origin in development and may sit on
   a separate domain in production, so allowed origins are configuration. */
const string StorefrontPolicy = "storefront";

/* An Origin header never carries a trailing slash or padding, so "https://x/"
   typed into a dashboard matches nothing and CORS fails silently: the browser
   reports a network error, the server logs a clean 204, and nothing anywhere
   says "origin rejected". Normalising here costs nothing and removes the most
   common way to misconfigure this. */
var corsOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                   ?? ["http://localhost:5173"])
    .Where(o => !string.IsNullOrWhiteSpace(o))
    .Select(o => o.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy(StorefrontPolicy, policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Required for the guest-cart cookie to travel with requests.
        .AllowCredentials());
});

/* Reverse proxy. TLS terminates in front of this process on any normal
   deployment, so without these headers Request.IsHttps is false: HTTPS
   redirection loops, and the guest-cart cookie loses its Secure flag.

   OFF BY DEFAULT and enabled with ForwardedHeaders:Enabled, because trusting
   X-Forwarded-* from an untrusted caller is a spoofing vector — X-Forwarded-For
   is what the admin rate limiter partitions on, so a client that can set it at
   will can rotate its way around the limiter. Turn it on only when this app is
   reachable solely through a proxy you control. */
var behindProxy = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");

if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // The proxy's address is not knowable in a container network, so the
        // hop count is the only bound. See the comment above.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

/* HTTP-only binding outside Development.

   Render, and any other platform that terminates TLS in front of the
   container, forwards plain HTTP inwards. The container holds no certificate,
   so an HTTPS endpoint is not merely unnecessary — it is fatal: Kestrel falls
   back to the ASP.NET Core DEVELOPER certificate, which does not exist in the
   image, and UseHttps throws inside BindAsync before a single request is
   served. The process exits during startup.

   Two things can introduce that endpoint without a line of code asking for it:
   ASPNETCORE_HTTPS_PORTS, and an https:// entry anywhere in ASPNETCORE_URLS.
   Both were reproduced against the published build.

   An explicit Listen call is what makes this safe rather than merely tidy: it
   takes precedence over ASPNETCORE_URLS and ASPNETCORE_*_PORTS entirely, so no
   environment variable — inherited from a base image, added in a dashboard, or
   set by the platform — can attach an HTTPS listener to this process.

   PORT is honoured because hosts assign it; 10000 is the documented default.
   Development is untouched, so the https profile in launchSettings.json still
   runs against the local developer certificate. */
if (!builder.Environment.IsDevelopment())
{
    var httpPort = builder.Configuration.GetValue<int?>("PORT") ?? 10000;
    builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(httpPort));
}

/* Admin rate limiting. The admin API's only credential is a static key with
   no account and no lockout, so this is what stands between it and an
   offline-speed guessing attack. 60 requests a minute per IP is far above
   what editing a product costs and far below what guessing a 32-character
   key would need. */
const string AdminPolicy = "admin";

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(AdminPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.Services.AddResponseCompression();

/* Required by the [ResponseCache] attributes on the catalog controllers:
   using VaryByQueryKeys without this middleware throws at runtime. */
builder.Services.AddResponseCaching();

var app = builder.Build();

/* =========================================================================
   Pipeline
   ========================================================================= */

// Before everything: the scheme and client IP every later stage reads are
// wrong until the proxy's headers have been applied.
if (behindProxy) app.UseForwardedHeaders();

// First of ours, so it can catch anything thrown further down.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Content-Language and Vary, on every response including errors.
app.UseMiddleware<LanguageResponseMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // A response header, correct whenever the public origin is https.
    app.UseHsts();

    /* Redirecting is the proxy's job when there is one. Behind Render the
       edge already sends http to https, and this process listens on HTTP
       only — so a redirect here can only point at a port it does not serve.
       It would also fire on any internal request that arrives without
       X-Forwarded-Proto, which is how platform health checks usually reach a
       container: /health would answer 307 instead of 200 and the deploy would
       be marked unhealthy.

       Running with no proxy in front, the app owns its own TLS and should
       still redirect, so this follows the same switch as the headers. */
    if (!behindProxy) app.UseHttpsRedirection();
}

app.UseResponseCompression();

// Product imagery. In production this is fronted by a CDN and Media:BaseUrl
// points there instead.
app.UseStaticFiles();

// CORS must run before caching, so a cached response is never replayed to an
// origin that was not allowed to receive it.
/* Logged because a rejected origin is otherwise invisible from the server
   side: the response is a normal 204 with no CORS headers, and nothing
   records WHY. One line here turns "the site cannot reach the API" into a
   check anyone can make from the deployment log. */
if (corsOrigins.Length == 0)
{
    /* Fail closed, but never quietly. An empty list rejects every browser
       request while the API answers curl perfectly and reports itself
       healthy — the storefront looks broken and the server looks fine.
       Blank is always a mistake: a variable set to "" or to a name the
       binder does not recognise. */
    app.Logger.LogError(
        "CORS is configured with NO allowed origins, so every browser request "
        + "will be refused. Set Cors__AllowedOrigins__0 (note the __0 index) to "
        + "the storefront's origin, with no trailing slash.");
}
else
{
    app.Logger.LogInformation(
        "CORS allows {Count} origin(s): {Origins}",
        corsOrigins.Length,
        string.Join(", ", corsOrigins));
}

app.UseCors(StorefrontPolicy);
app.UseResponseCaching();

app.UseRateLimiter();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithTags("Diagnostics");

/* Database bring-up.

   Outside production: migrate AND seed, so a fresh clone has a catalogue to
   look at without anyone running a command.

   In production: migrate, never seed. This used to be skipped entirely, with
   migrations run by hand as a deploy step — which is the safer arrangement in
   principle and the wrong one here in practice: this app deploys itself from
   a GitHub workflow with no migration step in it, so a release carrying a
   schema change would start serving against a database that does not have it
   and 500 on the new endpoint.

   Seeding stays off, because that WOULD touch real data. Migrating does not:
   EF applies only the migrations recorded as pending in __EFMigrationsHistory
   and no-ops when there are none, which is every deploy that changes no
   schema. EF Core 9 takes a database lock around this, so several Container
   App replicas starting at once cannot apply the same migration twice.

   The trade to know about: a migration reaches production the moment its
   commit does. Review them as carefully as you would a manual run.

   Two guards, both learned the hard way when this first went out:

   ONLY WITH A REAL PROVIDER. Database:Provider defaults to Sqlite, so an
   image started with no environment at all tries to create aderfia.db in the
   working directory — which the non-root user cannot write, so EF throws and
   the process dies before it binds. That is precisely what the deploy's
   /health smoke test does: `docker run` the image with nothing configured.
   There is no database there and nothing to migrate.

   NEVER FATAL. This process is the whole API. If SQL Server is briefly
   unreachable while a replica starts, crash-looping turns a blip into an
   outage that cannot recover on its own, and Container Apps will keep
   restarting into the same failure. Serving while saying loudly what is
   wrong is strictly better: /health stays honest that the process is up, the
   endpoints that need the database fail on their own terms, and the log
   names the cause. */
if (app.Environment.IsProduction())
{
    var provider = app.Configuration["Database:Provider"];

    if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            await app.Services.InitialiseDatabaseAsync(seed: false);
            app.Logger.LogInformation("Database schema is up to date.");
        }
        catch (Exception ex)
        {
            app.Logger.LogError(
                ex,
                "Database migration failed during startup. The API is still serving, but the "
                + "schema may be out of date and data endpoints will fail until this is resolved.");
        }
    }
    else
    {
        app.Logger.LogWarning(
            "No SQL Server provider is configured (Database:Provider = {Provider}), so no "
            + "migrations ran. Expected when smoke-testing the image; a mistake in production.",
            provider ?? "unset");
    }
}
else
{
    await app.Services.InitialiseDatabaseAsync(seed: true);
}

app.Run();
