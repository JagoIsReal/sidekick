using Dalamud.Configuration;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Moq;
using Newtonsoft.Json;
using Sidekick.Localization;

namespace Sidekick.Tests;

public sealed class LegacyConfigurationTests
{
    [Fact]
    public void ImportsLegacyTypesAndKeywordsOnceWithoutChangingTheSource()
    {
        using var files = new ConfigFiles();
        const string legacy = """
            {
              "$type":"Highlight.Configuration, Highlight",
              "Version":3,"Enabled":false,"HighlightColorRgb":1193046,"HighlightChatBubbles":true,
              "Keywords":{"$type":"Highlight.Keywords.KeywordSettings, Highlight","Rules":[
                {"$type":"Highlight.Keywords.KeywordRule, Highlight","Id":"11111111-1111-1111-1111-111111111111",
                 "Text":"raid","Account":true,"Enabled":false}
              ]}
            }
            """;
        File.WriteAllText(files.LegacyPath, legacy);
        var pluginInterface = files.MockInterface();
        pluginInterface.Setup(x => x.SavePluginConfig(It.IsAny<IPluginConfiguration>()))
            .Callback<IPluginConfiguration>(value => File.WriteAllText(files.CurrentPath, JsonConvert.SerializeObject(value)));

        var store = new ConfigurationStore(pluginInterface.Object, Mock.Of<IPluginLog>());
        Assert.True(store.CanSave);
        Assert.False(store.Value.Enabled);
        Assert.DoesNotContain("HighlightChatBubbles", JsonConvert.SerializeObject(store.Value));
        Assert.Equal(0x123456u, store.Value.HighlightColorRgb);
        var rule = Assert.Single(store.Value.Keywords.Rules);
        Assert.Equal("raid", rule.Text);
        Assert.True(rule.Account);
        Assert.False(rule.Enabled);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), rule.Id);
        Assert.Equal(legacy, File.ReadAllText(files.LegacyPath));
        Assert.True(File.Exists(files.CurrentPath));
        pluginInterface.Verify(x => x.SavePluginConfig(It.IsAny<IPluginConfiguration>()), Times.Once);

        var saved = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(files.CurrentPath))!;
        pluginInterface.Setup(x => x.GetPluginConfig()).Returns(saved);
        var reloaded = new ConfigurationStore(pluginInterface.Object, Mock.Of<IPluginLog>());
        Assert.Equal(rule.Id, reloaded.Value.Keywords.Rules[0].Id);
        pluginInterface.Verify(x => x.SavePluginConfig(It.IsAny<IPluginConfiguration>()), Times.Once);
    }

    [Fact]
    public void ExistingSidekickSettingsWinEvenWhenTheLegacyFileIsBroken()
    {
        using var files = new ConfigFiles();
        File.WriteAllText(files.LegacyPath, "invalid legacy data");
        var current = new Configuration { HighlightColorRgb = 0xABCDEF };
        var pluginInterface = files.MockInterface();
        pluginInterface.Setup(x => x.GetPluginConfig()).Returns(current);
        var store = new ConfigurationStore(pluginInterface.Object, Mock.Of<IPluginLog>());
        Assert.Same(current, store.Value);
        Assert.Equal(0xABCDEFu, store.Value.HighlightColorRgb);
        pluginInterface.Verify(x => x.SavePluginConfig(It.IsAny<IPluginConfiguration>()), Times.Never);
    }

    [Fact]
    public void AnUnreadableExistingSidekickFileCannotBeReplacedByLegacySettings()
    {
        using var files = new ConfigFiles();
        File.WriteAllText(files.LegacyPath, "{\"Enabled\":false}");
        File.WriteAllText(files.CurrentPath, "broken Sidekick data");
        var pluginInterface = files.MockInterface(); // Simulate a loader returning null for an unreadable file.
        var store = new ConfigurationStore(pluginInterface.Object, Mock.Of<IPluginLog>());
        Assert.False(store.CanSave);
        store.Save();
        Assert.Equal("broken Sidekick data", File.ReadAllText(files.CurrentPath));
        pluginInterface.Verify(x => x.SavePluginConfig(It.IsAny<IPluginConfiguration>()), Times.Never);
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("null")]
    [InlineData("{\"Version\":999}")]
    [InlineData("{} {}")]
    public void InvalidOrNewerLegacySettingsArePreservedAndNotSaved(string json)
    {
        using var files = new ConfigFiles();
        File.WriteAllText(files.LegacyPath, json);
        var pluginInterface = files.MockInterface();
        var store = new ConfigurationStore(pluginInterface.Object, Mock.Of<IPluginLog>());
        Assert.False(store.CanSave);
        Assert.Equal(json, File.ReadAllText(files.LegacyPath));
        Assert.False(File.Exists(files.CurrentPath));
        pluginInterface.Verify(x => x.SavePluginConfig(It.IsAny<IPluginConfiguration>()), Times.Never);
    }

    [Fact]
    public void NoLegacyFileUsesNormalDefaultsWithoutWritingAFile()
    {
        using var files = new ConfigFiles();
        var pluginInterface = files.MockInterface();
        var store = new ConfigurationStore(pluginInterface.Object, Mock.Of<IPluginLog>());
        Assert.True(store.CanSave);
        Assert.True(store.Value.Enabled);
        Assert.Empty(store.Value.Keywords.Rules);
        pluginInterface.Verify(x => x.SavePluginConfig(It.IsAny<IPluginConfiguration>()), Times.Never);
    }

    [Fact]
    public void FailedMigrationSaveKeepsTheImportedSettingsInMemoryAndOriginalFileIntact()
    {
        using var files = new ConfigFiles();
        const string json = "{\"Enabled\":false,\"HighlightColorRgb\":255}";
        File.WriteAllText(files.LegacyPath, json);
        var pluginInterface = files.MockInterface();
        pluginInterface.Setup(x => x.SavePluginConfig(It.IsAny<IPluginConfiguration>())).Throws<IOException>();
        var store = new ConfigurationStore(pluginInterface.Object, Mock.Of<IPluginLog>());
        Assert.True(store.CanSave);
        Assert.True(store.SaveFailed);
        Assert.False(store.Value.Enabled);
        Assert.Equal(255u, store.Value.HighlightColorRgb);
        Assert.Equal(json, File.ReadAllText(files.LegacyPath));
    }

    [Fact]
    public void UserFacingResourcesUseSidekickBranding()
    {
        Assert.Equal("Sidekick | Settings", Strings.SettingsTitle);
        Assert.Contains("Sidekick", Strings.CommandHelp);
        Assert.DoesNotContain("focus", Strings.CommandHelp, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Highlight color", Strings.HighlightColor);
    }

    private sealed class ConfigFiles : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), $"SidekickTests-{Guid.NewGuid():N}");
        internal string CurrentPath => Path.Combine(directory, "Sidekick.json");
        internal string LegacyPath => Path.Combine(directory, "Highlight.json");

        internal ConfigFiles() => Directory.CreateDirectory(directory);

        internal Mock<IDalamudPluginInterface> MockInterface()
        {
            var pluginInterface = new Mock<IDalamudPluginInterface>();
            pluginInterface.SetupGet(x => x.ConfigFile).Returns(new FileInfo(CurrentPath));
            return pluginInterface;
        }

        public void Dispose()
        {
            File.Delete(CurrentPath);
            File.Delete(LegacyPath);
            Directory.Delete(directory);
        }
    }
}
