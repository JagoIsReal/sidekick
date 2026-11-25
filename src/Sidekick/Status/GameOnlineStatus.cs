using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Sidekick.Locations;

namespace Sidekick.Status;

internal sealed class GameOnlineStatus(IFramework framework, ILocationReader reader)
{
    internal unsafe bool TryApply(LocationSnapshot expected, uint target)
    {
        // Re-read the live context on the framework thread before submitting the native request.
        if (!framework.IsInFrameworkUpdateThread || !StatusTargets.Selectable.Contains(target)
            || reader.Read() != expected)
            return false;

        var detail = InfoProxyDetail.Instance();
        if (detail == null) return false;
        detail->SendOnlineStatusUpdate(target);
        return true;
    }
}
