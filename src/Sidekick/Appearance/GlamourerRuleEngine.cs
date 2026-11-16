using Sidekick.Locations;
using Sidekick.Rules;

namespace Sidekick.Appearance;

internal sealed class GlamourerRuleEngine(GlamourerSettings settings, RuleProfileSettings profiles, IGlamourerGateway glamourer)
{
    private sealed record Held(Guid Rule, Guid Design, string Baseline, string Applied);
    private readonly Dictionary<(string Name, uint World), Held> held = [];
    private ((string, uint) Actor, Guid Rule, Guid Design)? attempted;
    private bool retryRequested;
    private (string Name, uint World)? currentActor;
    internal int ActiveCount => held.Count;
    internal string? ActiveRuleName => currentActor is { } actor && held.TryGetValue(actor, out var current)
        ? settings.Rules.FirstOrDefault(r => r.Id == current.Rule)?.Name : null;
    internal int DeferredCount { get; private set; }
    internal bool CleanupPending { get; private set; }
    internal bool Skipped { get; private set; }
    internal void Retry()
    {
        attempted = null;
        retryRequested = true;
    }

    internal void Update(LocationSnapshot? context, bool release = false)
    {
        CleanupPending = false;
        Skipped = false;
        if (!release && context?.AppearanceBlocked == true) return;
        var character = glamourer.Character;
        if (character is null || character.HomeWorldId == 0)
        {
            currentActor = null;
            DeferredCount = held.Count;
            return;
        }
        var actor = (character.DisplayName.ToUpperInvariant(), character.HomeWorldId);
        var explicitRetry = retryRequested;
        retryRequested = false;
        currentActor = actor;
        DeferredCount = held.Count(p => p.Key != actor);
        // Account is evaluated first, then character rules; last match wins within a profile.
        var desired = !release && settings.Enabled && context?.Character is { } identity
            && identity.HomeWorldId == character.HomeWorldId
            && string.Equals(identity.DisplayName, character.DisplayName, StringComparison.OrdinalIgnoreCase)
            ? settings.Rules.OrderBy(r => r.ProfileId.HasValue).LastOrDefault(r => r.Enabled && r.IsValid
                && profiles.Matches(r.ProfileId, identity) && r.Conditions.Matches(context)) : null;
        held.TryGetValue(actor, out var previous);
        if (desired is null) attempted = null;
        if (previous is null && desired is null) return;
        var observed = glamourer.Capture(character);
        if (observed is null)
        {
            CleanupPending = previous is not null;
            Skipped = desired is not null;
            return;
        }
        if (previous is not null && !GlamourerGateway.SameState(observed, previous.Applied))
        {
            // Never restore over a foreign change. An explicit retry adopts it as the new baseline.
            held.Remove(actor);
            previous = null;
            if (desired is null) return;
            if (!explicitRetry)
            {
                attempted = (actor, desired.Id, desired.DesignId);
                return;
            }
        }
        if (previous is not null)
        {
            if (desired is not null && previous.Rule == desired.Id && previous.Design == desired.DesignId) return;
            if (!glamourer.Restore(character, previous.Baseline))
            {
                CleanupPending = true;
                return;
            }
            held.Remove(actor);
            observed = glamourer.Capture(character);
        }
        if (desired is null || observed is null) return;
        var attempt = (actor, desired.Id, desired.DesignId);
        if (attempted == attempt || held.Count >= RuleProfileSettings.MaxProfiles)
        {
            Skipped = true;
            return;
        }
        attempted = attempt;
        if (!glamourer.Apply(character, desired.DesignId))
        {
            Skipped = true;
            return;
        }
        var applied = glamourer.Capture(character);
        if (applied is null)
            throw new InvalidOperationException("Glamourer did not provide state after applying a design.");
        held[actor] = new Held(desired.Id, desired.DesignId, observed, applied);
    }
}
