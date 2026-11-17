using Dalamud.Plugin.Services;
using Sidekick.Locations;
using Sidekick.Rules;

namespace Sidekick.Mods;

internal sealed class ModRuleController : IDisposable
{
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly ILocationReader reader;
    private readonly ModRuleSettings settings;
    private readonly IPenumbraGateway penumbra;
    private readonly ModRuleEngine engine;
    private readonly RuleProfileSettings? profiles;
    private readonly IEarlyHousingReader? earlyReader;
    private LocationSnapshot? lastEarly;
    private int earlyRevision = -1;
    private int earlyProfileRevision = -1;
    private bool wasTransitioning;
    private long nextPoll;
    private readonly StableRuleContext stable = new();
    private bool errorLogged;
    private bool disposed;
    internal LocationSnapshot? Current { get; private set; }
    internal bool Available { get; private set; }
    internal bool Failed { get; private set; }
    internal int ActiveCount => engine.ActiveCount;
    internal int SkippedCount => engine.SkippedCount;
    internal int LastEarlyApplications { get; private set; }
    internal IReadOnlyDictionary<string, string> Mods { get; private set; } = new Dictionary<string, string>();
    internal IReadOnlyDictionary<Guid, string> Collections { get; private set; } = new Dictionary<Guid, string>();
    private bool refreshLists = true;

    internal ModRuleController(IFramework framework, IPluginLog log, ILocationReader reader,
        IPenumbraGateway penumbra, ModRuleSettings settings, RuleProfileSettings? profiles = null,
        IEarlyHousingReader? earlyReader = null)
    {
        this.framework = framework;
        this.log = log;
        this.reader = reader;
        this.penumbra = penumbra;
        this.settings = settings;
        this.profiles = profiles;
        this.earlyReader = earlyReader;
        engine = new ModRuleEngine(settings, penumbra, profiles);
        framework.Update += Update;
    }

    internal void Refresh()
    {
        RefreshLists();
        engine.Retry();
        lastEarly = null;
        errorLogged = false;
    }

    internal void RefreshLists()
    {
        refreshLists = true;
        nextPoll = 0;
    }

    private void Update(IFramework _) => Poll(Environment.TickCount64);

    internal void Poll(long now)
    {
        if (disposed)
            return;
        try
        {
            var transitioning = reader.IsTransitioning;
            // Sample every framework frame when opted in; 500 ms can miss the loading window.
            var earlyEnabled = settings.Enabled && settings.Rules.Any(r => r.Enabled && r.IsValid
                && r.EarlyApplication && r.SupportsEarlyApplication);
            var earlyLocation = earlyEnabled ? earlyReader?.Read(transitioning) : null;
            if (!earlyEnabled) earlyReader?.Reset();
            if (transitioning != wasTransitioning)
            {
                lastEarly = null;
                if (transitioning) LastEarlyApplications = 0;
                wasTransitioning = transitioning;
                nextPoll = 0;
            }
            var poll = now >= nextPoll;
            if (poll)
            {
                nextPoll = now + 500;
                var available = penumbra.Available;
                if (available && !Available)
                {
                    engine.Update(null, null, deferRefresh: transitioning);
                    refreshLists = true;
                    engine.Retry();
                    lastEarly = null;
                }
                Available = available;
                if (!available)
                {
                    Mods = new Dictionary<string, string>();
                    Collections = new Dictionary<Guid, string>();
                    return;
                }
                if (refreshLists)
                {
                    Mods = penumbra.GetMods();
                    Collections = penumbra.GetCollections();
                    refreshLists = false;
                }
            }
            if (Available && transitioning && earlyLocation is not null
                && (lastEarly != earlyLocation || earlyRevision != settings.Revision
                    || earlyProfileRevision != (profiles?.Revision ?? 0) || poll && engine.CleanupPending))
            {
                lastEarly = earlyLocation;
                earlyRevision = settings.Revision;
                earlyProfileRevision = profiles?.Revision ?? 0;
                try
                {
                    engine.Update(earlyLocation, null, early: true);
                }
                finally
                {
                    LastEarlyApplications += engine.AppliedCount;
                    Failed = engine.CleanupPending;
                    if (engine.AppliedCount > 0)
                        log.Debug("Sidekick applied {Count} temporary Penumbra settings during a housing transition.", engine.AppliedCount);
                }
            }
            if (!poll || !Available) return;
            var location = transitioning ? null : reader.Read();
            Current = location;
            // Loading screens do not briefly turn a matching rule off and on.
            // Explicit module disable still cleans up immediately during a transition.
            if (transitioning)
            {
                Current = null;
                stable.Ready(null, true, now);
                if (!settings.Enabled)
                    engine.Update(null, null, deferRefresh: true);
                return;
            }
            if (!stable.Ready(location, false, now)) return;
            engine.Update(location, location is null ? null : penumbra.CurrentCollection());
            Failed = engine.CleanupPending;
            if (Failed && !errorLogged)
            {
                log.Warning("Sidekick has pending temporary Penumbra settings cleanup; cleanup will be retried.");
                errorLogged = true;
            }
        }
        catch (Exception ex)
        {
            Failed = true;
            if (!errorLogged)
            {
                log.Warning("Sidekick location/Penumbra integration failed ({ErrorType}). Rules will be retried.", ex.GetType().Name);
                errorLogged = true;
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        framework.Update -= Update;
        try
        {
            if (penumbra.Available)
                engine.Update(null, null, deferRefresh: wasTransitioning);
            if (engine.ActiveCount > 0)
                log.Warning("Sidekick could not release all temporary Penumbra settings. Check Penumbra's temporary settings.");
        }
        catch (Exception ex)
        {
            log.Warning("Sidekick could not release temporary Penumbra settings ({ErrorType}).", ex.GetType().Name);
        }
    }
}
