using System.Text;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using Sidekick.Chat;

namespace Sidekick.Focus;

internal sealed class FocusController : IDisposable
{
    private readonly ConfigurationStore store;
    private readonly IFocusSceneSource scenes;
    private readonly IChatColors colors;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private long lastNearbyRefresh;
    private long nearbyRequestedUntil;
    private FocusScene? nearby;
    private bool reportedNearbyError;
    private bool disposed;

    internal FocusController(ConfigurationStore store, IFocusSceneSource scenes, IChatColors colors, IFramework framework, IPluginLog log)
    {
        this.store = store;
        this.scenes = scenes;
        this.colors = colors;
        this.framework = framework;
        this.log = log;
        framework.Update += OnFrameworkUpdate;
    }

    internal bool Enabled => store.Value.Focus.Enabled;
    internal long UnresolvedColors { get; private set; }
    internal bool NearbyUnavailable { get; private set; }

    internal bool SetEnabled(bool? value = null)
    {
        if (disposed)
            return Enabled;
        store.Value.Focus.Enabled = value ?? !Enabled;
        store.Save();
        return Enabled;
    }

    internal FocusScene? Nearby()
    {
        if (disposed)
            return null;
        // UI consumes only an immutable snapshot captured by the next framework update.
        Volatile.Write(ref nearbyRequestedUntil, Environment.TickCount64 + 1000);
        return Volatile.Read(ref nearby);
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (disposed)
            return;
        var now = Environment.TickCount64;
        if (now > Volatile.Read(ref nearbyRequestedUntil))
        {
            Volatile.Write(ref nearby, null);
            return;
        }
        if (lastNearbyRefresh != 0 && now - lastNearbyRefresh < 500)
            return;
        lastNearbyRefresh = now;
        try
        {
            Volatile.Write(ref nearby, scenes.Read());
            NearbyUnavailable = false;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref nearby, null);
            NearbyUnavailable = true;
            if (!reportedNearbyError)
            {
                log.Warning("Sidekick could not read nearby Focus players ({ErrorType}); further warnings are suppressed until reload.", exception.GetType().Name);
                reportedNearbyError = true;
            }
        }
    }

    public void Dispose()
    {
        disposed = true;
        framework.Update -= OnFrameworkUpdate;
        Volatile.Write(ref nearby, null);
    }

    internal SeString Transform(IChatMessage context, SeString message)
    {
        var settings = store.Value.Focus;
        if (disposed || !settings.Enabled || settings.OtherOpacityPercent >= 100 || message.Payloads.Count == 0 ||
            !settings.Channels.Contains(context.LogKind) || !FocusChannels.All.Contains(context.LogKind) ||
            context.SourceKind == XivChatRelationKind.LocalPlayer)
            return message;

        var scene = scenes.Read();
        if (scene is null)
            return message;
        var senderString = context.OriginalSender.Data.IsEmpty ? context.Sender : SeString.Parse(context.OriginalSender.Data.Span);
        var sender = ResolveSender(senderString, scene);
        var distance = sender is { } author
            ? scene.Players.FirstOrDefault(p => p.Character.Matches(author))?.DistanceYalms : null;
        if (!FocusRules.ShouldDim(settings, context.LogKind, sender, scene.Self, distance))
            return message;
        if (colors.Channel(context.LogKind) is not { } channelColor)
        {
            UnresolvedColors++;
            return message;
        }
        if (!SeStringOpacity.TryApply(message, channelColor, settings.OtherOpacityPercent, colors, out var output))
            UnresolvedColors++;
        return output;
    }

    internal static FocusCharacter? ResolveSender(SeString sender, FocusScene scene)
    {
        FocusCharacter? linked = null;
        var links = 0;
        foreach (var part in new ReadOnlySeStringSpan(sender.Encode()))
        {
            if (part.Type != ReadOnlySePayloadType.Macro || part.MacroCode != MacroCode.Link ||
                !part.TryGetExpression(out var type, out _, out var world, out _, out var name) ||
                !type.TryGetUInt(out var linkType) || linkType != (uint)LinkMacroPayloadType.Character)
                continue;
            // Read the public link expressions directly. PlayerPayload.World resolves a Dalamud data service.
            links++;
            if (links > 1 || !world.TryGetUInt(out var worldId) || !name.TryGetString(out var characterName))
                return null;
            linked = FocusCharacter.Create(characterName.ExtractText(), worldId);
        }
        if (links > 0)
            return linked;

        // Some channels carry a plain full name. Resolve it only against an unambiguous nearby player.
        var text = SeStringHighlighter.GetVisibleText(sender).Trim().Normalize(NormalizationForm.FormC);
        var matches = scene.Players.Select(p => p.Character).Append(scene.Self)
            .Where(p => string.Equals(p.Name, text, StringComparison.OrdinalIgnoreCase)).Distinct().Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
