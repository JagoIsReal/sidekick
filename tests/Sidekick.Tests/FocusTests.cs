using Dalamud.Game.Text;
using Dalamud.Game.Config;
using Newtonsoft.Json;
using Sidekick.Chat;
using Sidekick.Focus;
using Sidekick.Windows;

namespace Sidekick.Tests;

public sealed class FocusTests
{
    private static readonly FocusCharacter Self = new("Ann Example", 21);
    private static readonly FocusCharacter Other = new("Iris Another", 22);

    [Theory]
    [InlineData(FocusCombination.Any, true, true, false)]
    [InlineData(FocusCombination.Any, true, false, false)]
    [InlineData(FocusCombination.Any, false, true, false)]
    [InlineData(FocusCombination.Any, false, false, true)]
    [InlineData(FocusCombination.All, true, true, false)]
    [InlineData(FocusCombination.All, true, false, true)]
    [InlineData(FocusCombination.All, false, true, true)]
    [InlineData(FocusCombination.All, false, false, true)]
    public void CombinationOfDistanceAndParticipants(FocusCombination combination, bool nearby, bool selected, bool dim)
    {
        var settings = new FocusSettings { Enabled = true, Combination = combination };
        if (selected)
            settings.Add(Other.Name, Other.HomeWorldId);
        Assert.Equal(dim, FocusRules.ShouldDim(settings, XivChatType.Say, Other, Self, nearby ? 10 : 20));
    }

    [Theory]
    [InlineData(true, false, 15, false)]
    [InlineData(true, false, 15.01f, true)]
    [InlineData(false, true, 100, false)]
    [InlineData(false, false, 100, true)]
    public void SingleCriteriaAndInclusiveDistanceBoundary(bool useDistance, bool useParticipants, float distance, bool dim)
    {
        var settings = new FocusSettings { Enabled = true, UseDistance = useDistance, UseParticipants = useParticipants };
        settings.Add(Other.Name, Other.HomeWorldId);
        Assert.Equal(dim, FocusRules.ShouldDim(settings, XivChatType.Say, Other, Self, distance));
    }

    [Fact]
    public void MissingDistanceNeverPretendsTheAuthorIsNearbyButListedPeopleSurviveOr()
    {
        var settings = new FocusSettings { Enabled = true };
        Assert.True(FocusRules.ShouldDim(settings, XivChatType.Say, Other, Self, null));
        settings.Add(Other.Name, Other.HomeWorldId);
        Assert.False(FocusRules.ShouldDim(settings, XivChatType.Say, Other, Self, null));
        settings.Combination = FocusCombination.All;
        Assert.True(FocusRules.ShouldDim(settings, XivChatType.Say, Other, Self, null));
        Assert.True(FocusRules.ShouldDim(settings, XivChatType.Say, Other, Self, float.NaN));
        Assert.True(FocusRules.ShouldDim(settings, XivChatType.Say, Other, Self, -1));
    }

    [Fact]
    public void DisabledUnknownOwnSystemAndUnselectedChannelsArePreserved()
    {
        var settings = new FocusSettings();
        Assert.False(FocusRules.ShouldDim(settings, XivChatType.Say, Other, Self, 100));
        settings.Enabled = true;
        Assert.False(FocusRules.ShouldDim(settings, XivChatType.Say, Self, Self, 100));
        Assert.False(FocusRules.ShouldDim(settings, XivChatType.Say, Other, Self, 100, true));
        Assert.False(FocusRules.ShouldDim(settings, XivChatType.Say, null, Self, 100));
        Assert.False(FocusRules.ShouldDim(settings, XivChatType.Say, Other, null, 100));
        Assert.False(FocusRules.ShouldDim(settings, XivChatType.TellIncoming, Other, Self, 100));
        settings.Channels.Add(XivChatType.SystemMessage);
        Assert.False(FocusRules.ShouldDim(settings, XivChatType.SystemMessage, Other, Self, 100));
        settings.OtherOpacityPercent = 100;
        Assert.False(FocusRules.ShouldDim(settings, XivChatType.Say, Other, Self, 100));
    }

    [Fact]
    public void ParticipantsUseNormalizedFullNameAndHomeWorldAndCanBeDisabledAndRemoved()
    {
        var settings = new FocusSettings { Enabled = true, UseDistance = false };
        Assert.Equal(FocusParticipantResult.Success, settings.Add("  Iri\u0301s Another  ", 22));
        var id = Assert.Single(settings.Participants).Id;
        var same = FocusCharacter.Create("IRÍS ANOTHER", 22)!.Value;
        Assert.True(settings.Contains(same));
        Assert.False(settings.Contains(new FocusCharacter("Irís Another", 23)));
        Assert.False(settings.Contains(new FocusCharacter("Iris Another", 22)));
        Assert.Equal(FocusParticipantResult.Duplicate, settings.Add("Irís Another", 22));
        settings.SetParticipantEnabled(id, false);
        Assert.False(settings.Contains(same));
        settings.SetParticipantEnabled(id, true);
        Assert.True(settings.Contains(same));
        Assert.Equal(id, Assert.Single(settings.Participants).Id);
        settings.Remove(id);
        Assert.False(settings.Contains(same));
        Assert.Empty(settings.Participants);
    }

