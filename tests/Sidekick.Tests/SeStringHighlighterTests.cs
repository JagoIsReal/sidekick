using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Sidekick.Chat;
using Sidekick.Core;
using Lumina.Text.Expressions;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using Builder = Lumina.Text.SeStringBuilder;

namespace Sidekick.Tests;

public sealed class SeStringHighlighterTests
{
    private static readonly CharacterIdentity Ann = CharacterIdentity.FromName("Ann Example")!;

    [Theory]
    [InlineData(false, "Hello Ann!")]
    [InlineData(true, "Hello Annabelle!")]
    [InlineData(true, "")]
    public void UnaffectedMessageRetainsObjectAndBytes(bool enabled, string text)
    {
        var original = new SeString(new TextPayload(text));
        var before = original.Encode();
        var highlighter = new ChatHighlighter(new Configuration { Enabled = enabled });
        Assert.Same(original, highlighter.Transform(original, Ann));
        Assert.Equal(before, original.Encode());
    }

    [Fact]
    public void NoIdentityLeavesMessageUntouched()
    {
        var original = new SeString(new TextPayload("Ann"));
        Assert.Same(original, new ChatHighlighter(new Configuration()).Transform(original, null));
    }

    [Fact]
    public void MentionAcrossFormattingBoundariesIsDetected()
    {
        var original = new SeString(new TextPayload("Hello A"), new EmphasisItalicPayload(true),
            new TextPayload("nn!"), new EmphasisItalicPayload(false));
        Assert.Equal("Hello Ann!", SeStringHighlighter.GetVisibleText(original));
        Assert.NotSame(original, new ChatHighlighter(new Configuration()).Transform(original, Ann));
    }

    [Fact]
    public void IconDoesNotJoinTwoFragmentsIntoAName()
    {
        var original = new SeString(new TextPayload("A"), new IconPayload(BitmapFontIcon.ControllerDPadUp), new TextPayload("nn"));
        Assert.Same(original, new ChatHighlighter(new Configuration()).Transform(original, Ann));
    }

    [Fact]
    public void LinksStylesAndUnknownPayloadsArePreservedInOrder()
    {
        var unknown = new Builder().BeginMacro(MacroCode.NonBreakingSpace).EndMacro().ToArray();
        var original = SeString.Parse(new PlayerPayload("Ann Example", 21).Encode());
        original.Payloads.Insert(1, new EmphasisItalicPayload(true));
        original.Payloads.Insert(original.Payloads.Count - 1, new EmphasisItalicPayload(false));
        original.Payloads.Add(new RawPayload(unknown));
        original.Payloads.Add(new TextPayload(" says hello."));
        var bytes = original.Encode();
        var transformed = new ChatHighlighter(new Configuration()).Transform(original, Ann);
        Assert.NotSame(original, transformed);
        Assert.Equal(bytes, original.Encode());
        Assert.Equal(original.TextValue, transformed.TextValue);

        var originalPayloads = original.Payloads.Select(payload => payload.Encode()).ToArray();
        var keptPayloads = transformed.Payloads.Where(payload => !IsRgbColor(payload)).Select(payload => payload.Encode()).ToArray();
        Assert.Equal(originalPayloads.Length, keptPayloads.Length);
        for (var i = 0; i < originalPayloads.Length; i++)
            Assert.Equal(originalPayloads[i], keptPayloads[i]);
    }

    [Fact]
    public void ConstructedPlayerLinkIsMatchedAndColoredWithoutDuplicatingItsLabel()
    {
        var original = new SeString(new PlayerPayload("Ann Example", 21));
        var output = new ChatHighlighter(new Configuration()).Transform(original, Ann);
        Assert.NotSame(original, output);
        Assert.Equal("Ann Example", output.TextValue);
        Assert.Equal("Ann Example", SeStringHighlighter.GetVisibleText(output));
        Assert.Single(output.Payloads.OfType<PlayerPayload>());
        Assert.Equal(2, output.Payloads.Count(IsRgbColor));
    }

    [Fact]
    public void AutotranslateAndIconBytesSurviveColoringWithoutResolvingGameData()
    {
        var translation = new AutoTranslatePayload(1, 1);
        var icon = new IconPayload(BitmapFontIcon.ControllerDPadUp);
        var original = new SeString(new TextPayload("Ann "), translation, icon);
        var before = original.Encode();
        var output = SeStringHighlighter.Apply(original, RgbColor.FromRgb(RgbColor.DefaultRgb));
        Assert.Equal(before, original.Encode());
        Assert.Equal(translation.Encode(), Assert.Single(output.Payloads.OfType<AutoTranslatePayload>()).Encode());
        Assert.Equal(icon.Encode(), Assert.Single(output.Payloads.OfType<IconPayload>()).Encode());
    }

    [Fact]
    public void RawFormattingDoesNotBreakMentionAndOpaquePayloadDoesNotJoinFragments()
    {
        var italic = new Builder().BeginMacro(MacroCode.Italic).AppendUIntExpression(1).EndMacro().ToArray();
        var formatted = new SeString(new TextPayload("A"), new RawPayload(italic), new TextPayload("nn"));
        Assert.Equal("Ann", SeStringHighlighter.GetVisibleText(formatted));

        var separator = new Builder().BeginMacro(MacroCode.NonBreakingSpace).EndMacro().ToArray();
        var separated = new SeString(new TextPayload("A"), new RawPayload(separator), new TextPayload("nn"));
        Assert.Same(separated, new ChatHighlighter(new Configuration()).Transform(separated, Ann));
    }

    [Theory]
    [InlineData(0xFF0000u)]
    [InlineData(0x00FF00u)]
    [InlineData(0x0000FFu)]
    [InlineData(0xFFD54Fu)]
    public void EachTextSegmentHasBalancedRgbScopeAndExistingColorsSurvive(uint rgb)
    {
        var input = new Builder().PushColorRgba(1, 2, 3, 255).Append("Ann")
            .PushColorRgba(4, 5, 6, 255).Append(" says hello").PopColor().PopColor().ToArray();
        var configuration = new Configuration { HighlightColorRgb = rgb };
        var output = new ChatHighlighter(configuration).Transform(SeString.Parse(input), Ann);
        var colors = new Stack<uint>();
        var texts = 0;
        foreach (var payload in new ReadOnlySeStringSpan(output.Encode()))
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                Assert.Equal(0xFF000000u | rgb, colors.Peek());
                texts++;
            }
            else if (payload.MacroCode == MacroCode.Color && payload.TryGetExpression(out var expression))
            {
                if (expression.TryGetUInt(out var argb))
                    colors.Push(argb);
                else
                {
                    Assert.True(expression.TryGetPlaceholderExpression(out var placeholder));
                    Assert.Equal((int)ExpressionType.StackColor, (int)placeholder);
                    Assert.NotEmpty(colors);
                    colors.Pop();
                }
            }
        }

        Assert.Equal(2, texts);
        Assert.Empty(colors);
    }

    private static bool IsRgbColor(Payload payload)
    {
        foreach (var part in new ReadOnlySeStringSpan(payload.Encode()))
            if (part.Type == ReadOnlySePayloadType.Macro && part.MacroCode == MacroCode.Color)
                return true;
        return false;
    }
}

