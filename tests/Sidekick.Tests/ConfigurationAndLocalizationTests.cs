using System.Globalization;
using System.Numerics;
using Sidekick.Core;
using Sidekick.Localization;
using Newtonsoft.Json;

namespace Sidekick.Tests;

public sealed class ConfigurationAndLocalizationTests
{
    [Fact]
    public void InvalidColorFallsBackAndOldVersionPreservesPreferences()
    {
        var configuration = new Configuration { Version = 0, Enabled = false, HighlightColorRgb = 0x1000000 };
        configuration.ValidateAndMigrate();
        Assert.Equal(Configuration.CurrentVersion, configuration.Version);
        Assert.False(configuration.Enabled);
        Assert.Equal(RgbColor.DefaultRgb, configuration.HighlightColorRgb);
    }

    [Fact]
    public void FutureConfigurationVersionIsRejected() => Assert.Throws<NotSupportedException>(
        () => new Configuration { Version = Configuration.CurrentVersion + 1 }.ValidateAndMigrate());

    [Fact]
    public void PersistedConfigurationRoundTripsWithoutUiOrGameState()
    {
        var original = new Configuration { Enabled = false, HighlightColorRgb = 0x12AB34, LanguageCode = "fr" };
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(original))!;
        restored.ValidateAndMigrate();
        Assert.False(restored.Enabled);
        Assert.Equal("fr", restored.LanguageCode);
        Assert.Equal(0x12AB34u, restored.HighlightColorRgb);
        Assert.Equal(Configuration.CurrentVersion, restored.Version);
    }

    [Fact]
    public void ResetUsesSameDefaultsAsNewConfiguration()
    {
        var configuration = new Configuration { Enabled = false, HighlightColorRgb = 0, LanguageCode = "fr" };
        configuration.Reset();
        var defaults = new Configuration();
        Assert.Equal(defaults.Enabled, configuration.Enabled);
        Assert.Equal(defaults.HighlightColorRgb, configuration.HighlightColorRgb);
        Assert.Equal("fr", configuration.LanguageCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void OldConfigurationsKeepPreferencesAndDefaultToEnglish(int version)
    {
        var json = $"{{\"Version\":{version},\"Enabled\":false,\"HighlightColorRgb\":255}}";
        var configuration = JsonConvert.DeserializeObject<Configuration>(json)!;
        configuration.ValidateAndMigrate();
        Assert.False(configuration.Enabled);
        Assert.Equal(255u, configuration.HighlightColorRgb);
        Assert.Equal("en", configuration.LanguageCode);
        Assert.Equal(Configuration.CurrentVersion, configuration.Version);
    }

    [Fact]
    public void VersionTwoMigrationIgnoresRemovedBubbleSettingAndPreservesColor()
    {
        var old = JsonConvert.DeserializeObject<Configuration>("{\"Version\":2,\"Enabled\":false,\"HighlightChatBubbles\":true,\"HighlightColorRgb\":1193046}")!;
        old.ValidateAndMigrate();
        Assert.False(old.Enabled);
        Assert.DoesNotContain("HighlightChatBubbles", JsonConvert.SerializeObject(old));
        Assert.Equal(0x123456u, old.HighlightColorRgb);
        Assert.Empty(old.Keywords.Rules);
        Assert.Equal(Configuration.CurrentVersion, old.Version);
    }

    [Theory]
    [InlineData(0xFF0000u, 1f, 0f, 0f)]
    [InlineData(0x00FF00u, 0f, 1f, 0f)]
    [InlineData(0x0000FFu, 0f, 0f, 1f)]
    [InlineData(0xFFFFFFu, 1f, 1f, 1f)]
    public void RgbStorageAndUiChannelsRoundTrip(uint packed, float red, float green, float blue)
    {
        Assert.Equal(new Vector3(red, green, blue), RgbColor.FromRgb(packed).ToVector3());
        Assert.Equal(packed, RgbColor.FromVector3(new Vector3(red, green, blue)).PackedRgb);
    }

    [Fact]
    public void UiConversionClampsInvalidComponents() =>
        Assert.Equal(0x00FF00u, RgbColor.FromVector3(new Vector3(-1, 2, float.NaN)).PackedRgb);

    [Fact]
    public void UntranslatedCultureFallsBackToEnglish()
    {
        var resources = new System.Resources.ResourceManager("Sidekick.Localization.Strings", typeof(Configuration).Assembly);
        Assert.Equal("Enable highlighting", resources.GetString("EnableHighlighting", CultureInfo.GetCultureInfo("de-DE")));
        Assert.Contains("Ann", string.Format(resources.GetString("PreviewMessage", CultureInfo.InvariantCulture)!, "Ann"));
    }

    [Fact]
    public void FrenchIsCompleteAndPreservesTechnicalTermsAndFormatParameters()
    {
        Assert.Equal("en", LanguagePreference.DefaultCode);
        var resources = new System.Resources.ResourceManager("Sidekick.Localization.Strings", typeof(Configuration).Assembly);
        Assert.Equal("Sidekick | Réglages", resources.GetString("SettingsTitle", CultureInfo.GetCultureInfo("fr-FR")));
        var neutral = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var french = resources.GetResourceSet(CultureInfo.GetCultureInfo("fr"), true, false)!;
        Assert.NotNull(french);
        foreach (System.Collections.DictionaryEntry entry in neutral)
        {
            var key = (string)entry.Key;
            var translated = french.GetString(key);
            Assert.False(string.IsNullOrWhiteSpace(translated), key);
            var sourceFormat = System.Text.CompositeFormat.Parse((string)entry.Value!);
            var targetFormat = System.Text.CompositeFormat.Parse(translated!);
            Assert.Equal(sourceFormat.MinimumArgumentCount, targetFormat.MinimumArgumentCount);
        }
        Assert.Equal("Compte", french.GetString("Account"));
        Assert.Equal("Toggle", Strings.ToggleMod);
        Assert.Equal("Redraw all", Strings.RefreshAll);
        Assert.Equal("Self", Strings.RefreshSelf);
        Assert.DoesNotContain("expérimental", Strings.EnableModRules, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("experimental", resources.GetString("EnableModRules", CultureInfo.InvariantCulture)!, StringComparison.OrdinalIgnoreCase);
    }
}

