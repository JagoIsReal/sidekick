using Newtonsoft.Json;
using Sidekick;
using Sidekick.Appearance;
using Sidekick.Core;
using Sidekick.Locations;
using Sidekick.Rules;

namespace Sidekick.Tests;

public sealed class ConditionalRuleTests
{
    private static readonly CharacterIdentity Ann = CharacterIdentity.FromName("Ann Example", 21)!;

    [Theory]
    [InlineData(0, false)]
    [InlineData(21599, false)]
    [InlineData(21600, true)]
    [InlineData(64799, true)]
    [InlineData(64800, false)]
    [InlineData(86399, false)]
    [InlineData(108000, true)]
    public void DaytimeUsesEorzeaTimeAndExactSixToEighteenBoundaries(long seconds, bool expected) =>
        Assert.Equal(expected, GameLocationReader.IsDaytime(seconds));

    [Fact]
    public void ConditionsCanUseOnlyEnvironmentAndStillCombineWithAnAddress()
    {
        var condition = new LocationCondition { WeatherId = 7, Daytime = false, Flying = true, Mounted = true };
        var context = new LocationSnapshot(100, 99, 0, Character: Ann, WeatherId: 7, Daytime: false, Flying: true, Mounted: true);
        Assert.True(condition.IsValid);
        Assert.True(condition.Matches(context));
        Assert.False(condition.Matches(context with { WeatherId = null }));
        Assert.False(condition.Matches(context with { Daytime = true }));
        Assert.False(condition.Matches(context with { Flying = null }));
        condition.WorldId = 21;
        Assert.False(condition.Matches(context)); // Visited world is independent of profile home world.
        Assert.True(condition.Matches(context with { WorldId = 21 }));
        Assert.False(new LocationCondition { Diving = true, Swimming = false }.IsValid);
        Assert.False(new LocationCondition { Flying = true, Mounted = false }.IsValid);
        Assert.False(new LocationCondition { WeatherId = 256 }.IsValid);
        Assert.True(new LocationCondition { Swimming = false }.IsValid);
        Assert.False(new LocationCondition { Swimming = false }.Matches(context)); // Unknown is not false.
    }

    [Fact]
    public void ProfilesUseFullNormalizedNamesAndHomeWorldWithoutPromotingMissingProfiles()
    {
        var profiles = new RuleProfileSettings();
        var profile = new RuleCharacterProfile { CharacterName = "  Iri\u0301s Another  ", HomeWorldId = 21 };
        Assert.True(profiles.TrySave(profile));
        Assert.True(profiles.Matches(profile.Id, CharacterIdentity.FromName("IRÍS ANOTHER", 21)));
        Assert.False(profiles.Matches(profile.Id, CharacterIdentity.FromName("Irís Another", 22)));
        Assert.False(profiles.Matches(profile.Id, CharacterIdentity.FromName("Iris Another", 21)));
        Assert.False(profiles.Matches(Guid.NewGuid(), Ann));
        Assert.False(profiles.Matches(profile.Id, null));
        Assert.True(profiles.Matches(null, Ann));
        Assert.False(profiles.TrySave(new RuleCharacterProfile { CharacterName = "Irís Another", HomeWorldId = 21 }));
        Assert.True(profiles.TrySave(new RuleCharacterProfile { CharacterName = "Irís Another", HomeWorldId = 22 }));
        var edit = Assert.Single(profiles.Characters, p => p.Id == profile.Id).Copy();
        edit.Enabled = false;
        Assert.True(profiles.TrySave(edit));
        Assert.False(profiles.Matches(edit.Id, CharacterIdentity.FromName("Irís Another", 21)));
    }

    [Theory]
    [InlineData(null, 21)]
    [InlineData("", 21)]
    [InlineData("Ann", 21)]
    [InlineData("Ann\nExample", 21)]
    [InlineData("Ann Example", 0)]
    [InlineData("Ann Example", 65536)]
    public void InvalidProfilesAreRejected(string? name, uint world) =>
        Assert.False(new RuleProfileSettings().TrySave(new RuleCharacterProfile { CharacterName = name!, HomeWorldId = world }));

