using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Sidekick.Chat;
using Sidekick.Core;
using Sidekick.Keywords;
using Newtonsoft.Json;

namespace Sidekick.Tests;

public sealed class KeywordTests
{
    private static readonly CharacterIdentity Ann = CharacterIdentity.FromName("Ann Example", 21)!;
    private static readonly CharacterIdentity OtherWorld = CharacterIdentity.FromName("Ann Example", 22)!;
    private static readonly CharacterIdentity Iris = CharacterIdentity.FromName("Iris Other", 21)!;
    private static readonly KeywordProfile AnnProfile = KeywordProfile.ForCharacter("Ann Example", 21)!;

    [Fact]
    public void CharacterKeywordRequiresTheExactNameAndHomeWorld()
    {
        var configuration = new Configuration();
        Assert.Equal(KeywordEditResult.Success, configuration.Keywords.TrySave(AnnProfile, "sunflower"));
        var highlighter = new ChatHighlighter(configuration);
        Assert.True(Matches(highlighter, "A sunflower!", Ann));
        Assert.False(Matches(highlighter, "A sunflower!", OtherWorld));
        Assert.False(Matches(highlighter, "A sunflower!", Iris));
        Assert.False(Matches(highlighter, "A sunflower!", CharacterIdentity.FromName("Ann Example")));
        Assert.True(Matches(highlighter, "A sunflower!", CharacterIdentity.FromName("ANN EXAMPLE", 21)));
    }

    [Fact]
    public void AccountCheckboxMakesTheKeywordGlobalWithoutRequiringAnAccountId()
    {
        var configuration = new Configuration();
        configuration.Keywords.TrySave(KeywordProfile.Global, "sunflower");
        var highlighter = new ChatHighlighter(configuration);
        Assert.True(Matches(highlighter, "sunflower", Ann));
        Assert.True(Matches(highlighter, "sunflower", Iris));
        Assert.True(Matches(highlighter, "sunflower", OtherWorld));
        Assert.True(Matches(highlighter, "sunflower", CharacterIdentity.FromName("No World")));
        Assert.False(Matches(highlighter, "sunflower", null));
    }

    [Theory]
    [InlineData("raid", "RAID!", true)]
    [InlineData("raid", "raiding", false)]
    [InlineData("raid", "raid-party", false)]
    [InlineData("raid", "raid_1", false)]
    [InlineData("raid ce soir", "Un raid ce soir ?", true)]
    [InlineData("raid ce soir", "Un raid  ce soir ?", false)]
    [InlineData("café", "CAFE\u0301!", true)]
    [InlineData("café", "cafe", false)]
    [InlineData("[rp]", "Hello [RP]!", true)]
    public void KeywordsUseTheSharedLiteralUnicodeRules(string keyword, string text, bool expected)
    {
        var configuration = new Configuration();
        configuration.Keywords.TrySave(KeywordProfile.Global, keyword);
        Assert.Equal(expected, Matches(new ChatHighlighter(configuration), text, Ann));
    }

    [Fact]
    public void GlobalCharacterAndAutomaticNameTriggersAreCombined()
    {
        var configuration = new Configuration();
        configuration.Keywords.TrySave(KeywordProfile.Global, "sunflower");
        configuration.Keywords.TrySave(AnnProfile, "moonlight");
        var highlighter = new ChatHighlighter(configuration);
        Assert.True(Matches(highlighter, "sunflower", Ann));
        Assert.True(Matches(highlighter, "moonlight", Ann));
        Assert.True(Matches(highlighter, "Hi Ann!", Ann));
        Assert.False(Matches(highlighter, "moonlight", Iris));
        configuration.Enabled = false;
        Assert.False(Matches(highlighter, "sunflower", Ann));
    }

    [Fact]
    public void EditsDisableRemoveAndScopeChangesInvalidateTheMatcherImmediately()
    {
        var configuration = new Configuration();
        var keywords = configuration.Keywords;
        keywords.TrySave(AnnProfile, "sunflower");
        var rule = Assert.Single(keywords.Rules);
        var id = rule.Id;
        var highlighter = new ChatHighlighter(configuration);
        Assert.False(Matches(highlighter, "sunflower", Iris));
        Assert.Equal(KeywordEditResult.Success, keywords.TrySave(KeywordProfile.Global, rule.Text, id));
        Assert.True(rule.Account);
        Assert.Empty(rule.CharacterName);
        Assert.True(Matches(highlighter, "sunflower", Iris));
        Assert.True(keywords.SetEnabled(id, false));
        Assert.False(Matches(highlighter, "sunflower", Iris));
        keywords.SetEnabled(id, true);
        Assert.Equal(KeywordEditResult.Success, keywords.TrySave(AnnProfile, "moonlight", id));
        Assert.Equal(id, rule.Id);
        Assert.False(rule.Account);
        Assert.False(Matches(highlighter, "sunflower", Ann));
        Assert.True(Matches(highlighter, "moonlight", Ann));
        Assert.False(Matches(highlighter, "moonlight", Iris));
        Assert.True(keywords.Remove(id));
        Assert.False(Matches(highlighter, "moonlight", Ann));
    }

