using Aderfia.Application.Common;
using Aderfia.Domain.Common;

namespace Aderfia.Api.Middleware;

/// <summary>
/// Resolves the language for the current request, once.
/// <para>
/// Precedence is deliberate: an explicit <c>?lang=</c> beats the
/// <c>Accept-Language</c> header, which beats the store default (Arabic). The
/// query parameter wins because it represents a choice the visitor made in the
/// language switcher, while the header only reports what their browser was
/// configured with — someone browsing an Arabic store from a machine set to
/// English should get what they clicked.
/// </para>
/// <para>
/// Registered scoped and resolved lazily: most requests never touch it, and
/// the cost is a header read either way.
/// </para>
/// </summary>
public sealed class RequestLanguage(IHttpContextAccessor accessor) : ICurrentLanguage
{
    private Language? resolved;

    public Language Value => resolved ??= Resolve(accessor.HttpContext);

    private static Language Resolve(HttpContext? context)
    {
        if (context is null) return LanguageSupport.Default;

        if (context.Request.Query.TryGetValue("lang", out var query) && query.Count > 0)
            return LanguageSupport.Parse(query[0]);

        return LanguageSupport.Parse(context.Request.Headers.AcceptLanguage.ToString());
    }
}

/// <summary>
/// Stamps every response with the language it was rendered in, and tells
/// caches that the header participates in the cache key.
/// <para>
/// Without the <c>Vary</c>, a shared cache would happily serve an Arabic
/// product page to the next visitor who asked for English.
/// </para>
/// </summary>
public sealed class LanguageResponseMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, ICurrentLanguage language)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.ContentLanguage = language.Value.ToTag();
            context.Response.Headers.Append("Vary", "Accept-Language");
            return Task.CompletedTask;
        });

        return next(context);
    }
}
