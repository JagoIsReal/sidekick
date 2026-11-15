using System.Text;
using Sidekick.Core;
using Newtonsoft.Json;

namespace Sidekick.Keywords;

[Serializable]
public sealed class KeywordSettings
{
    public const int MaxTextLength = 128;
    public const int MaxRulesPerProfile = 64;

    // Public setters support Dalamud's serializer. Runtime edits use the methods below to invalidate caches.
    public List<KeywordRule> Rules { get; set; } = [];
    [JsonIgnore] public long Revision { get; private set; }

    public IEnumerable<KeywordRule> ForProfile(KeywordProfile? profile) => profile is null ? [] : Rules.Where(profile.Contains);

    internal IEnumerable<string> ActiveTerms(CharacterIdentity identity)
    {
        var character = KeywordProfile.ForCharacter(identity.DisplayName, identity.HomeWorldId);
        return Rules.Where(rule => rule.Enabled && (rule.Account || (character?.Contains(rule) ?? false)))
            .Select(rule => rule.Text).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public KeywordEditResult TrySave(KeywordProfile? profile, string text, Guid? existingId = null)
    {
        if (profile is null)
            return KeywordEditResult.ProfileUnavailable;
        var normalized = Normalize(text);
        if (normalized is null)
            return KeywordEditResult.InvalidText;

        var rules = ForProfile(profile).ToArray();
        var existing = existingId is { } id ? Rules.FirstOrDefault(rule => rule.Id == id) : null;
        if (existingId is not null && existing is null)
            return KeywordEditResult.RuleNotFound;
        if (rules.Any(rule => rule.Id != existingId && string.Equals(rule.Text, normalized, StringComparison.OrdinalIgnoreCase)))
            return KeywordEditResult.Duplicate;
        if ((existing is null || !profile.Contains(existing)) && rules.Length >= MaxRulesPerProfile)
            return KeywordEditResult.LimitReached;

        if (existing is null)
            Rules.Add(existing = new KeywordRule());
        existing.Text = normalized;
        existing.Account = profile.Account;
        existing.CharacterName = profile.CharacterName;
        existing.HomeWorldId = profile.HomeWorldId;
        Revision++;
        return KeywordEditResult.Success;
    }

    public bool SetEnabled(Guid id, bool enabled)
    {
        var rule = Rules.FirstOrDefault(rule => rule.Id == id);
        if (rule is null || rule.Enabled == enabled)
            return false;
        rule.Enabled = enabled;
        Revision++;
        return true;
    }

    public bool Remove(Guid id)
    {
        var rule = Rules.FirstOrDefault(rule => rule.Id == id);
        if (rule is null)
            return false;
        Rules.Remove(rule);
        Revision++;
        return true;
    }

    internal void Validate()
    {
        var cleaned = new List<KeywordRule>();
        var ids = new HashSet<Guid>();
        foreach (var rule in Rules ?? [])
        {
            if (rule is null)
                continue;
            var profile = KeywordProfile.FromRule(rule);
            if (profile is null)
                continue;
            var text = Normalize(rule.Text);
            if (text is null)
                continue;
            var previous = cleaned.Where(profile.Contains).ToArray();
            if (previous.Length >= MaxRulesPerProfile || previous.Any(other => string.Equals(other.Text, text, StringComparison.OrdinalIgnoreCase)))
                continue;
            rule.Text = text;
            rule.CharacterName = profile.CharacterName;
            rule.HomeWorldId = profile.HomeWorldId;
            if (rule.Id == Guid.Empty || !ids.Add(rule.Id))
            {
                rule.Id = Guid.NewGuid();
                ids.Add(rule.Id);
            }
            cleaned.Add(rule);
        }
        Rules = cleaned;
        Revision++;
    }

    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl))
            return null;
        try
        {
            var normalized = text.Trim().Normalize(NormalizationForm.FormC);
            return normalized.Length is > 0 and <= MaxTextLength ? normalized : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
