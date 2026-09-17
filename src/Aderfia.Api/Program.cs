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

builder.Services.AddCors(options =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                  ?? ["http://localhost:5173"];

    options.AddPolicy(StorefrontPolicy, policy => policy
        .WithOrigins(origins)
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
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseResponseCompression();

// Product imagery. In production this is fronted by a CDN and Media:BaseUrl
// points there instead.
app.UseStaticFiles();

// CORS must run before caching, so a cached response is never replayed to an
// origin that was not allowed to receive it.
app.UseCors(StorefrontPolicy);
app.UseResponseCaching();

app.UseRateLimiter();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithTags("Diagnostics");

/* Migrate and seed outside production. In production, migrations run as a
   deliberate deployment step rather than on process start. */
if (!app.Environment.IsProduction())
{
    await app.Services.InitialiseDatabaseAsync(seed: true);
}

app.Run();
