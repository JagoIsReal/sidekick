using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Lumina.Text.Expressions;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using Builder = Lumina.Text.SeStringBuilder;

namespace Sidekick.Chat;

internal static class SeStringOpacity
{
    internal static bool TryApply(SeString original, uint channelArgb, int opacityPercent, IChatColors colors, out SeString result)
    {
        result = original;
        if (opacityPercent >= 100 || original.Payloads.Count == 0)
            return true;
        opacityPercent = Math.Clamp(opacityPercent, 1, 100);
        var foreground = new Stack<uint>();
        var edge = new Stack<uint>();
        var shadow = new Stack<uint>();
        var output = new Builder();
        var changed = false;

        // Work on encoded payloads, preserving links, unknown macros and their original ordering.
        foreach (var part in new ReadOnlySeStringSpan(original.Encode()))
        {
            if (part.Type == ReadOnlySePayloadType.Text || IsAutoTranslate(part))
            {
                output.PushColorBgra(ScaleAlpha(foreground.TryPeek(out var argb) ? argb : channelArgb, opacityPercent));
                if (edge.TryPeek(out var edgeArgb))
                    output.PushEdgeColorBgra(ScaleAlpha(edgeArgb, opacityPercent));
                if (shadow.TryPeek(out var shadowArgb))
                    output.PushShadowColorBgra(ScaleAlpha(shadowArgb, opacityPercent));
                output.Append(part);
                if (shadow.Count > 0)
                    output.PopShadowColor();
                if (edge.Count > 0)
                    output.PopEdgeColor();
                output.PopColor();
                changed = true;
                continue;
            }

            if (!Track(part, foreground, edge, shadow, colors))
                return false; // An unresolved dynamic color must never be replaced by a guessed RGB value.
            output.Append(part);
        }
        result = changed ? SeString.Parse(output.ToArray()) : original;
        return true;
    }

    internal static uint ScaleAlpha(uint argb, int percent)
    {
        var alpha = argb >> 24;
        // ChatTwo treats an omitted zero alpha byte as opaque. Keep nonzero alpha representable.
        var scaled = alpha == 0 ? 0 : Math.Max(1u, (alpha * (uint)Math.Clamp(percent, 1, 100) + 50) / 100);
        return (argb & 0xFFFFFFu) | (scaled << 24);
    }

    private static bool IsAutoTranslate(ReadOnlySePayloadSpan part) =>
        part.Type == ReadOnlySePayloadType.Macro && part.MacroCode == MacroCode.Fixed &&
        SeString.Parse(new Builder().Append(part).ToArray()).Payloads is [AutoTranslatePayload];

    private static bool Track(ReadOnlySePayloadSpan part, Stack<uint> foreground, Stack<uint> edge,
        Stack<uint> shadow, IChatColors colors)
    {
        var stack = part.MacroCode switch
        {
            MacroCode.Color or MacroCode.ColorType => foreground,
            MacroCode.EdgeColor or MacroCode.EdgeColorType => edge,
            MacroCode.ShadowColor => shadow,
            _ => null,
        };
        if (stack is null)
            return true;
        if (!part.TryGetExpression(out var expression))
            return false;
        if (expression.TryGetPlaceholderExpression(out var placeholder) && placeholder == (byte)ExpressionType.StackColor)
        {
            stack.TryPop(out _);
            return true;
        }
        if (!expression.TryGetUInt(out var value))
        {
            if (part.MacroCode is MacroCode.ColorType or MacroCode.EdgeColorType ||
                !expression.TryGetParameterExpression(out var type, out var operand) || type != (byte)ExpressionType.GlobalNumber ||
                !operand.TryGetUInt(out var index) || colors.Global(index) is not { } resolvedGlobal)
                return false;
            value = resolvedGlobal;
        }
        if (part.MacroCode is MacroCode.ColorType or MacroCode.EdgeColorType)
        {
            if (value == 0)
            {
                stack.TryPop(out _);
                return true;
            }
            if (colors.Ui(value, part.MacroCode == MacroCode.EdgeColorType) is not { } resolved)
                return false;
            value = resolved;
        }
        stack.Push(value);
        return true;
    }
}