    [Fact]
    public void DuplicateProfileIdsCannotRetargetExistingRulesToAnotherCharacter()
    {
        var id = Guid.NewGuid();
        var profiles = new RuleProfileSettings
        {
            Characters = [
            new() { Id = id, CharacterName = "Ann Example", HomeWorldId = 21 },
            new() { Id = id, CharacterName = "Iris Another", HomeWorldId = 22 },
        ]
        };
        profiles.Validate();
        Assert.Single(profiles.Characters);
        Assert.True(profiles.Matches(id, Ann));
        Assert.False(profiles.Matches(id, CharacterIdentity.FromName("Iris Another", 22)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void OldConfigurationsKeepAccountRulesAndDoNotEnableGlamourer(int version)
    {
        var id = Guid.NewGuid();
        var json = JsonConvert.SerializeObject(new
        {
            Version = version,
            Enabled = false,
            LanguageCode = "fr",
            Focus = new { Enabled = true, DistanceMeters = 10 },
            ModRules = new { Enabled = true, Rules = new[] { new { Id = id, Name = "Old rule", Location = new { TerritoryId = 100 }, Mods = new[] { "test-mod" } } } }
        });
        var configuration = JsonConvert.DeserializeObject<Configuration>(json)!;
        configuration.ValidateAndMigrate();
        Assert.Equal(Configuration.CurrentVersion, configuration.Version);
        Assert.False(configuration.Enabled);
        Assert.Equal("fr", configuration.LanguageCode);
        Assert.True(configuration.Focus.Enabled);
        Assert.Equal(10, configuration.Focus.DistanceYalms);
        Assert.True(configuration.ModRules.Enabled);
        Assert.Equal(id, Assert.Single(configuration.ModRules.Rules).Id);
        Assert.Null(configuration.ModRules.Rules[0].ProfileId);
        Assert.False(configuration.Glamourer.Enabled);
        Assert.Empty(configuration.RuleProfiles.Characters);
    }

    [Fact]
    public void ProfilesAndGlamourerRulesRoundTripAndSurviveChatAndFocusResets()
    {
        var configuration = new Configuration();
        var profile = new RuleCharacterProfile { CharacterName = "Ann Example", HomeWorldId = 21 };
        configuration.RuleProfiles.TrySave(profile);
        configuration.Glamourer.Enabled = true;
        configuration.Glamourer.Rules.Add(new GlamourerRule
        {
            Name = "Swimming",
            ProfileId = profile.Id,
            Conditions = new LocationCondition { Swimming = true },
            DesignId = Guid.NewGuid()
        });
        configuration.Reset(); configuration.Focus.Reset();
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(configuration))!;
        restored.ValidateAndMigrate();
        Assert.True(restored.Glamourer.Enabled);
        Assert.Equal(profile.Id, Assert.Single(restored.Glamourer.Rules).ProfileId);
        Assert.True(restored.RuleProfiles.Matches(profile.Id, Ann));
    }

    [Fact]
    public void StableContextWaitsForCategoricalChangesAndResetsAfterLoading()
    {
        var stable = new StableRuleContext();
        var context = new LocationSnapshot(100, 21, 0, Character: Ann, Daytime: true);
        Assert.False(stable.Ready(context, false, 1000));
        Assert.False(stable.Ready(context with { }, false, 1500));
        Assert.True(stable.Ready(context with { }, false, 2000));
        Assert.False(stable.Ready(context with { Flying = true }, false, 2500));
        Assert.True(stable.Ready(context with { Flying = true }, false, 3500));
        Assert.False(stable.Ready(null, true, 4000));
        Assert.False(stable.Ready(context, false, 4500));
        Assert.True(stable.Ready(context, false, 5500));
        Assert.True(stable.Ready(null, false, 6000));
    }
}
