using Sidekick.Locations;

namespace Sidekick.Appearance;

public sealed class GlamourerRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public Guid? ProfileId { get; set; }
    public LocationCondition Conditions { get; set; } = new();
    public Guid DesignId { get; set; }

    internal bool IsValid => !string.IsNullOrWhiteSpace(Name) && Name.Length <= 80 && !Name.Any(char.IsControl)
        && ProfileId != Guid.Empty && DesignId != Guid.Empty && Conditions is { IsValid: true };

    internal GlamourerRule Copy() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        ProfileId = ProfileId,
        Conditions = Conditions.Copy(),
        DesignId = DesignId,
    };
}

public sealed class GlamourerSettings
{
    public const int MaxRules = 64;
    public bool Enabled { get; set; }
    public List<GlamourerRule> Rules { get; set; } = [];

    internal void Validate()
    {
        foreach (var rule in Rules ?? []) rule?.Conditions?.NormalizeSelections();
        Rules = (Rules ?? []).Where(r => r is not null && r.IsValid).Take(MaxRules).ToList();
        var ids = new HashSet<Guid>();
        foreach (var rule in Rules)
        {
            if (rule.Id == Guid.Empty || !ids.Add(rule.Id))
            {
                rule.Id = Guid.NewGuid();
                ids.Add(rule.Id);
            }
        }
    }
}
