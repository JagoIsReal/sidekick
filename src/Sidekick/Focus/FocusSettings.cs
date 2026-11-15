using System.Text;
using Dalamud.Game.Text;
using Newtonsoft.Json;
using Sidekick.Core;

namespace Sidekick.Focus;

public readonly record struct FocusCharacter(string Name, uint HomeWorldId)
{
    public bool Matches(FocusCharacter other) => HomeWorldId == other.HomeWorldId &&
        string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public static FocusCharacter? Create(string? name, uint homeWorldId)
    {
        if (homeWorldId is 0 or > ushort.MaxValue || string.IsNullOrWhiteSpace(name) ||
            name.Length > 64 || name.Any(char.IsControl))
            return null;
        try
        {
            var identity = CharacterIdentity.FromName(name.Normalize(NormalizationForm.FormC), homeWorldId);
            return identity is null || identity.LastName.Length == 0 ? null : new FocusCharacter(identity.DisplayName, homeWorldId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

public enum FocusCombination { Any, All }
public enum FocusParticipantResult { Success, Invalid, Duplicate, LimitReached }

[Serializable]
public sealed class FocusParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public uint HomeWorldId { get; set; }
    public bool Enabled { get; set; } = true;
}

[Serializable]
public sealed class FocusSettings
{
    public const int MaxParticipants = 64;
    public const int DefaultOpacity = 25;
    public const float DefaultDistance = 15;
    public const float MaxDistance = 300;

    public bool Enabled { get; set; }
    public int OtherOpacityPercent { get; set; } = DefaultOpacity;
    public bool UseDistance { get; set; } = true;
    // Preserve the serialized key from v6; native world units have always been yalms.
    [JsonProperty("DistanceMeters")]
    public float DistanceYalms { get; set; } = DefaultDistance;
    public bool UseParticipants { get; set; } = true;
    public FocusCombination Combination { get; set; }
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public HashSet<XivChatType> Channels { get; set; } = [XivChatType.Say, XivChatType.CustomEmote, XivChatType.StandardEmote];
    public List<FocusParticipant> Participants { get; set; } = [];
    [JsonIgnore] private FocusCharacter[] activeParticipants = [];

    public bool Contains(FocusCharacter character) => activeParticipants.Any(character.Matches);

    public FocusParticipantResult Add(string? name, uint homeWorldId)
    {
        if (FocusCharacter.Create(name, homeWorldId) is not { } character)
            return FocusParticipantResult.Invalid;
        if (Participants.Any(p => new FocusCharacter(p.Name, p.HomeWorldId).Matches(character)))
            return FocusParticipantResult.Duplicate;
        if (Participants.Count >= MaxParticipants)
            return FocusParticipantResult.LimitReached;
        Participants.Add(new FocusParticipant { Name = character.Name, HomeWorldId = character.HomeWorldId });
        Rebuild();
        return FocusParticipantResult.Success;
    }

    public void SetParticipantEnabled(Guid id, bool enabled)
    {
        if (Participants.FirstOrDefault(p => p.Id == id) is not { } participant)
            return;
        participant.Enabled = enabled;
        Rebuild();
    }

    public void Remove(Guid id)
    {
        Participants.RemoveAll(p => p.Id == id);
        Rebuild();
    }

    internal void Validate()
    {
        if (OtherOpacityPercent is < 1 or > 100)
            OtherOpacityPercent = DefaultOpacity;
        if (!float.IsFinite(DistanceYalms) || DistanceYalms is < 1 or > MaxDistance)
            DistanceYalms = DefaultDistance;
        if (!Enum.IsDefined(Combination))
            Combination = FocusCombination.Any;
        Channels = (Channels ?? []).Where(channel => FocusChannels.All.Contains(channel)).ToHashSet();
        var cleaned = new List<FocusParticipant>();
        var ids = new HashSet<Guid>();
        foreach (var participant in Participants ?? [])
        {
            if (participant is null || FocusCharacter.Create(participant.Name, participant.HomeWorldId) is not { } character ||
                cleaned.Any(p => new FocusCharacter(p.Name, p.HomeWorldId).Matches(character)))
                continue;
            if (cleaned.Count == MaxParticipants)
                break;
            participant.Name = character.Name;
            if (participant.Id == Guid.Empty || !ids.Add(participant.Id))
            {
                participant.Id = Guid.NewGuid();
                ids.Add(participant.Id);
            }
            cleaned.Add(participant);
        }
        Participants = cleaned;
        Rebuild();
    }

    private void Rebuild() => activeParticipants = Participants.Where(p => p.Enabled)
        .Select(p => new FocusCharacter(p.Name, p.HomeWorldId)).ToArray();

    public void Reset()
    {
        var defaults = new FocusSettings();
        Enabled = defaults.Enabled;
        OtherOpacityPercent = defaults.OtherOpacityPercent;
        UseDistance = defaults.UseDistance;
        DistanceYalms = defaults.DistanceYalms;
        UseParticipants = defaults.UseParticipants;
        Combination = defaults.Combination;
        Channels = defaults.Channels;
    }
}

// Only player communication channels are eligible. System and GM messages are always preserved.
internal static class FocusChannels
{
    internal static readonly XivChatType[] All = [
        XivChatType.Say, XivChatType.CustomEmote, XivChatType.StandardEmote, XivChatType.Yell, XivChatType.Shout,
        XivChatType.TellIncoming, XivChatType.TellOutgoing, XivChatType.Party, XivChatType.CrossParty,
        XivChatType.Alliance, XivChatType.FreeCompany, XivChatType.PvPTeam, XivChatType.NoviceNetwork,
        XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4,
        XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8,
        XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
    ];
}