    [Theory]
    [InlineData("", 21)]
    [InlineData("Ann", 21)]
    [InlineData("Ann Example", 0)]
    [InlineData("Ann Example", 65536)]
    [InlineData("Ann\nExample", 21)]
    [InlineData("Ann Example Another", 21)]
    public void InvalidParticipantsAreRejected(string name, uint world) =>
        Assert.Equal(FocusParticipantResult.Invalid, new FocusSettings().Add(name, world));

    [Fact]
    public void ParticipantListIsBounded()
    {
        var settings = new FocusSettings();
        for (var i = 1; i <= FocusSettings.MaxParticipants; i++)
            Assert.Equal(FocusParticipantResult.Success, settings.Add("Iris Another", (uint)i));
        Assert.Equal(FocusParticipantResult.LimitReached, settings.Add("Other Person", 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void OlderConfigurationsKeepPreferencesAndDoNotEnableFocus(int version)
    {
        var json = JsonConvert.SerializeObject(new { Version = version, Enabled = false, HighlightColorRgb = 255, ModRules = new { Enabled = true } });
        var configuration = JsonConvert.DeserializeObject<Configuration>(json)!;
        configuration.ValidateAndMigrate();
        Assert.False(configuration.Enabled);
        Assert.True(configuration.ModRules.Enabled);
        Assert.Equal(255u, configuration.HighlightColorRgb);
        Assert.False(configuration.Focus.Enabled);
        Assert.Equal(25, configuration.Focus.OtherOpacityPercent);
        Assert.Equal(Configuration.CurrentVersion, configuration.Version);
    }

    [Fact]
    public void FocusRoundTripsAndChatResetPreservesIt()
    {
        var configuration = new Configuration();
        configuration.Focus.Enabled = true;
        configuration.Focus.UseDistance = false;
        configuration.Focus.OtherOpacityPercent = 37;
        configuration.Focus.Channels = [XivChatType.Party];
        configuration.Focus.Add(Other.Name, Other.HomeWorldId);
        configuration.Reset();
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(configuration))!;
        restored.ValidateAndMigrate();
        Assert.True(restored.Focus.Enabled);
        Assert.Equal(37, restored.Focus.OtherOpacityPercent);
        Assert.Equal([XivChatType.Party], restored.Focus.Channels);
        Assert.False(restored.Focus.UseDistance);
        Assert.True(restored.Focus.Contains(Other));
    }

    [Fact]
    public void InvalidSettingsAndDuplicateParticipantIdsAreRepaired()
    {
        var id = Guid.NewGuid();
        var settings = new FocusSettings
        {
            OtherOpacityPercent = -1,
            DistanceYalms = float.NaN,
            Combination = (FocusCombination)99,
            Channels = [XivChatType.Say, XivChatType.SystemMessage, XivChatType.GmTell],
            Participants = [
                new() { Id = id, Name = "Ann Example", HomeWorldId = 21 },
                new() { Id = id, Name = "Iris Another", HomeWorldId = 22 },
                new() { Name = "ANN EXAMPLE", HomeWorldId = 21 },
                new() { Name = "Invalid", HomeWorldId = 0 },
            ],
        };
        settings.Validate();
        Assert.Equal(25, settings.OtherOpacityPercent);
        Assert.Equal(15, settings.DistanceYalms);
        Assert.Equal(FocusCombination.Any, settings.Combination);
        Assert.Equal([XivChatType.Say], settings.Channels);
        Assert.Equal(2, settings.Participants.Count);
        Assert.NotEqual(settings.Participants[0].Id, settings.Participants[1].Id);
        Assert.True(settings.Contains(Other));
    }

    [Fact]
    public void ExplicitlyEmptyChannelsStayEmptyAfterReload()
    {
        var configuration = new Configuration();
        configuration.Focus.Channels.Clear();
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(configuration))!;
        restored.ValidateAndMigrate();
        Assert.Empty(restored.Focus.Channels);
    }

    [Fact]
    public void EverySelectableChannelHasALabelAndANativeColorSource()
    {
        Assert.Equal(FocusChannels.All.Length, FocusChannels.All.Distinct().Count());
        foreach (var channel in FocusChannels.All)
        {
            Assert.NotNull(GameChatColors.ChannelOption(channel));
            Assert.True(Enum.TryParse<UiConfigOption>(GameChatColors.ChannelOption(channel), out _));
            Assert.False(string.IsNullOrWhiteSpace(FocusSettingsPanel.ChannelName(channel)));
        }
    }

    [Theory]
    [InlineData("", nameof(FocusCommandAction.OpenSettings))]
    [InlineData("   ", nameof(FocusCommandAction.OpenSettings))]
    [InlineData("focus", nameof(FocusCommandAction.Toggle))]
    [InlineData(" FOCUS  On ", nameof(FocusCommandAction.Enable))]
    [InlineData("focus off", nameof(FocusCommandAction.Disable))]
    [InlineData("focus once", nameof(FocusCommandAction.Usage))]
    [InlineData("focus on off", nameof(FocusCommandAction.Usage))]
    [InlineData("unknown", nameof(FocusCommandAction.Usage))]
    public void CommandsAreExplicitAndRejectExtraArguments(string arguments, string action) =>
        Assert.Equal(action, FocusCommand.Parse(arguments).ToString());
}
