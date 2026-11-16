using Newtonsoft.Json;
using Sidekick.Appearance;
using Sidekick.Locations;
using Sidekick.Mods;
using Sidekick.Rules;

namespace Sidekick.Tests;

public sealed class RuleSelectionTests
{
    [Theory]
    [InlineData(7u, 12u, true)]
    [InlineData(8u, 17u, true)]
    [InlineData(7u, 22u, false)]
    [InlineData(1u, 12u, false)]
    [InlineData(null, 12u, false)]
    [InlineData(7u, null, false)]
    public void AlternativesWithinAFilterAreOrAndDifferentFiltersRemainAnd(uint? weather, uint? status, bool matches)
    {
        var condition = new LocationCondition { WeatherIds = [7, 8], OnlineStatusIds = [12, 17], Daytime = true };
        var context = new LocationSnapshot(100, 21, 0, WeatherId: weather, OnlineStatusId: status, Daytime: true);
        Assert.True(condition.IsValid);
        Assert.Equal(matches, condition.Matches(context));
        Assert.False(condition.Matches(context with { Daytime = false }));
    }

    [Fact]
    public void EmptySelectionIgnoresThatFilterButStatusZeroRemainsARealSelection()
    {
        var condition = new LocationCondition { OnlineStatusIds = [0, 47] };
        var context = new LocationSnapshot(100, 21, 0, OnlineStatusId: 0);
        Assert.True(condition.IsValid);
        Assert.True(condition.Matches(context));
        Assert.True(condition.Matches(context with { OnlineStatusId = 47 }));
        Assert.False(condition.Matches(context with { OnlineStatusId = null }));
        condition.OnlineStatusIds.Clear();
        condition.WeatherIds.Add(7);
        Assert.True(condition.Matches(context with { WeatherId = 7, OnlineStatusId = null }));
    }

    [Fact]
    public void IdenticallyNamedOptionsSelectAllNativeVariantsWithoutDuplicatingTheLabel()
    {
        var groups = RuleOptionGroup.Create([(7, "Pluie"), (8, "PLUIE"), (75, " Pluie "), (7, "Pluie"),
            (9, "Éclaircie"), (10, "E\u0301claircie"), (11, "Eclaircie")]);
        Assert.Equal(3, groups.Length);
        var rain = Assert.Single(groups, group => group.Ids.Contains(7u));
        Assert.Equal(new uint[] { 7, 8, 75 }, rain.Ids);
        var selected = new List<uint> { 9 };
        rain.Select(selected, true);
        rain.Select(selected, true);
        Assert.Equal(new uint[] { 7, 8, 9, 75 }, selected.Order().ToArray());
        rain.Select(selected, false);
        Assert.Equal(new uint[] { 9 }, selected);
    }

    [Fact]
    public void EditingADraftCannotMutateEitherModulesSavedSelections()
    {
        var conditions = new LocationCondition { WeatherIds = [7, 8], OnlineStatusIds = [12, 22] };
        var mod = new ModLocationRule { Location = conditions };
        var glamourer = new GlamourerRule { Conditions = conditions };
        var modDraft = mod.Copy();
        var glamourerDraft = glamourer.Copy();
        modDraft.Location.WeatherIds.Clear();
        glamourerDraft.Conditions.OnlineStatusIds.Remove(12);
        Assert.Equal(new uint[] { 7, 8 }, conditions.WeatherIds);
        Assert.Equal(new uint[] { 12, 22 }, conditions.OnlineStatusIds);
        Assert.NotSame(conditions.WeatherIds, modDraft.Location.WeatherIds);
        Assert.NotSame(conditions.OnlineStatusIds, glamourerDraft.Conditions.OnlineStatusIds);
    }

    [Fact]
    public void SchemaEightSingleWeatherSelectionsMigrateExactlyForBothModules()
    {
        var configuration = new Configuration { Version = 8 };
        configuration.ModRules.Enabled = true;
        configuration.ModRules.Rules.Add(new() { Name = "Mods", Location = new() { WeatherId = 7 }, Mods = ["test-mod"] });
        configuration.Glamourer.Enabled = true;
        configuration.Glamourer.Rules.Add(new() { Name = "Outfit", Conditions = new() { WeatherId = 8 }, DesignId = Guid.NewGuid() });
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(configuration))!;
        restored.ValidateAndMigrate();
        Assert.Equal(9, restored.Version);
        var mods = Assert.Single(restored.ModRules.Rules);
        var outfit = Assert.Single(restored.Glamourer.Rules);
        Assert.True(restored.ModRules.Enabled);
        Assert.True(restored.Glamourer.Enabled);
        Assert.Null(mods.Location.WeatherId);
        Assert.Null(outfit.Conditions.WeatherId);
        Assert.Equal(new uint[] { 7 }, mods.Location.WeatherIds);
        Assert.Equal(new uint[] { 8 }, outfit.Conditions.WeatherIds);
        Assert.False(mods.Location.Matches(new(100, 21, 0, WeatherId: 75)));
        Assert.DoesNotContain("\"WeatherId\"", JsonConvert.SerializeObject(restored));
    }

    [Fact]
    public void MultipleSelectionsRoundTripWithTheirStoredIds()
    {
        var condition = new LocationCondition { WeatherIds = [7, 75, 8], OnlineStatusIds = [0, 47, 22] };
        var restored = JsonConvert.DeserializeObject<LocationCondition>(JsonConvert.SerializeObject(condition))!;
        restored.NormalizeSelections();
        Assert.Equal(condition.WeatherIds, restored.WeatherIds);
        Assert.Equal(condition.OnlineStatusIds, restored.OnlineStatusIds);
        Assert.True(restored.Matches(new(100, 21, 0, WeatherId: 75, OnlineStatusId: 22)));
    }

    [Fact]
    public void InvalidSelectionsCannotTurnIntoAnUnfilteredRule()
    {
        Assert.False(new LocationCondition { TerritoryId = 100, WeatherIds = [0] }.IsValid);
        Assert.False(new LocationCondition { WeatherIds = [256] }.IsValid);
        Assert.False(new LocationCondition { OnlineStatusIds = [256] }.IsValid);
        Assert.False(new LocationCondition { TerritoryId = 100, WeatherIds = null! }.IsValid);
        Assert.False(new LocationCondition { TerritoryId = 100, OnlineStatusIds = null! }.IsValid);
    }
}
