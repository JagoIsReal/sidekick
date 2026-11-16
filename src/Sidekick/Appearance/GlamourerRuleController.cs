using Dalamud.Plugin.Services;
using Sidekick.Locations;
using Sidekick.Rules;

namespace Sidekick.Appearance;

internal sealed class GlamourerRuleController : IDisposable
{
    private readonly IFramework framework;
    private readonly IClientState client;
    private readonly ILocationReader reader;
    private readonly IGlamourerGateway gateway;
    private readonly IPluginLog log;
    private readonly GlamourerSettings settings;
    private readonly GlamourerRuleEngine engine;
    private readonly StableRuleContext stable = new();
    private long nextPoll;
    private bool refresh = true;
    private bool retry = true;
    private bool reportedError;
    private bool disposed;
    internal LocationSnapshot? Current { get; private set; }
    internal bool Available { get; private set; }
    internal bool Failed { get; private set; }
    internal int ActiveCount { get; private set; }
    internal string? ActiveRuleName { get; private set; }
    internal int DeferredCount { get; private set; }
    internal bool Skipped { get; private set; }
    internal IReadOnlyDictionary<Guid, string> Designs { get; private set; } = new Dictionary<Guid, string>();

    internal GlamourerRuleController(IFramework framework, IClientState client, ILocationReader reader,
        IGlamourerGateway gateway, IPluginLog log, GlamourerSettings settings, RuleProfileSettings profiles)
    {
        this.framework = framework; this.client = client; this.reader = reader;
        this.gateway = gateway; this.log = log; this.settings = settings;
        engine = new GlamourerRuleEngine(settings, profiles, gateway);
        framework.Update += Update;
        client.Logout += Logout;
    }

    internal void Refresh()
    {
        refresh = true;
        nextPoll = 0;
        reportedError = false;
        retry = true;
    }

    private void Update(IFramework _) => Poll(Environment.TickCount64);

    internal void Poll(long now)
    {
        if (disposed || now < nextPoll) return;
        nextPoll = now + 500;
        try
        {
            var transitioning = reader.IsTransitioning;
            Current = transitioning ? null : reader.Read();
            var available = gateway.Available;
            if (available && !Available)
            {
                engine.Update(null, true);
                Refresh();
            }
            Available = available;
            if (!Available)
            {
                Designs = new Dictionary<Guid, string>();
                return;
            }
            if (retry) { engine.Retry(); retry = false; }
            if (refresh)
            {
                Designs = gateway.GetDesigns();
                refresh = false;
            }
            if (!stable.Ready(Current, transitioning, now) && settings.Enabled) return;
            engine.Update(Current, !settings.Enabled);
            ActiveCount = engine.ActiveCount;
            ActiveRuleName = engine.ActiveRuleName;
            DeferredCount = engine.DeferredCount;
            Skipped = engine.Skipped;
            Failed = engine.CleanupPending;
        }
        catch (Exception ex)
        {
            Failed = true;
            if (!reportedError)
            {
                log.Warning("Sidekick Glamourer rules failed ({ErrorType}); further warnings are suppressed until retry.", ex.GetType().Name);
                reportedError = true;
            }
        }
    }

    private void Logout(int _, int __)
    {
        try { if (gateway.Available) engine.Update(null, true); }
        catch (Exception ex) { log.Warning("Sidekick appearance restoration deferred ({ErrorType}).", ex.GetType().Name); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        framework.Update -= Update;
        client.Logout -= Logout;
        Logout(0, 0);
        if (engine.ActiveCount > 0)
            log.Warning("Sidekick could not restore some unavailable characters' appearances before unloading.");
    }
}
