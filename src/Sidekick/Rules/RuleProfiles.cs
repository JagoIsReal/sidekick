using Newtonsoft.Json;
using Sidekick.Core;

namespace Sidekick.Rules;

public sealed class RuleCharacterProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CharacterName { get; set; } = "";
    public uint HomeWorldId { get; set; }
    public bool Enabled { get; set; } = true;

    internal bool IsValid => Id != Guid.Empty && HomeWorldId is > 0 and <= ushort.MaxValue
        && !string.IsNullOrWhiteSpace(CharacterName) && CharacterName.Length <= 64 && !CharacterName.Any(char.IsControl)
        && CharacterIdentity.FromName(CharacterName, HomeWorldId) is { LastName.Length: > 0 };

    internal bool Matches(CharacterIdentity? character) => Enabled && IsValid && character is not null
        && HomeWorldId == character.HomeWorldId
        && string.Equals(CharacterIdentity.FromName(CharacterName)!.DisplayName, character.DisplayName, StringComparison.OrdinalIgnoreCase);

    internal RuleCharacterProfile Copy() => new()
    {
        Id = Id,
        CharacterName = CharacterName,
        HomeWorldId = HomeWorldId,
        Enabled = Enabled,
    };
}

public sealed class RuleProfileSettings
{
    public const int MaxProfiles = 64;
    public List<RuleCharacterProfile> Characters { get; set; } = [];
    [JsonIgnore] internal int Revision { get; private set; }

    // A null profile is Account: all characters using this local configuration.
    internal bool Matches(Guid? id, CharacterIdentity? character) => id is null
        || Characters.FirstOrDefault(p => p.Id == id)?.Matches(character) == true;

    internal bool TrySave(RuleCharacterProfile draft)
    {
        if (!draft.IsValid || (Characters.All(p => p.Id != draft.Id) && Characters.Count >= MaxProfiles))
            return false;
        var copy = draft.Copy();
        copy.CharacterName = CharacterIdentity.FromName(copy.CharacterName)!.DisplayName;
        if (Characters.Any(p => p.Id != copy.Id && p.HomeWorldId == copy.HomeWorldId
            && string.Equals(p.CharacterName, copy.CharacterName, StringComparison.OrdinalIgnoreCase)))
            return false;
        var index = Characters.FindIndex(p => p.Id == copy.Id);
        if (index < 0) Characters.Add(copy);
        else Characters[index] = copy;
        Revision++;
        return true;
    }

    internal void Validate()
    {
        var valid = (Characters ?? []).Where(p => p is not null && p.IsValid).Take(MaxProfiles).ToArray();
        Characters = [];
        foreach (var profile in valid)
            if (Characters.All(p => p.Id != profile.Id)) TrySave(profile);
        Revision++;
    }
}
