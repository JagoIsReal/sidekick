using Dalamud.Plugin.Services;
using Sidekick.Locations;
using Sidekick.Rules;

namespace Sidekick.Status;

internal sealed class StatusRuleController : IDisposable
{
    private readonly IFramework framework;
    private readonly IClientState client;
    private readonly ILocationReader reader;
    private readonly Func<LocationSnapshot, uint, bool> apply;
    private readonly IPluginLog log;
    private readonly StatusRuleSettings settings;
    private readonly RuleProfileSettings profiles;
    private readonly StableRuleContext stable = new();
    private long nextPoll;
    private bool loginPending;
    private StatusRule? lastConditionalWinner;
    private (LocationSnapshot Source, uint Target, long Deadline)? pending;
    internal LocationSnapshot? Current { get; private set; }
    internal bool Failed { get; private set; }

    internal StatusRuleController(IFramework framework, IClientState client, ILocationReader reader,
        Func<LocationSnapshot, uint, bool> apply, IPluginLog log, StatusRuleSettings settings, RuleProfileSettings profiles)
    {
        this.framework = framework;
        this.client = client;
        this.reader = reader;
        this.apply = apply;
        this.log = log;
        this.settings = settings;
        this.profiles = profiles;
        framework.Update += Update;
        client.Login += Login;
        client.Logout += Logout;
    }

    private void Login()
    {
        ResetSession();
        loginPending = settings.Enabled;
    }

    private void Logout(int _, int __) => ResetSession();

    private void ResetSession()
    {
        stable.Ready(null, false, 0);
        nextPoll = 0;
        loginPending = false;
        lastConditionalWinner = null;
        pending = null;
        Current = null;
        Failed = false;
    }

    private void Update(IFramework _) => Poll(Environment.TickCount64);

    internal void Poll(long now)
    {
        if (now < nextPoll) return;
        nextPoll = now + 500;
        try
        {
            var transitioning = reader.IsTransitioning;
            Current = transitioning ? null : reader.Read();
            if (!settings.Enabled)
            {
                loginPending = false;
                lastConditionalWinner = null;
                pending = null;
                Failed = false;
            }

            var context = Current is { Character.HomeWorldId: > 0, OnlineStatusId: not null, AppearanceBlocked: false }
                ? Current : null;
            if (settings.Enabled && pending is { } request)
            {
                if (context is not null && context.Character != request.Source.Character)
                    pending = null;
                else if (context?.OnlineStatusId is { } observed && StatusTargets.IsObserved(request.Target, observed))
                {
                    // Rebase the winner on our own status change so it cannot trigger another rule.
                    lastConditionalWinner = Winner(request.Source with { OnlineStatusId = observed }, atLogin: false);
                    pending = null;
                }
                else if (now >= request.Deadline)
                {
                    Failed = true;
                    pending = null;
                }
            }

            if (!stable.Ready(context, transitioning, now) || !settings.Enabled || context is null || pending is not null)
                return;

            var conditional = Winner(context, atLogin: false);
            var login = loginPending ? Winner(context, atLogin: true) : null;
            loginPending = false;
            var chosen = login ?? (conditional != lastConditionalWinner ? conditional : null);
            lastConditionalWinner = conditional;
            if (chosen?.TargetStatusId is not { } target) return;
            Failed = false;
            if (StatusTargets.IsObserved(target, context.OnlineStatusId!.Value)) return;
            Failed = !apply(context, target);
            if (!Failed) pending = (context, target, now + 5000);
        }
        catch (Exception ex)
        {
            if (!Failed) log.Warning("Sidekick status rules failed ({ErrorType}).", ex.GetType().Name);
            Failed = true;
        }
    }

    private StatusRule? Winner(LocationSnapshot context, bool atLogin)
    {
        StatusRule? account = null;
        for (var i = settings.Rules.Count - 1; i >= 0; i--)
        {
            var rule = settings.Rules[i];
            if (!rule.Enabled || rule.AtLogin != atLogin || !rule.IsValid
                || (rule.Conditions.HasSelection && !rule.Conditions.Matches(context))
                || !profiles.Matches(rule.ProfileId, context.Character))
                continue;
            if (rule.ProfileId.HasValue) return rule;
            account ??= rule;
        }
        return account;
    }

    public void Dispose()
    {
        framework.Update -= Update;
        client.Login -= Login;
        client.Logout -= Logout;
        ResetSession();
    }
}
