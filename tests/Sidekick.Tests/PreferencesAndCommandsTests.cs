using System.Globalization;
using System.Resources;
using Dalamud.Game.Text;
using Newtonsoft.Json;
using Sidekick.Commands;
using Sidekick.Focus;
using Sidekick.Keywords;
using Sidekick.Localization;
using Sidekick.Mods;
using Sidekick.Poses;

namespace Sidekick.Tests;

public sealed class PreferencesAndCommandsTests
{
    [Fact]
    public void LanguageDefaultsToEnglishAndFrenchIsPersisted()
    {
        Assert.Equal("en", new Configuration().LanguageCode);
        var configuration = new Configuration { LanguageCode = "fr", Enabled = false };
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(configuration))!;
        restored.ValidateAndMigrate();
        Assert.Equal("fr", restored.LanguageCode);
        Assert.False(restored.Enabled);
        Assert.Equal("fr", LanguagePreference.CultureFor(restored.LanguageCode).Name);
    }

    [Theory]
    [InlineData(null, "en")]
    [InlineData("en", "en")]
    [InlineData("fr", "fr")]
    [InlineData("FR", "fr")]
    [InlineData("de", "en")]
    [InlineData("invalid", "en")]
    public void InvalidLanguagesFallBackWithoutChangingTheFrameworkThreadCulture(string? requested, string expected)
    {
        var before = CultureInfo.CurrentCulture;
        var beforeUi = CultureInfo.CurrentUICulture;
        var configuration = new Configuration { LanguageCode = requested! };
        configuration.ValidateAndMigrate();
        Assert.Equal(expected, configuration.LanguageCode);
        Assert.Equal(expected, LanguagePreference.CultureFor(requested).Name);
        Assert.Same(before, CultureInfo.CurrentCulture);
        Assert.Same(beforeUi, CultureInfo.CurrentUICulture);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void OldConfigurationKeepsItsPreferencesAndDistanceWithoutAnUnnecessaryUnitConversion(int version)
    {
        var json = JsonConvert.SerializeObject(new
        {
            Version = version,
            Enabled = false,
            HighlightColorRgb = 0x123456,
            HighlightChatBubbles = true,
            Focus = new { Enabled = true, DistanceMeters = 42.5f, OtherOpacityPercent = 37 },
            ModRules = new { Enabled = true },
        });
        var configuration = JsonConvert.DeserializeObject<Configuration>(json)!;
        configuration.ValidateAndMigrate();
        Assert.Equal(Configuration.CurrentVersion, configuration.Version);
        Assert.False(configuration.Enabled);
        Assert.Equal(0x123456u, configuration.HighlightColorRgb);
        Assert.Equal("en", configuration.LanguageCode);
        Assert.True(configuration.Focus.Enabled);
        Assert.Equal(42.5f, configuration.Focus.DistanceYalms);
        Assert.Equal(37, configuration.Focus.OtherOpacityPercent);
        Assert.True(configuration.ModRules.Enabled);
        var saved = JsonConvert.SerializeObject(configuration);
        Assert.DoesNotContain("HighlightChatBubbles", saved);
        Assert.Contains("DistanceMeters", saved); // Existing serialized keys stay stable.
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void SavedRadiusOfTenRemainsTenYalmsAfterMigrationAndReload(int version)
    {
        var json = JsonConvert.SerializeObject(new
        {
            Version = version,
            Focus = new { Enabled = true, UseDistance = true, UseParticipants = false, DistanceMeters = 10 },
        });
        var migrated = JsonConvert.DeserializeObject<Configuration>(json)!;
        migrated.ValidateAndMigrate();
        Assert.Equal(10, migrated.Focus.DistanceYalms);

        var reloaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(migrated))!;
        reloaded.ValidateAndMigrate();
        Assert.Equal(10, reloaded.Focus.DistanceYalms);
        var self = new FocusCharacter("Ann Example", 21);
        var author = new FocusCharacter("Iris Another", 22);
        Assert.False(FocusRules.ShouldDim(reloaded.Focus, XivChatType.Say, author, self, 10));
        Assert.True(FocusRules.ShouldDim(reloaded.Focus, XivChatType.Say, author, self, 10.1f));
    }

    [Fact]
    public void ResettingChatOrFocusKeepsCustomEntriesOtherModulesAndLanguage()
    {
        var configuration = new Configuration { Enabled = false, HighlightColorRgb = 0x123456, LanguageCode = "fr" };
        configuration.Keywords.TrySave(KeywordProfile.Global, "sunflower");
        configuration.Focus.Enabled = true;
        configuration.Focus.DistanceYalms = 42;
        configuration.Focus.OtherOpacityPercent = 37;
        configuration.Focus.UseDistance = false;
        configuration.Focus.Combination = FocusCombination.All;
        configuration.Focus.Channels = [XivChatType.Party];
        configuration.Focus.Add("Iris Another", 22);
        var participant = Assert.Single(configuration.Focus.Participants);
        configuration.Focus.SetParticipantEnabled(participant.Id, false);
        configuration.ModRules.Enabled = true;
        var mod = new ModLocationRule();
        configuration.ModRules.Rules.Add(mod);

        configuration.Reset();
        Assert.True(configuration.Enabled);
        Assert.Equal(Sidekick.Core.RgbColor.DefaultRgb, configuration.HighlightColorRgb);
        Assert.True(configuration.Focus.Enabled);
        Assert.Equal(37, configuration.Focus.OtherOpacityPercent);
        Assert.Single(configuration.Keywords.Rules);

        configuration.Enabled = false;
        configuration.Focus.Reset();
        Assert.False(configuration.Focus.Enabled);
        Assert.Equal(25, configuration.Focus.OtherOpacityPercent);
        Assert.Equal(15, configuration.Focus.DistanceYalms);
        Assert.True(configuration.Focus.UseDistance);
        Assert.True(configuration.Focus.UseParticipants);
        Assert.Equal(FocusCombination.Any, configuration.Focus.Combination);
        Assert.Equal(new HashSet<XivChatType> { XivChatType.Say, XivChatType.CustomEmote, XivChatType.StandardEmote }, configuration.Focus.Channels);
        Assert.Same(participant, Assert.Single(configuration.Focus.Participants));
        Assert.False(participant.Enabled);
        Assert.False(configuration.Enabled);
        Assert.Equal("fr", configuration.LanguageCode);
        Assert.Single(configuration.Keywords.Rules);
        Assert.True(configuration.ModRules.Enabled);
        Assert.Same(mod, Assert.Single(configuration.ModRules.Rules));
    }

    [Fact]
    public void CommandCatalogCoversTheActualParserAndBothWindowAliases()
    {
        var categories = CommandCatalog.Categories();
        Assert.Equal(["General", "Emotes", "Focus"], categories.Select(category => category.Id));
        var entries = categories.SelectMany(category => category.Commands).ToArray();
        Assert.Contains(entries, entry => entry.Command == "/sidekick");
        Assert.Contains(entries, entry => entry.Command == "/sk");
        Assert.Contains(entries, entry => entry.Command == "/sk cpose 1");
        Assert.Contains(entries, entry => entry.Command == "/sk cpose 5");
        Assert.Contains(entries, entry => entry.Command == "/sk focus");
        Assert.Contains(entries, entry => entry.Command == "/sk focus on");
        Assert.Contains(entries, entry => entry.Command == "/sk focus off");
        Assert.Equal(entries.Length, entries.Select(entry => entry.Command).Distinct().Count());
        foreach (var entry in entries.Where(entry => !entry.Command.Contains('<')))
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
            var prefix = entry.Command.StartsWith(CommandCatalog.MainCommand, StringComparison.Ordinal)
                ? CommandCatalog.MainCommand : CommandCatalog.ShortCommand;
            var arguments = entry.Command[prefix.Length..].Trim();
            Assert.True(CposeCommand.TryParse(arguments, out _) || FocusCommand.Parse(arguments) != FocusCommandAction.Usage, entry.Command);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void UiResourcesUseYalmsAndContainNoDevelopmentValidationNotices(string language)
    {
        var resources = new ResourceManager("Sidekick.Localization.Strings", typeof(Configuration).Assembly);
        var culture = LanguagePreference.CultureFor(language);
        Assert.Contains("yalms", resources.GetString("FocusDistanceHelp", culture)!);
        Assert.EndsWith(" y)", resources.GetString("FocusNearbyEntry", culture));
        var set = resources.GetResourceSet(culture, true, language == "en")!;
        foreach (System.Collections.DictionaryEntry entry in set)
        {
            var value = (string)entry.Value!;
            Assert.DoesNotContain("in-game validation", value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("à valider en jeu", value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("à vérifier en jeu", value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain('\u2014', value);
        }
    }
}
