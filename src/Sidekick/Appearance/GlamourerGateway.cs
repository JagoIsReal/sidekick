using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;
using Sidekick.Core;

namespace Sidekick.Appearance;

internal interface IGlamourerGateway
{
    bool Available { get; }
    CharacterIdentity? Character { get; }
    IReadOnlyDictionary<Guid, string> GetDesigns();
    string? Capture(CharacterIdentity character);
    bool Apply(CharacterIdentity character, Guid design);
    bool Restore(CharacterIdentity character, string state);
}

// Wire contracts inspected in Glamourer.Api shipped with Glamourer 1.7.1.3 (API 1).
// No assembly reference, reflection, hooks or edits to Glamourer's saved designs.
internal sealed class GlamourerGateway : IGlamourerGateway
{
    private const ulong EquipmentOnce = 1UL | 2UL; // ApplyFlag.Once | ApplyFlag.Equipment. Never lock the actor.
    private readonly IObjectTable objects;
    private readonly ICallGateSubscriber<(int, int)> version;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>> designs;
    private readonly ICallGateSubscriber<int, uint, (int, JObject?)> state;
    private readonly ICallGateSubscriber<Guid, int, uint, ulong, int> apply;
    private readonly ICallGateSubscriber<object, int, uint, ulong, int> restore;

    internal GlamourerGateway(IDalamudPluginInterface pi, IObjectTable objects)
    {
        this.objects = objects;
        version = pi.GetIpcSubscriber<(int, int)>("Glamourer.ApiVersion.V2");
        designs = pi.GetIpcSubscriber<Dictionary<Guid, string>>("Glamourer.GetDesignList.V2");
        state = pi.GetIpcSubscriber<int, uint, (int, JObject?)>("Glamourer.GetState");
        apply = pi.GetIpcSubscriber<Guid, int, uint, ulong, int>("Glamourer.ApplyDesign");
        restore = pi.GetIpcSubscriber<object, int, uint, ulong, int>("Glamourer.ApplyState");
    }

    public bool Available => version.HasFunction && version.InvokeFunc().Item1 == 1 && designs.HasFunction
        && state.HasFunction && apply.HasFunction && restore.HasFunction;

    public CharacterIdentity? Character => objects.LocalPlayer is { } player
        ? CharacterIdentity.FromName(player.Name.TextValue, player.HomeWorld.RowId) : null;

    public IReadOnlyDictionary<Guid, string> GetDesigns() => designs.InvokeFunc();

    private int? Index(CharacterIdentity expected)
    {
        var player = objects.LocalPlayer;
        var identity = player is null ? null : CharacterIdentity.FromName(player.Name.TextValue, player.HomeWorld.RowId);
        return player is not null && identity is not null && identity.HomeWorldId == expected.HomeWorldId
            && string.Equals(identity.DisplayName, expected.DisplayName, StringComparison.OrdinalIgnoreCase)
            ? player.ObjectIndex : null;
    }

    public string? Capture(CharacterIdentity character)
    {
        if (Index(character) is not { } index) return null;
        var (error, data) = state.InvokeFunc(index, 0);
        return error == 0 ? data?.ToString() : null;
    }

    public bool Apply(CharacterIdentity character, Guid design) => Index(character) is { } index
        && apply.InvokeFunc(design, index, 0, EquipmentOnce) is 0 or 1;

    public bool Restore(CharacterIdentity character, string data) => Index(character) is { } index
        && restore.InvokeFunc(JObject.Parse(data), index, 0, EquipmentOnce) is 0 or 1;

    internal static bool SameState(string left, string right) => JToken.DeepEquals(JToken.Parse(left), JToken.Parse(right));
}
