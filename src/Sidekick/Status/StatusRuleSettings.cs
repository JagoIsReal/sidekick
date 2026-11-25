using System.Numerics;
using Sidekick.Locations;
using NativeStatus = FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCommonList.CharacterData.OnlineStatus;

namespace Sidekick.Status;

internal static class StatusTargets
{
    // These are player-selectable statuses. The filter can also match statuses set by the game.
    internal static readonly uint Online = Id(NativeStatus.Online);
    internal static readonly IReadOnlyList<uint> Selectable =
    [
        Online, Id(NativeStatus.AwayFromKeyboard), Id(NativeStatus.Busy),
        Id(NativeStatus.RolePlaying), Id(NativeStatus.LookingToMeldMateria), Id(NativeStatus.LookingForParty),
    ];

    internal static bool IsObserved(uint target, uint current) => current == target || current == 0 && target == Online;

    private static uint Id(NativeStatus status) => (uint)BitOperations.TrailingZeroCount((ulong)status);
}

public sealed class StatusRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public Guid? ProfileId { get; set; }
    public bool AtLogin { get; set; }
    public uint? TargetStatusId { get; set; }
    public LocationCondition Conditions { get; set; } = new();

    internal bool IsValid => !string.IsNullOrWhiteSpace(Name) && Name.Length <= 80 && !Name.Any(char.IsControl)
        && ProfileId != Guid.Empty && TargetStatusId is { } target && StatusTargets.Selectable.Contains(target)
        && Conditions is { IsWellFormed: true } && (AtLogin || Conditions.HasSelection);

    internal StatusRule Copy() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        ProfileId = ProfileId,
        AtLogin = AtLogin,
        TargetStatusId = TargetStatusId,
        Conditions = Conditions.Copy(),
    };
}

public sealed class StatusRuleSettings
{
    public const int MaxRules = 64;
    public bool Enabled { get; set; }
    public List<StatusRule> Rules { get; set; } = [];

    internal void Validate()
    {
        var ids = new HashSet<Guid>();
        foreach (var rule in Rules ?? []) rule?.Conditions?.NormalizeSelections();
        Rules = (Rules ?? []).Where(r => r is not null && r.IsValid && r.Id != Guid.Empty && ids.Add(r.Id))
            .Take(MaxRules).ToList();
    }
}
