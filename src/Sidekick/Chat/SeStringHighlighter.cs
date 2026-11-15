using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Sidekick.Core;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using LuminaBuilder = Lumina.Text.SeStringBuilder;

namespace Sidekick.Chat;

public static class SeStringHighlighter
{
    public static string GetVisibleText(SeString message)
    {
        var text = new StringBuilder();
        foreach (var payload in message.Payloads)
        {
            if (payload is ITextProvider provider)
            {
                text.Append(provider.Text);
            }
            else if (payload is IconPayload)
            {
                text.Append(' ');
            }
            else if (payload is EmphasisItalicPayload or UIForegroundPayload or UIGlowPayload)
            {
                // Formatting is invisible and must not split a name.
            }
            else
            {
                // Some public link constructors encode both a link marker and its visible label.
                foreach (var part in new ReadOnlySeStringSpan(payload.Encode()))
                {
                    if (part.Type == ReadOnlySePayloadType.Text)
                    {
                        text.Append(Encoding.UTF8.GetString(part.Body));
                        continue;
                    }

                    switch (part.MacroCode)
                    {
                        case MacroCode.Color:
                        case MacroCode.EdgeColor:
                        case MacroCode.ShadowColor:
                        case MacroCode.ColorType:
                        case MacroCode.EdgeColorType:
                        case MacroCode.Link:
                        case MacroCode.Italic:
                        case MacroCode.SoftHyphen:
                            break;
                        case MacroCode.Hyphen:
                            text.Append('-');
                            break;
                        default:
                            // Unknown visible content must not join otherwise separate word fragments.
                            text.Append(' ');
                            break;
                    }
                }
            }
        }

        return text.ToString();
    }

    public static SeString Apply(SeString original, RgbColor color)
    {
        // Public Lumina builders encode RGB and the matching stack pop; no hand-written payload bytes.
        var open = new LuminaBuilder().PushColorRgba(color.Red, color.Green, color.Blue, 255).ToArray();
        var close = new LuminaBuilder().PopColor().ToArray();
        var result = new List<Payload>(original.Payloads.Count * 3);

        foreach (var payload in SeString.Parse(original.Encode()).Payloads)
        {
            if (payload is ITextProvider)
            {
                // A local push/pop also overrides colors inside links, then restores their original scope.
                result.Add(new RawPayload(open));
                result.Add(payload);
                result.Add(new RawPayload(close));
            }
            else
            {
                result.Add(payload);
            }
        }

        // Reparse into independent payload objects and finish serialization before publishing the result.
        return SeString.Parse(new SeString(result).Encode());
    }
}

