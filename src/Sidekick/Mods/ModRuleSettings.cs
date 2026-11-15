using Newtonsoft.Json;
using Sidekick.Locations;

namespace Sidekick.Mods;

public enum ModAction { Enable, Disable, Toggle }
public enum ModRefreshMode { CollectionMembers = 0, All = 1, Self = 2, Furniture = 3 }

public sealed class ModLocationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public LocationCondition Location { get; set; } = new();
    public ModAction Action { get; set; }
    // Null preserves legacy rules by following the local character's effective collection.
    public Guid? CollectionId { get; set; }
    // Defaults preserve the behavior of rules created before schema 5.
    public ModRefreshMode RefreshMode { get; set; }
    // Penumbra directory identifiers, never ambiguous display names or absolute paths.
    public List<string> Mods { get; set; } = [];

    internal bool IsValid => !string.IsNullOrWhiteSpace(Name) && Name.Length <= 80
        && !Name.Any(char.IsControl) && Enum.IsDefined(Action) && Location is { IsValid: true }
        && Enum.IsDefined(RefreshMode) && CollectionId != Guid.Empty
        && Mods is { Count: > 0 and <= 64 }
        && Mods.All(m => !string.IsNullOrWhiteSpace(m) && m.Length <= 260 && !m.Any(char.IsControl)
            && m.IndexOfAny(['/', '\\', ':']) < 0);

    internal ModLocationRule Copy() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        Location = Location with { },
        Action = Action,
        CollectionId = CollectionId,
        RefreshMode = RefreshMode,
        Mods = [.. Mods],
    };
}

public sealed class ModRuleSettings
{
    public const int MaxRules = 64;
    public bool Enabled { get; set; }
    public List<ModLocationRule> Rules { get; set; } = [];
    [JsonIgnore] internal int Revision { get; private set; }

    internal void Changed() => Revision++;

    internal void Validate()
    {
        Rules ??= [];
        var ids = new HashSet<Guid>();
        Rules = Rules.Where(r => r is not null && r.IsValid).Take(MaxRules).ToList();
        foreach (var rule in Rules)
        {
            if (rule.Id == Guid.Empty || !ids.Add(rule.Id))
            {
                rule.Id = Guid.NewGuid();
                ids.Add(rule.Id);
            }
            rule.Mods = rule.Mods.Distinct(StringComparer.Ordinal).ToList();
        }
        Changed();
    }
}
