using System.Runtime.ExceptionServices;
using Sidekick.Locations;
using Sidekick.Rules;

namespace Sidekick.Mods;

internal sealed class ModRuleEngine(ModRuleSettings settings, IPenumbraGateway penumbra, RuleProfileSettings? profiles = null)
{
    private sealed record Desired(Guid Rule, ModAction Action, ModRefreshMode RefreshMode);
    private sealed record Held(Desired Desired, ModState State);
    private readonly Dictionary<(Guid Collection, string Mod), Held> held = [];
    private readonly Dictionary<(Guid Collection, string Mod), Desired> attempted = [];
    private readonly HashSet<ModRefreshRequest> pendingRedraw = [];
    private int revision = -1;
    private int profileRevision = -1;
    internal int ActiveCount => held.Count;
    internal int SkippedCount { get; private set; }
    internal bool CleanupPending { get; private set; }
    internal int AppliedCount { get; private set; }

    internal void Retry() => attempted.Clear();

    internal void Update(LocationSnapshot? location, Guid? characterCollection, bool early = false, bool deferRefresh = false)
    {
        AppliedCount = 0;
        if (revision != settings.Revision || profileRevision != (profiles?.Revision ?? 0))
        {
            attempted.Clear();
            revision = settings.Revision;
            profileRevision = profiles?.Revision ?? 0;
        }
        var desired = new Dictionary<(Guid Collection, string Mod), Desired>();
        if (early && settings.Enabled)
        {
            // Preserve the old location's effects and manual-edit suppression until arrival.
            foreach (var entry in attempted) desired[entry.Key] = entry.Value;
            foreach (var entry in held) desired[entry.Key] = entry.Value.Desired;
        }
        if (settings.Enabled && location is not null)
        {
            // Last matching rule wins per collection/mod, including its refresh policy.
            foreach (var rule in settings.Rules.OrderBy(r => r.ProfileId.HasValue))
            {
                if (rule.Enabled && rule.IsValid && (!early || rule.EarlyApplication && rule.SupportsEarlyApplication)
                    && rule.Location.Matches(location)
                    && (rule.ProfileId is null || profiles?.Matches(rule.ProfileId, location.Character) == true))
                {
                    var target = rule.CollectionId ?? characterCollection;
                    // Never fall back to the character's collection when a target is unavailable.
                    if (target is not { } id || id == Guid.Empty)
                        continue;
                    foreach (var mod in rule.Mods)
                        desired[(id, mod)] = new Desired(rule.Id, rule.Action, rule.RefreshMode);
                }
            }
        }

        Exception? failure = null;
        CleanupPending = false;
        foreach (var (key, value) in held.ToArray())
        {
            try
            {
                if (desired.TryGetValue(key, out var next) && next == value.Desired)
                {
                    // A manual edit/removal wins until this rule is activated again.
                    if (!penumbra.IsApplied(key.Collection, key.Mod, value.State))
                    {
                        if (!penumbra.Release(key.Collection, key.Mod, value.State))
                        {
                            CleanupPending = true;
                            continue;
                        }
                        held.Remove(key);
                    }
                    continue;
                }
                if (!penumbra.Release(key.Collection, key.Mod, value.State))
                {
                    CleanupPending = true;
                    continue;
                }
                held.Remove(key);
                pendingRedraw.Add(new ModRefreshRequest(key.Collection, value.Desired.RefreshMode));
            }
            catch (Exception exception)
            {
                // One unavailable mod must not block cleanup of other overrides.
                CleanupPending = true;
                failure ??= exception;
            }
        }
        foreach (var key in attempted.Keys.Where(k => !desired.ContainsKey(k)).ToArray())
            attempted.Remove(key);

        SkippedCount = 0;
        foreach (var (key, value) in desired)
        {
            try
            {
                if (held.ContainsKey(key))
                    continue;
                if (CleanupPending)
                {
                    // Finish releasing the original scope before acquiring another one.
                    SkippedCount++;
                    continue;
                }
                if (attempted.TryGetValue(key, out var previous) && previous == value)
                {
                    SkippedCount++;
                    continue;
                }
                attempted[key] = value;
                var baseline = penumbra.ReadPermanent(key.Collection, key.Mod);
                if (baseline is null || penumbra.HasTemporary(key.Collection, key.Mod))
                {
                    SkippedCount++;
                    continue;
                }
                var enabled = value.Action switch
                {
                    ModAction.Enable => true,
                    ModAction.Disable => false,
                    ModAction.Toggle => !baseline.Enabled,
                    _ => baseline.Enabled,
                };
                var applied = baseline with { Enabled = enabled };
                // Track before crossing IPC, including a provider that throws after applying a change.
                held[key] = new Held(value, applied);
                var refresh = new ModRefreshRequest(key.Collection, value.RefreshMode);
                var alreadyPending = !pendingRedraw.Add(refresh);
                if (!penumbra.Apply(key.Collection, key.Mod, applied))
                {
                    held.Remove(key);
                    if (!alreadyPending)
                        pendingRedraw.Remove(refresh);
                    SkippedCount++;
                }
                else
                    AppliedCount++;
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }
        // Keep refreshes queued: actors and furniture may not exist during loading.
        foreach (var request in early || deferRefresh ? [] : CoalesceRefreshes())
        {
            try
            {
                penumbra.Redraw(request.Collection, request.Mode);
                pendingRedraw.RemoveWhere(p => request.Mode switch
                {
                    ModRefreshMode.All => p.Mode != ModRefreshMode.Furniture,
                    ModRefreshMode.CollectionMembers => p.Mode == request.Mode && p.Collection == request.Collection,
                    _ => p.Mode == request.Mode,
                });
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private ModRefreshRequest[] CoalesceRefreshes()
    {
        var plan = new List<ModRefreshRequest>();
        if (pendingRedraw.Any(r => r.Mode == ModRefreshMode.All))
            plan.Add(pendingRedraw.First(r => r.Mode == ModRefreshMode.All));
        else
        {
            plan.AddRange(pendingRedraw.Where(r => r.Mode == ModRefreshMode.CollectionMembers));
            plan.AddRange(pendingRedraw.Where(r => r.Mode == ModRefreshMode.Self).Take(1));
        }
        // RedrawAll covers actors, not housing furniture. Keep this request independent.
        plan.AddRange(pendingRedraw.Where(r => r.Mode == ModRefreshMode.Furniture).Take(1));
        return plan.ToArray();
    }
}
