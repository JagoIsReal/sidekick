using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Lumina.Text.Expressions;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using Sidekick.Chat;
using Sidekick.Core;
using Builder = Lumina.Text.SeStringBuilder;

namespace Sidekick.Tests;

public sealed class SeStringOpacityTests
{
    [Theory]
    [InlineData(0xFFFF0000u, 0x40FF0000u)]
    [InlineData(0xFF00FF00u, 0x4000FF00u)]
    [InlineData(0xFF0000FFu, 0x400000FFu)]
    [InlineData(0xFF000000u, 0x40000000u)]
    public void DefaultColorKeepsItsRgbAndUsesQuarterOpacity(uint channel, uint expected)
    {
        var original = new SeString(new TextPayload("Visible text"));
        var bytes = original.Encode();
        Assert.True(SeStringOpacity.TryApply(original, channel, 25, new TestChatColors(), out var result));
        Assert.NotSame(original, result);
        Assert.Equal(bytes, original.Encode());
        Assert.Equal(original.TextValue, result.TextValue);
        Assert.Equal(expected, Assert.Single(ColorsAtText(result)));
    }

    [Fact]
    public void AlphaIsMultipliedRatherThanReplacedAndNestedColorsAreRestored()
    {
        var original = SeString.Parse(new Builder().Append("base")
            .PushColorBgra(0x80FF0000).Append("red")
            .PushColorBgra(0x400000FF).Append("blue").PopColor()
            .Append("red again").PopColor().Append("base again").ToArray());
        Assert.True(SeStringOpacity.TryApply(original, 0xFF123456, 25, new TestChatColors(), out var result));
        Assert.Equal([0x40123456u, 0x20FF0000u, 0x100000FFu, 0x20FF0000u, 0x40123456u], ColorsAtText(result));
        AssertOriginalPayloadsPreserved(original, result);
    }

    [Fact]
    public void HighlightingIsPreservedAndFocusOnlyChangesItsAlpha()
    {
        var input = new SeString(new TextPayload("Hello Ann!"));
        var highlighted = new ChatHighlighter(new Configuration()).Transform(input, CharacterIdentity.FromName("Ann Example"));
        var before = highlighted.Encode();
        Assert.True(SeStringOpacity.TryApply(highlighted, 0xFF112233, 25, new TestChatColors(), out var result));
        Assert.Equal(0x40FFD54Fu, Assert.Single(ColorsAtText(result)));
        Assert.Equal(before, highlighted.Encode());
    }

    [Fact]
    public void PaletteAndGlobalColorsAreResolvedWithoutChangingTheirOriginalPayloads()
    {
        var colors = new TestChatColors();
        colors.Palette[34] = (0xFF00FF00, 0xFFFF0000);
        colors.Globals[13] = 0x80445566;
        var original = SeString.Parse(new Builder().PushColorType(34).Append("palette").PopColorType()
            .BeginMacro(MacroCode.Color).BeginUnaryExpression(ExpressionType.GlobalNumber).AppendUIntExpression(13)
            .EndExpression().EndMacro().Append("dynamic").PopColor().Append("default").ToArray());
        Assert.True(SeStringOpacity.TryApply(original, 0xFF112233, 25, colors, out var result));
        Assert.Equal([0x4000FF00u, 0x20445566u, 0x40112233u], ColorsAtText(result));
        AssertOriginalPayloadsPreserved(original, result);
    }

    [Fact]
    public void EdgeAndShadowColorsAreAlsoAttenuatedInsideTheirExistingScopes()
    {
        var original = SeString.Parse(new Builder().PushEdgeColorBgra(0xFFFF0000).PushShadowColorBgra(0x800000FF)
            .Append("text").PopShadowColor().PopEdgeColor().ToArray());
        Assert.True(SeStringOpacity.TryApply(original, 0xFF112233, 25, new TestChatColors(), out var result));
        Assert.Equal(0x40FF0000u, Assert.Single(ColorsAtText(result, MacroCode.EdgeColor)));
        Assert.Equal(0x200000FFu, Assert.Single(ColorsAtText(result, MacroCode.ShadowColor)));
    }

    [Fact]
    public void LinksIconsItalicAndOpaqueMacrosArePreservedAndInputIsIndependent()
    {
        var original = SeString.Parse(new PlayerPayload("Iris Another", 22).Encode());
        original.Payloads.Insert(0, new EmphasisItalicPayload(true));
        original.Payloads.Add(new EmphasisItalicPayload(false));
        original.Payloads.Add(new IconPayload(BitmapFontIcon.ControllerDPadUp));
        original.Payloads.Add(new AutoTranslatePayload(1, 1));
        original.Payloads.Add(new RawPayload(new Builder().BeginMacro(MacroCode.NonBreakingSpace).EndMacro().ToArray()));
        var bytes = original.Encode();
        Assert.True(SeStringOpacity.TryApply(original, 0xFFAABBCC, 25, new TestChatColors(), out var result));
        AssertOriginalPayloadsPreserved(original, result);
        Assert.Equal(bytes, original.Encode());
        Assert.Equal(original.Payloads.OfType<TextPayload>().Select(p => p.Text),
            result.Payloads.OfType<TextPayload>().Select(p => p.Text));
        var link = Assert.Single(result.Payloads.OfType<PlayerPayload>());
        Assert.Equal("Iris Another", link.PlayerName);
        Assert.Single(result.Payloads.OfType<IconPayload>());
        Assert.Single(result.Payloads.OfType<AutoTranslatePayload>());
    }

