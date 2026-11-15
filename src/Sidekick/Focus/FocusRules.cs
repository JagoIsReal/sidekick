using Dalamud.Game.Text;

namespace Sidekick.Focus;

public static class FocusRules
{
    public static bool ShouldDim(FocusSettings settings, XivChatType channel, FocusCharacter? sender,
        FocusCharacter? self, float? distanceYalms, bool isOwnMessage = false)
    {
        if (!settings.Enabled || settings.OtherOpacityPercent >= 100 || !settings.Channels.Contains(channel) ||
            !FocusChannels.All.Contains(channel) || sender is null || self is null || isOwnMessage ||
            sender.Value.Matches(self.Value))
            return false;

        var inRange = settings.UseDistance && distanceYalms is { } distance && float.IsFinite(distance) &&
            distance >= 0 && distance <= settings.DistanceYalms;
        var selected = settings.UseParticipants && settings.Contains(sender.Value);
        var focused = settings.UseDistance && settings.UseParticipants
            ? settings.Combination == FocusCombination.All ? inRange && selected : inRange || selected
            : inRange || selected;
        return !focused;
    }
}

internal enum FocusCommandAction { OpenSettings, Toggle, Enable, Disable, Usage }

internal static class FocusCommand
{
    internal static FocusCommandAction Parse(string arguments)
    {
        var parts = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return FocusCommandAction.OpenSettings;
        if (!parts[0].Equals("focus", StringComparison.OrdinalIgnoreCase))
            return FocusCommandAction.Usage;
        if (parts.Length == 1)
            return FocusCommandAction.Toggle;
        if (parts.Length != 2)
            return FocusCommandAction.Usage;
        return parts[1].ToLowerInvariant() switch
        {
            "on" => FocusCommandAction.Enable,
            "off" => FocusCommandAction.Disable,
            _ => FocusCommandAction.Usage,
        };
    }
}
