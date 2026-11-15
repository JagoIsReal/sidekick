using Sidekick.Core;

namespace Sidekick.Keywords;

public sealed record KeywordProfile
{
    private KeywordProfile(bool account, string characterName, uint homeWorldId)
    {
        Account = account;
        CharacterName = characterName;
        HomeWorldId = homeWorldId;
    }

    public bool Account { get; }
    public string CharacterName { get; }
    public uint HomeWorldId { get; }
    public static KeywordProfile Global { get; } = new(true, string.Empty, 0);

    public static KeywordProfile? ForCharacter(string? name, uint homeWorldId)
    {
        if (string.IsNullOrWhiteSpace(name) || homeWorldId == 0 || name.Length > 64 || name.Any(char.IsControl))
            return null;
        try
        {
            var identity = CharacterIdentity.FromName(name);
            return identity is null || identity.LastName.Length == 0
                ? null : new KeywordProfile(false, identity.DisplayName, homeWorldId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    internal bool Contains(KeywordRule rule) => rule.Account == Account && (Account ||
        rule.HomeWorldId == HomeWorldId && string.Equals(rule.CharacterName, CharacterName, StringComparison.OrdinalIgnoreCase));

    internal static KeywordProfile? FromRule(KeywordRule rule) => rule.Account
        ? Global : ForCharacter(rule.CharacterName, rule.HomeWorldId);
}