    [Fact]
    public void UnknownDynamicColorsAbortTheWholeTransformationAndKeepOriginalReference()
    {
        var original = SeString.Parse(new Builder().Append("already visited")
            .BeginMacro(MacroCode.Color).BeginUnaryExpression(ExpressionType.GlobalNumber).AppendUIntExpression(13)
            .EndExpression().EndMacro().Append("unknown dynamic color").PopColor().ToArray());
        var bytes = original.Encode();
        Assert.False(SeStringOpacity.TryApply(original, 0xFFFFFFFF, 25, new TestChatColors(), out var result));
        Assert.Same(original, result);
        Assert.Equal(bytes, original.Encode());
        var palette = SeString.Parse(new Builder().PushColorType(999).Append("unknown palette").PopColorType().ToArray());
        Assert.False(SeStringOpacity.TryApply(palette, 0xFFFFFFFF, 25, new TestChatColors(), out result));
        Assert.Same(palette, result);
    }

    [Fact]
    public void AutoTranslateTextIsWrappedWithoutResolvingOrRebuildingItsContents()
    {
        var translation = new AutoTranslatePayload(1, 1);
        var original = new SeString(translation);
        Assert.True(SeStringOpacity.TryApply(original, 0xFFF7F7F7, 25, new TestChatColors(), out var result));
        Assert.NotSame(original, result);
        Assert.Equal(translation.Encode(), Assert.Single(result.Payloads.OfType<AutoTranslatePayload>()).Encode());
        var macroCodes = new List<MacroCode>();
        foreach (var part in new ReadOnlySeStringSpan(result.Encode()))
        {
            macroCodes.Add(part.MacroCode);
            if (part.MacroCode == MacroCode.Color && part.TryGetExpression(out var value) && value.TryGetUInt(out var argb))
                Assert.Equal(0x40F7F7F7u, argb);
        }
        Assert.Equal([MacroCode.Color, MacroCode.Fixed, MacroCode.Color], macroCodes);
    }

    [Fact]
    public void FullOpacityEmptyAndIconOnlyMessagesKeepTheirOriginalObjects()
    {
        foreach (var message in new[] { new SeString(), new SeString(new IconPayload(BitmapFontIcon.ControllerDPadUp)) })
        {
            Assert.True(SeStringOpacity.TryApply(message, 0xFFFFFFFF, 25, new TestChatColors(), out var result));
            Assert.Same(message, result);
        }
        var text = new SeString(new TextPayload("text"));
        Assert.True(SeStringOpacity.TryApply(text, 0xFFFFFFFF, 100, new TestChatColors(), out var full));
        Assert.Same(text, full);
    }

    [Theory]
    [InlineData(0xFF0000FFu, 0xFFFF0000u)]
    [InlineData(0x00FF00FFu, 0xFF00FF00u)]
    [InlineData(0x0000FFFFu, 0xFF0000FFu)]
    [InlineData(0x12345680u, 0x80123456u)]
    public void PaletteByteOrderIsExplicit(uint rgba, uint argb) => Assert.Equal(argb, GameChatColors.FromSheet(rgba));

    [Fact]
    public void TinyNonzeroAlphaNeverBecomesAnOmittedOpaqueAlphaInChatTwo()
    {
        Assert.Equal(0x01123456u, SeStringOpacity.ScaleAlpha(0x01123456, 1));
        Assert.Equal(0x00123456u, SeStringOpacity.ScaleAlpha(0x00123456, 25));
    }

    internal static uint[] ColorsAtText(SeString message, MacroCode code = MacroCode.Color)
    {
        var stack = new Stack<uint>();
        var values = new List<uint>();
        foreach (var part in new ReadOnlySeStringSpan(message.Encode()))
        {
            if (part.Type == ReadOnlySePayloadType.Text)
                values.Add(stack.Peek());
            else if (part.MacroCode == code && part.TryGetExpression(out var expression))
            {
                if (expression.TryGetUInt(out var value))
                    stack.Push(value);
                else if (expression.TryGetPlaceholderExpression(out var placeholder) && placeholder == (byte)ExpressionType.StackColor)
                    stack.TryPop(out _);
            }
        }
        Assert.Empty(stack);
        return values.ToArray();
    }

    private static void AssertOriginalPayloadsPreserved(SeString original, SeString output)
    {
        var remaining = new Queue<byte[]>(SeString.Parse(original.Encode()).Payloads.Select(p => p.Encode()));
        foreach (var payload in output.Payloads)
            if (remaining.TryPeek(out var next) && payload.Encode().SequenceEqual(next))
                remaining.Dequeue();
        Assert.Empty(remaining);
    }
}

internal sealed class TestChatColors : IChatColors
{
    internal uint? BaseColor { get; set; } = 0xFFF7F7F7;
    internal Dictionary<uint, (uint Foreground, uint Edge)> Palette { get; } = [];
    internal Dictionary<uint, uint> Globals { get; } = [];
    public uint? Channel(XivChatType channel) => BaseColor;
    public uint? Ui(uint id, bool edge) => Palette.TryGetValue(id, out var value) ? edge ? value.Edge : value.Foreground : null;
    public uint? Global(uint index) => Globals.TryGetValue(index, out var value) ? value : null;
}