    [Fact]
    public void DuplicateCheckIsNormalizedAndScopedAndFailedMoveIsAtomic()
    {
        var settings = new KeywordSettings();
        Assert.Equal(KeywordEditResult.Success, settings.TrySave(AnnProfile, "  café  "));
        Assert.Equal(KeywordEditResult.Duplicate, settings.TrySave(AnnProfile, "CAFE\u0301"));
        Assert.Equal(KeywordEditResult.Success, settings.TrySave(KeywordProfile.Global, "café"));
        var local = settings.ForProfile(AnnProfile).Single();
        var revision = settings.Revision;
        Assert.Equal(KeywordEditResult.Duplicate, settings.TrySave(KeywordProfile.Global, "café", local.Id));
        Assert.False(local.Account);
        Assert.Equal("Ann Example", local.CharacterName);
        Assert.Equal(revision, settings.Revision);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("line\nbreak")]
    [InlineData("tab\tword")]
    [InlineData("\0")]
    public void InvalidKeywordsAreRejected(string text) => Assert.Equal(KeywordEditResult.InvalidText,
        new KeywordSettings().TrySave(KeywordProfile.Global, text));

    [Fact]
    public void MalformedUtf16IsRejected() => Assert.Equal(KeywordEditResult.InvalidText,
        new KeywordSettings().TrySave(KeywordProfile.Global, new string((char)0xD800, 1)));

    [Fact]
    public void LimitsAreIndependentPerTargetAndDoNotPreventEditing()
    {
        var settings = new KeywordSettings();
        for (var i = 0; i < KeywordSettings.MaxRulesPerProfile; i++)
            Assert.Equal(KeywordEditResult.Success, settings.TrySave(AnnProfile, $"keyword{i}"));
        Assert.Equal(KeywordEditResult.LimitReached, settings.TrySave(AnnProfile, "overflow"));
        Assert.Equal(KeywordEditResult.Success, settings.TrySave(KeywordProfile.Global, "overflow"));
        Assert.Equal(KeywordEditResult.Success, settings.TrySave(AnnProfile, "edited", settings.Rules[0].Id));
        Assert.Equal(KeywordEditResult.InvalidText, settings.TrySave(KeywordProfile.Global, new string('a', KeywordSettings.MaxTextLength + 1)));
    }

    [Theory]
    [InlineData("Ann", 21u)]
    [InlineData("One Two Three", 21u)]
    [InlineData("Ann Example", 0u)]
    [InlineData("Ann\nExample", 21u)]
    public void NamedProfileRequiresAFullNameAndHomeWorld(string name, uint world) =>
        Assert.Null(KeywordProfile.ForCharacter(name, world));

    [Fact]
    public void OfflineProfileCreationAndJsonRoundTripRetainRulesAndStableIds()
    {
        var configuration = new Configuration();
        configuration.Keywords.TrySave(KeywordProfile.ForCharacter("Iris Other", 22), "raid");
        configuration.Keywords.TrySave(KeywordProfile.Global, "café");
        var id = configuration.Keywords.Rules[0].Id;
        configuration.Keywords.SetEnabled(id, false);
        var json = JsonConvert.SerializeObject(configuration);
        Assert.DoesNotContain("Revision", json);
        var restored = JsonConvert.DeserializeObject<Configuration>(json)!;
        restored.ValidateAndMigrate();
        Assert.Equal(2, restored.Keywords.Rules.Count);
        Assert.Equal(id, restored.Keywords.Rules[0].Id);
        Assert.False(restored.Keywords.Rules[0].Enabled);
        Assert.Equal(22u, restored.Keywords.Rules[0].HomeWorldId);
        Assert.True(restored.Keywords.Rules[1].Account);
    }

    [Fact]
    public void GeneralResetPreservesKeywords()
    {
        var configuration = new Configuration();
        configuration.Keywords.TrySave(KeywordProfile.Global, "raid");
        configuration.Reset();
        Assert.Equal("raid", Assert.Single(configuration.Keywords.Rules).Text);
    }

    [Fact]
    public void ValidationRepairsBadEntriesWithoutTurningThemIntoGlobalRules()
    {
        var configuration = new Configuration
        {
            Keywords = new KeywordSettings
            {
                Rules =
            [
                new() { Text = "unbound" },
                new() { Text = "  café ", Account = true, Id = Guid.Empty },
                new() { Text = "CAFE\u0301", Account = true },
                new() { Text = "\n", Account = true },
                null!,
            ]
            },
        };
        configuration.ValidateAndMigrate();
        var rule = Assert.Single(configuration.Keywords.Rules);
        Assert.Equal("café", rule.Text);
        Assert.NotEqual(Guid.Empty, rule.Id);
        Assert.True(rule.Account);
    }

    [Fact]
    public void ChatReevaluatesAnUnchangedMessageAfterKeywordEdits()
    {
        var configuration = new Configuration();
        var highlighter = new ChatHighlighter(configuration);
        Assert.False(Matches(highlighter, "sunflower", Ann));
        configuration.Keywords.TrySave(KeywordProfile.Global, "sunflower");
        Assert.True(Matches(highlighter, "sunflower", Ann));
        configuration.Keywords.Remove(configuration.Keywords.Rules[0].Id);
        Assert.False(Matches(highlighter, "sunflower", Ann));
    }

    [Fact]
    public void CharacterAndGlobalMatchesStillProduceOneTransformation()
    {
        var configuration = new Configuration();
        configuration.Keywords.TrySave(KeywordProfile.Global, "sunflower");
        configuration.Keywords.TrySave(AnnProfile, "sunflower");
        var original = new SeString(new TextPayload("Ann: sunflower sunflower"));
        var actual = new ChatHighlighter(configuration).Transform(original, Ann);
        var expected = SeStringHighlighter.Apply(original, RgbColor.FromRgb(configuration.HighlightColorRgb));
        Assert.Equal(expected.Encode(), actual.Encode());
    }

    private static bool Matches(ChatHighlighter highlighter, string text, CharacterIdentity? identity) =>
        highlighter.ShouldHighlight(new SeString(new TextPayload(text)), identity);
}
