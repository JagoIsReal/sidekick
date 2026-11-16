using Sidekick.Locations;

namespace Sidekick.Rules;

// Only categorical values enter the snapshot, so changing seconds/position cannot starve the delay.
internal sealed class StableRuleContext
{
    private LocationSnapshot? candidate;
    private long stableSince;

    internal bool Ready(LocationSnapshot? context, bool transitioning, long now)
    {
        if (transitioning || context is null)
        {
            candidate = null;
            stableSince = now;
            return !transitioning;
        }
        if (candidate != context)
        {
            candidate = context;
            stableSince = now;
            return false;
        }
        return now - stableSince >= 1000;
    }
}
