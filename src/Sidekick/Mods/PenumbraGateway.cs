using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using CurrentSettings = System.ValueTuple<int, (bool, int, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>, bool)?>;
using TemporarySettings = System.ValueTuple<int, (bool, bool, int, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>)?, string>;
using SettingsInput = System.ValueTuple<bool, bool, int, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<string>>>;

namespace Sidekick.Mods;

// Contracts verified against Penumbra.Api shipped with Penumbra 1.7.2.1 (API 5).
// Use the public, versioned wire contracts directly; Penumbra remains optional.
internal sealed class PenumbraGateway : IPenumbraGateway
{
    private const int Key = -1397310533; // Stable identification lock; permits manual edits in Penumbra.
    private const string Source = "Sidekick location rules";
    private readonly ICallGateSubscriber<(int, int)> version;
    private readonly ICallGateSubscriber<Dictionary<string, string>> mods;
    private readonly ICallGateSubscriber<int, (bool, bool, (Guid, string))> collection;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>> collections;
    private readonly ICallGateSubscriber<Guid, string, string, bool, CurrentSettings> current;
    private readonly ICallGateSubscriber<Guid, string, string, int, TemporarySettings> temporary;
    private readonly ICallGateSubscriber<Guid, string, string, SettingsInput, string, int, int> set;
    private readonly ICallGateSubscriber<Guid, string, string, int, int> remove;
    private readonly ICallGateSubscriber<Guid, int, object> redraw;
    private readonly ICallGateSubscriber<int, object> redrawAll;
    private readonly ICallGateSubscriber<int, int, object> redrawObject;
    private readonly ICommandManager commands;
    private readonly Dictionary<(Guid, string), ModState> appliedStates = [];

    internal PenumbraGateway(IDalamudPluginInterface pi, ICommandManager commands)
    {
        this.commands = commands;
        version = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion.V5");
        mods = pi.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList");
        collection = pi.GetIpcSubscriber<int, (bool, bool, (Guid, string))>("Penumbra.GetCollectionForObject.V5");
        collections = pi.GetIpcSubscriber<Dictionary<Guid, string>>("Penumbra.GetCollections.V5");
        current = pi.GetIpcSubscriber<Guid, string, string, bool, CurrentSettings>("Penumbra.GetCurrentModSettings.V5");
        temporary = pi.GetIpcSubscriber<Guid, string, string, int, TemporarySettings>("Penumbra.QueryTemporaryModSettings.V5");
        set = pi.GetIpcSubscriber<Guid, string, string, SettingsInput, string, int, int>("Penumbra.SetTemporaryModSettings.V5");
        remove = pi.GetIpcSubscriber<Guid, string, string, int, int>("Penumbra.RemoveTemporaryModSettings.V5");
        redraw = pi.GetIpcSubscriber<Guid, int, object>("Penumbra.RedrawCollectionMembers");
        redrawAll = pi.GetIpcSubscriber<int, object>("Penumbra.RedrawAll.V5");
        redrawObject = pi.GetIpcSubscriber<int, int, object>("Penumbra.RedrawObject.V5");
    }

    public bool Available => version.HasFunction && version.InvokeFunc().Item1 == 5
        && mods.HasFunction && collection.HasFunction && current.HasFunction && temporary.HasFunction
        && collections.HasFunction
        && set.HasFunction && remove.HasFunction && redraw.HasAction && redrawAll.HasAction && redrawObject.HasAction;

    public Guid? CurrentCollection()
    {
        var (valid, _, effective) = collection.InvokeFunc(0);
        return valid && effective.Item1 != Guid.Empty ? effective.Item1 : null;
    }

    public IReadOnlyDictionary<string, string> GetMods() => mods.InvokeFunc();
    public IReadOnlyDictionary<Guid, string> GetCollections() => collections.InvokeFunc();

