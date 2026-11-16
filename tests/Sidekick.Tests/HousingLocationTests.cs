using Newtonsoft.Json;
using Sidekick.Locations;
using Sidekick.Mods;

namespace Sidekick.Tests;

public sealed class HousingLocationTests
{
    private static readonly LocationSnapshot Outside = new(339, 21, 0, 339, 7, 18, null, 1, false);
    private static readonly LocationSnapshot Inside = Outside with { TerritoryId = 1001, Indoors = true };

    private static LocationCondition Address => new()
    {
        TerritoryId = 339,
        HousingDistrictId = 339,
        WorldId = 21,
        Ward = 7,
        Plot = 18,
        Indoors = true,
    };

    [Fact]
    public void ResidentialZoneIncludesItsInteriorAndIndoorsStillExcludesTheStreet()
    {
        var filter = Address;
        Assert.True(filter.Matches(Inside));
        Assert.False(filter.Matches(Outside));
        Assert.True((filter with { Indoors = false }).Matches(Outside));
        Assert.False((filter with { Indoors = false }).Matches(Inside));
        Assert.True((filter with { Indoors = null }).Matches(Outside));
        Assert.True((filter with { Indoors = null }).Matches(Inside));
    }

    [Fact]
    public void DistrictSelectionWorksWithoutASeparateDistrictFilterAndNeverMatchesAnotherAddress()
    {
        var districtOnly = new LocationCondition { TerritoryId = 339 };
        Assert.True(districtOnly.Matches(Outside));
        Assert.True(districtOnly.Matches(Inside));
        Assert.False(districtOnly.Matches(Inside with { HousingDistrictId = 340 }));
        Assert.False(districtOnly.Matches(Inside with { HousingDistrictId = null }));
        Assert.False(Address.Matches(Inside with { WorldId = 22 }));
        Assert.False(Address.Matches(Inside with { Ward = 8 }));
        Assert.False(Address.Matches(Inside with { Plot = 19 }));
        Assert.False(Address.Matches(Inside with { HousingDistrictId = 340 }));
    }

    [Fact]
    public void ExactInteriorAndOrdinaryTerritorySelectionsRemainExact()
    {
        var interior = new LocationCondition { TerritoryId = 1001 };
        Assert.True(interior.Matches(Inside));
        Assert.False(interior.Matches(Outside));
        Assert.False(interior.Matches(Inside with { TerritoryId = 1002 }));
        var ordinary = new LocationCondition { TerritoryId = 900 };
        Assert.True(ordinary.Matches(new LocationSnapshot(900, 21, 0)));
        Assert.False(ordinary.Matches(new LocationSnapshot(901, 21, 0)));
    }

    [Fact]
    public void CapturingAnInteriorKeepsTheResidentialDistrictAcrossLayoutsAndRooms()
    {
        var filter = LocationCondition.Capture(Inside);
        Assert.Equal(339u, filter.TerritoryId);
        Assert.Equal(339u, filter.HousingDistrictId);
        Assert.True(filter.Matches(Inside with { TerritoryId = 1002 }));
        Assert.True(filter.Matches(Inside with { TerritoryId = 1003, Room = 9 }));
        Assert.False(filter.Matches(Outside));
        var room = LocationCondition.Capture(Inside with { Room = 4 });
        Assert.False(room.Matches(Inside with { Room = 5 }));
    }

    [Fact]
    public void SavedIndoorDistrictRuleMatchesWithoutRewritingItsStoredConditions()
    {
        var rule = new ModLocationRule
        {
            Name = "Indoor address",
            Location = Address,
            Mods = ["test-mod"],
            CollectionId = Guid.NewGuid(),
            RefreshMode = ModRefreshMode.Furniture,
        };
        var configuration = new Configuration();
        configuration.ModRules.Enabled = true;
        configuration.ModRules.Rules.Add(rule);
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(configuration))!;
        restored.ValidateAndMigrate();
        var saved = Assert.Single(restored.ModRules.Rules);
        Assert.True(saved.Location.Matches(Inside));
        // Selection lists have independent instances after loading; persisted values must stay identical.
        Assert.Equal(JsonConvert.SerializeObject(rule.Location), JsonConvert.SerializeObject(saved.Location));
        Assert.Equal(rule.Id, saved.Id);
        Assert.Equal(rule.CollectionId, saved.CollectionId);
        Assert.Equal(rule.RefreshMode, saved.RefreshMode);
    }

}
