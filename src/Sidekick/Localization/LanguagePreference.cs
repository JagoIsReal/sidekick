using System.Globalization;

namespace Sidekick.Localization;

internal static class LanguagePreference
{
    internal const string DefaultCode = "en";

    internal static string Normalize(string? code) => string.Equals(code, "fr", StringComparison.OrdinalIgnoreCase)
        ? "fr" : DefaultCode;

    internal static CultureInfo CultureFor(string? code) => CultureInfo.GetCultureInfo(Normalize(code));
}