    public ModState? ReadPermanent(Guid id, string mod)
    {
        var (error, settings) = current.InvokeFunc(id, mod, "", false);
        if (error != 0)
            return null;
        if (settings is not { } value)
            return new ModState(false, 0, new Dictionary<string, IReadOnlyList<string>>());
        return new ModState(value.Item1, value.Item2,
            value.Item3.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.ToArray(), StringComparer.Ordinal));
    }

    public bool HasTemporary(Guid id, string mod)
    {
        var (error, settings, _) = temporary.InvokeFunc(id, mod, "", Key);
        return error != 0 || settings.HasValue;
    }

    public bool Apply(Guid id, string mod, ModState state)
    {
        if (set.InvokeFunc(id, mod, "", (false, state.Enabled, state.Priority, state.Options), Source, Key) != 0)
            return false;
        // Penumbra expands unspecified/default option groups. Remember its canonical result.
        var (error, settings, source) = temporary.InvokeFunc(id, mod, "", Key);
        if (error != 0 || settings is not { } value || source != Source)
        {
            // Roll back only our identification key if read-back could not confirm the change.
            if (remove.InvokeFunc(id, mod, "", Key) is 0 or 1 or 2 or 3 or 19)
                return false;
            throw new InvalidOperationException("Could not confirm or release temporary mod settings ownership.");
        }
        appliedStates[(id, mod)] = new ModState(value.Item2, value.Item3,
            value.Item4.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.ToArray(), StringComparer.Ordinal));
        return true;
    }

    public bool IsApplied(Guid id, string mod, ModState applied)
    {
        var (error, settings, source) = temporary.InvokeFunc(id, mod, "", Key);
        if (error is not (0 or 2 or 3 or 19))
            throw new InvalidOperationException("Could not query temporary mod settings ownership.");
        var expected = appliedStates.GetValueOrDefault((id, mod)) ?? applied;
        return error == 0 && settings is { } value && source == Source && !value.Item1
            && value.Item2 == expected.Enabled && value.Item3 == expected.Priority && SameOptions(value.Item4, expected.Options);
    }

    public bool Release(Guid id, string mod, ModState applied)
    {
        var expected = appliedStates.GetValueOrDefault((id, mod)) ?? applied;
        var (error, settings, source) = temporary.InvokeFunc(id, mod, "", Key);
        if (error is 2 or 3 or 19) // Missing collection/mod or another plugin's lock: no longer ours.
        {
            appliedStates.Remove((id, mod));
            return true;
        }
        if (error != 0)
            return false;
        if (settings is not { } value || source != Source || value.Item1
            || value.Item2 != expected.Enabled || value.Item3 != expected.Priority
            || !SameOptions(value.Item4, expected.Options))
        {
            appliedStates.Remove((id, mod));
            return true; // User/another owner changed it; preserve their edit.
        }
        var released = remove.InvokeFunc(id, mod, "", Key) is 0 or 1 or 2 or 3 or 19;
        if (released)
            appliedStates.Remove((id, mod));
        return released;
    }

    internal static bool SameOptions(Dictionary<string, List<string>> actual,
        IReadOnlyDictionary<string, IReadOnlyList<string>> expected) => actual.Count == expected.Count
        && actual.All(p => expected.TryGetValue(p.Key, out var options) && p.Value.ToHashSet(StringComparer.Ordinal).SetEquals(options));

    public void Redraw(Guid id, ModRefreshMode mode)
    {
        switch (mode)
        {
            case ModRefreshMode.CollectionMembers:
                redraw.InvokeAction(id, 0);
                break;
            case ModRefreshMode.All:
                redrawAll.InvokeAction(0);
                break;
            case ModRefreshMode.Self:
                redrawObject.InvokeAction(0, 0);
                break;
            case ModRefreshMode.Furniture:
                // No public furniture IPC exists in API 5. Dispatch Penumbra's supported local
                // command through Dalamud, without sending/reprinting a chat message.
                if (!commands.ProcessCommand("/penumbra redraw furniture"))
                    throw new InvalidOperationException("Penumbra's furniture redraw command is unavailable.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
