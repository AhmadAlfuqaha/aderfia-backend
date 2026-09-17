using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Aderfia.Api.Middleware;

/// <summary>
/// Guards every admin endpoint with a shared key sent as <c>X-Admin-Key</c>.
///
/// <para>
/// <b>This is an interim measure, not a finished authentication system.</b>
/// It is a single shared secret with no user identity, no roles, no rotation
/// and no audit trail — appropriate while one person runs the shop, and the
/// thing to replace first when accounts arrive. The seam is deliberately
/// narrow: swap this attribute for <c>[Authorize(Roles = "Admin")]</c> and
/// nothing else in the admin has to change.
/// </para>
///
/// <para>
/// The key is read from configuration (<c>Admin:ApiKey</c>) or the
/// <c>ADERFIA_ADMIN_KEY</c> environment variable. If neither is set, admin
/// endpoints refuse every request rather than defaulting to open.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAdminKeyAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "X-Admin-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<RequireAdminKeyAttribute>();

        /* Blank counts as absent, not as a configured empty key. `??` alone
           would stop at an "Admin:ApiKey": "" left in a settings file as a
           placeholder — a non-null empty string that never falls through, so
           the environment variable would be silently ignored and admin would
           be dead with the key correctly set. */
        var configured = configuration["Admin:ApiKey"];
        var expected = string.IsNullOrWhiteSpace(configured)
            ? Environment.GetEnvironmentVariable("ADERFIA_ADMIN_KEY")
            : configured;

        // Fail closed. An unconfigured secret must never mean "no secret".
        if (string.IsNullOrWhiteSpace(expected))
        {
            logger.LogError(
                "Admin endpoints are disabled: no Admin:ApiKey is configured. " +
                "Set it in appsettings or the ADERFIA_ADMIN_KEY environment variable.");

            context.Result = Problem(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Admin is not configured",
                "No admin key is configured on the server, so the admin API is disabled.");
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !FixedTimeEquals(provided.ToString(), expected))
        {
            logger.LogWarning(
                "Rejected admin request to {Path} from {Ip}",
                context.HttpContext.Request.Path,
                context.HttpContext.Connection.RemoteIpAddress);

            context.Result = Problem(
                context,
                StatusCodes.Status401Unauthorized,
                "Not authorised",
                "A valid admin key is required.");
            return;
        }

        await next();
    }

    /// <summary>
    /// Constant-time comparison. A plain <c>==</c> returns as soon as two
    /// bytes differ, which leaks how much of the key was correct and makes
    /// the secret guessable one character at a time.
    /// </summary>
    private static bool FixedTimeEquals(string provided, string expected)
    {
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);

        // Length alone is not secret, and FixedTimeEquals requires equal spans.
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static ObjectResult Problem(ActionExecutingContext context, int status, string title, string detail) =>
        new(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.HttpContext.Request.Path
        })
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
}
