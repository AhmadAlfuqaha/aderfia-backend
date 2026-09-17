using Aderfia.Domain.Common;

namespace Aderfia.Application.Common;

/// <summary>
/// Parsing and serialising the language token that travels on the wire.
/// </summary>
public static class LanguageSupport
{
    /// <summary>Arabic is the store's default, so anything unrecognised lands there.</summary>
    public const Language Default = Language.Ar;

    public static Language Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Default;

        // Accepts "en", "EN", "en-GB", "en-US;q=0.9" — anything whose first
        // subtag is English. Everything else, including "ar-JO", is Arabic.
        var first = value.Split(',')[0].Split(';')[0].Trim();
        var primary = first.Split('-')[0];

        return primary.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? Language.English
            : Language.Ar;
    }

    /// <summary>The BCP 47 tag, for `lang` attributes and Intl formatting.</summary>
    public static string ToTag(this Language language) =>
        language == Language.English ? "en" : "ar";
}

/// <summary>
/// Text in both languages, as the ADMIN sees it.
/// <para>
/// The storefront receives a single resolved string — it has no business
/// knowing a second language exists. Only the editor needs both, which is why
/// this shape appears in admin contracts and nowhere else.
/// </para>
/// </summary>
public record LocalizedTextDto
{
    public string Ar { get; init; } = string.Empty;
    public string En { get; init; } = string.Empty;

    public static LocalizedTextDto From(LocalizedText text) =>
        new() { Ar = text.Ar ?? string.Empty, En = text.En ?? string.Empty };

    public LocalizedText ToDomain() =>
        new((Ar ?? string.Empty).Trim(), (En ?? string.Empty).Trim());
}
