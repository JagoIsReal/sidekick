using System.Text;

namespace Sidekick.Core;

public static class NameMatcher
{
    public static bool IsMatch(string? text, CharacterIdentity? identity)
    {
        if (identity is null || string.IsNullOrEmpty(text))
            return false;

        var normalized = text.Normalize(NormalizationForm.FormC);
        return WholeWordMatcher.Contains(normalized, identity.FirstName) || WholeWordMatcher.Contains(normalized, identity.LastName);
    }
}

