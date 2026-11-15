using Dalamud.Plugin.Services;
using Moq;
using Newtonsoft.Json;
using Sidekick.Locations;
using Sidekick.Mods;

namespace Sidekick.Tests;

public sealed class ModRuleTests
{
    private static readonly Guid Collection = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly LocationSnapshot Here = new(100, 21, 2, 339, 3, 17, 4, 1, true);
    private static ModLocationRule Rule(ModAction action = ModAction.Enable, params string[] mods) => new()
    {
        Name = "Test rule",
        Location = new LocationCondition { TerritoryId = 100 },
        Action = action,
        Mods = mods.Length == 0 ? ["test-mod"] : [.. mods],
    };

    [Fact]
    public void CapturedHousingAddressKeepsAddressConditionsAcrossInteriorTerritoryChanges()
    {
        var filter = LocationCondition.Capture(Here);
        Assert.True(filter.Matches(Here));
        Assert.Equal(Here.HousingDistrictId, filter.TerritoryId);
        Assert.True(filter.Matches(Here with { TerritoryId = 101 }));
        Assert.False(filter.Matches(Here with { WorldId = 22 }));
        Assert.False(filter.Matches(Here with { PublicInstanceId = 1 }));
        Assert.False(filter.Matches(Here with { HousingDistrictId = 340 }));
        Assert.False(filter.Matches(Here with { Ward = 4 }));
        Assert.False(filter.Matches(Here with { Plot = 18 }));
        Assert.False(filter.Matches(Here with { Room = 5 }));
        Assert.False(filter.Matches(Here with { Division = 2 }));
        Assert.False(filter.Matches(Here with { Indoors = false }));
        Assert.False(filter.Matches(new LocationSnapshot(100, 21, 2)));
    }

    [Fact]
    public void WildcardsAreExplicitAndAnEmptyFilterCannotAffectEverywhere()
    {
        Assert.False(new LocationCondition().Matches(Here));
        Assert.False(new LocationCondition { Ward = 0 }.IsValid);
        Assert.False(new LocationCondition { Plot = 61 }.IsValid);
        Assert.False(new LocationCondition { PublicInstanceId = 0 }.IsValid);
        var filter = new LocationCondition { WorldId = 21 };
        Assert.True(filter.Matches(Here));
        Assert.True(filter.Matches(new LocationSnapshot(999, 21, 0)));
        Assert.False(filter.Matches(Here with { WorldId = 22 }));
        var capture = LocationCondition.Capture(new LocationSnapshot(999, 21, 0));
        Assert.Equal(999u, capture.TerritoryId);
        Assert.False(capture.Matches(new LocationSnapshot(998, 21, 0)));
        Assert.Null(capture.PublicInstanceId);
        Assert.Null(capture.Ward);
    }

    [Theory]
    [InlineData(0, 0, 0, 1, false, 1, 1, null, 1)]
    [InlineData(29, 59, 4, 1, true, 30, 60, 4, 2)]
    [InlineData(2, -128, 8, 0, true, 3, null, 8, 1)]
    [InlineData(2, -127, 8, 0, true, 3, null, 8, 2)]
    [InlineData(2, -1, 0, 0, false, 3, null, null, null)]
    public void NativeHousingIndicesAndApartmentSentinelsNormalizeSafely(int ward, int plot, int room,
        int division, bool indoors, int expectedWard, int? expectedPlot, int? expectedRoom, int? expectedDivision)
    {
        var snapshot = new LocationSnapshot(100, 21, 0).WithHousing(339, ward, plot, room, division, indoors);
        Assert.Equal(expectedWard, snapshot.Ward);
        Assert.Equal(expectedPlot, snapshot.Plot);
        Assert.Equal(expectedRoom, snapshot.Room);
        Assert.Equal(expectedDivision, snapshot.Division);
        Assert.Equal(indoors, snapshot.Indoors);
    }

    [Fact]
    public void InvalidNativeHousingDataCannotBecomeAMatchingAddress()
    {
        var original = new LocationSnapshot(100, 21, 0);
        Assert.Same(original, original.WithHousing(339, -1, 0, 0, 1, true));
        Assert.Same(original, original.WithHousing(339, 30, 0, 0, 1, true));
        Assert.Null(original.WithHousing(ushort.MaxValue, 0, -1, 0, 0, true).HousingDistrictId);
    }

    [Theory]
    [InlineData(ModAction.Enable, false, true)]
    [InlineData(ModAction.Disable, true, false)]
    [InlineData(ModAction.Toggle, true, false)]
    [InlineData(ModAction.Toggle, false, true)]
    public void ActionRunsOncePreservingPriorityAndOptions(ModAction action, bool baseline, bool expected)
    {
        var api = new FakePenumbra
        {
            Baseline = new ModState(baseline, 37,
            new Dictionary<string, IReadOnlyList<string>> { ["Color"] = new[] { "Blue" } })
        };
        var settings = new ModRuleSettings { Enabled = true, Rules = [Rule(action)] };
        var engine = new ModRuleEngine(settings, api);
        for (var i = 0; i < 20; i++)
            engine.Update(Here, Collection);
        var applied = Assert.Single(api.Temporary).Value;
        Assert.Equal(expected, applied.Enabled);
        Assert.Equal(37, applied.Priority);
        Assert.Equal("Blue", Assert.Single(applied.Options["Color"]));
        Assert.Equal(1, api.ApplyCalls);
        Assert.Single(api.Redraws);
        engine.Update(Here with { TerritoryId = 101 }, Collection);
        Assert.Empty(api.Temporary);
        Assert.Equal(baseline, api.Baseline.Enabled);
        Assert.Equal(0, engine.ActiveCount);
    }

    [Fact]
    public void ToggleReentryUsesThePermanentBaseline()
    {
        var api = new FakePenumbra();
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [Rule(ModAction.Toggle)] }, api);
        engine.Update(Here, Collection);
        engine.Update(null, null);
        engine.Update(Here, Collection);
        Assert.True(Assert.Single(api.Temporary).Value.Enabled);
        Assert.Equal(2, api.ApplyCalls);
    }

    [Fact]
    public void LastMatchingRuleWinsAndFallsBackWhenDisabledOrRemoved()
    {
        var api = new FakePenumbra();
        var first = Rule(ModAction.Enable);
        var last = Rule(ModAction.Disable);
        last.Location.WorldId = 21;
        var settings = new ModRuleSettings { Enabled = true, Rules = [first, last] };
        var engine = new ModRuleEngine(settings, api);
        engine.Update(Here, Collection);
        Assert.False(Assert.Single(api.Temporary).Value.Enabled);
        last.Enabled = false;
        settings.Changed();
        engine.Update(Here, Collection);
        Assert.True(Assert.Single(api.Temporary).Value.Enabled);
        settings.Rules.Clear();
        settings.Changed();
        engine.Update(Here, Collection);
        Assert.Empty(api.Temporary);
    }

    [Fact]
    public void MultipleModsAreBatchedAndCollectionChangesCleanTheOriginalCollection()
    {
        var api = new FakePenumbra();
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [Rule(ModAction.Enable, "a", "b")] }, api);
        engine.Update(Here, Collection);
        Assert.Equal(2, api.Temporary.Count);
        Assert.Single(api.Redraws);
        var nextCollection = Guid.NewGuid();
        engine.Update(Here, nextCollection);
        Assert.Equal(2, api.Temporary.Count);
        Assert.All(api.Temporary.Keys, key => Assert.Equal(nextCollection, key.Item1));
        engine.Update(null, null);
        Assert.Empty(api.Temporary);
    }

    [Fact]
    public void ForeignTemporarySettingsAndMissingModsAreSkippedWithoutRepeatedWrites()
    {
        var api = new FakePenumbra { MissingMod = "missing" };
        api.Temporary[(Collection, "foreign")] = api.Baseline;
        var engine = new ModRuleEngine(new ModRuleSettings
        {
            Enabled = true,
            Rules = [Rule(ModAction.Enable, "foreign", "missing", "own")]
        }, api);
        engine.Update(Here, Collection);
        engine.Update(Here, Collection);
        Assert.Equal(2, engine.SkippedCount);
        Assert.Equal(1, api.ApplyCalls);
        engine.Update(null, null);
        Assert.Equal("foreign", Assert.Single(api.Temporary).Key.Item2);
    }

    [Fact]
    public void ManualOverrideIsPreservedAndNotReassertedUntilReactivation()
    {
        var api = new FakePenumbra();
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [Rule()] }, api);
        engine.Update(Here, Collection);
        api.Temporary[(Collection, "test-mod")] = api.Baseline with { Priority = 88 };
        engine.Update(Here, Collection);
        engine.Update(null, null);
        Assert.Equal(88, Assert.Single(api.Temporary).Value.Priority);
        Assert.Equal(1, api.ApplyCalls);
    }

    [Fact]
    public void ModuleDisableAndLogoutCleanUpIndependentlyOfChatSettings()
    {
        var api = new FakePenumbra();
        var config = new Configuration { Enabled = false };
        config.ModRules.Enabled = true;
        config.ModRules.Rules.Add(Rule());
        var engine = new ModRuleEngine(config.ModRules, api);
        engine.Update(Here, Collection);
        Assert.Single(api.Temporary);
        config.ModRules.Enabled = false;
        engine.Update(Here, Collection);
        Assert.Empty(api.Temporary);
        config.ModRules.Enabled = true;
        engine.Update(Here, Collection);
        engine.Update(null, null);
        Assert.Empty(api.Temporary);
    }

    [Fact]
    public void FailedCleanupRemainsTrackedAndIsRetried()
    {
        var api = new FakePenumbra();
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [Rule()] }, api);
        engine.Update(Here, Collection);
        api.FailRelease = true;
        engine.Update(null, null);
        Assert.True(engine.CleanupPending);
        Assert.Equal(1, engine.ActiveCount);
        api.FailRelease = false;
        engine.Update(null, null);
        Assert.Empty(api.Temporary);
        Assert.Equal(0, engine.ActiveCount);
        Assert.False(engine.CleanupPending);
    }

    [Fact]
    public void OneIpcFailureDoesNotBlockOtherModsOrTheirCleanup()
    {
        var api = new FakePenumbra { ThrowOnRead = "a" };
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [Rule(ModAction.Enable, "a", "b", "c")] }, api);
        Assert.Throws<InvalidOperationException>(() => engine.Update(Here, Collection));
        Assert.Equal(2, api.Temporary.Count);
        api.ThrowOnRelease = "b";
        Assert.Throws<InvalidOperationException>(() => engine.Update(null, null));
        Assert.Equal("b", Assert.Single(api.Temporary).Key.Item2);
        Assert.True(engine.CleanupPending);
        api.ThrowOnRelease = null;
        engine.Update(null, null);
        Assert.Empty(api.Temporary);
    }

    [Fact]
    public void RedrawFailuresKeepTheRequestPendingWithoutTogglingAgain()
    {
        var api = new FakePenumbra { FailRedraw = true };
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [Rule(ModAction.Toggle)] }, api);
        Assert.Throws<InvalidOperationException>(() => engine.Update(Here, Collection));
        Assert.Single(api.Temporary);
        api.FailRedraw = false;
        engine.Update(Here, Collection);
        Assert.Single(api.Redraws);
        Assert.Equal(1, api.ApplyCalls);
    }

    [Fact]
    public void RejectedWritesWaitForExplicitRetryOrRuleReentry()
    {
        var api = new FakePenumbra { FailApply = true };
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [Rule()] }, api);
        engine.Update(Here, Collection);
        Assert.Empty(api.RefreshRequests);
        api.FailApply = false;
        engine.Update(Here, Collection);
        Assert.Empty(api.Temporary);
        engine.Retry();
        engine.Update(Here, Collection);
        Assert.Single(api.Temporary);
    }

    [Fact]
    public void ACollectionSwitchWaitsForCleanupBeforeAcquiringNewOverrides()
    {
        var api = new FakePenumbra();
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [Rule()] }, api);
        engine.Update(Here, Collection);
        api.FailRelease = true;
        var nextCollection = Guid.NewGuid();
        engine.Update(Here, nextCollection);
        Assert.Equal(Collection, Assert.Single(api.Temporary).Key.Item1);
        Assert.Equal(1, api.ApplyCalls);
        api.FailRelease = false;
        engine.Update(Here, nextCollection);
        Assert.Equal(nextCollection, Assert.Single(api.Temporary).Key.Item1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MigrationKeepsExistingSettingsAndOptsOutOfModRules(int version)
    {
        var config = JsonConvert.DeserializeObject<Configuration>($$"""{"Version":{{version}},"Enabled":false,"HighlightColorRgb":255}""")!;
        config.ValidateAndMigrate();
        Assert.False(config.Enabled);
        Assert.Equal(255u, config.HighlightColorRgb);
        Assert.False(config.ModRules.Enabled);
        Assert.Empty(config.ModRules.Rules);
    }

    [Fact]
    public void RulesRoundTripAndGeneralResetDoesNotRemoveOrDisableThem()
    {
        var config = new Configuration();
        config.ModRules.Enabled = true;
        config.ModRules.Rules.Add(Rule(ModAction.Toggle, "a", "b"));
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        restored.ValidateAndMigrate();
        restored.Reset();
        Assert.True(restored.ModRules.Enabled);
        var rule = Assert.Single(restored.ModRules.Rules);
        Assert.Equal(config.ModRules.Rules[0].Id, rule.Id);
        Assert.Equal(ModAction.Toggle, rule.Action);
        Assert.Equal(new[] { "a", "b" }, rule.Mods);
    }

    [Fact]
    public void ValidationDropsUnsafeRulesAndRepairsStableIdsWithoutPromotingScope()
    {
        var first = Rule();
        var duplicate = first.Copy();
        duplicate.Mods.Add("test-mod");
        var bad = Rule();
        bad.Location = new LocationCondition();
        var path = Rule(ModAction.Enable, "../bad");
        var settings = new ModRuleSettings { Rules = [first, duplicate, bad, path] };
        settings.Validate();
        Assert.Equal(2, settings.Rules.Count);
        Assert.NotEqual(first.Id, duplicate.Id);
        Assert.Single(duplicate.Mods);
        Assert.Equal(100u, duplicate.Location.TerritoryId);
    }

    [Fact]
    public void ControllerDebouncesLocationsHoldsDuringLoadingAndRecoversAfterPenumbraReload()
    {
        var reader = new FakeLocation { Current = Here };
        var api = new FakePenumbra();
        var framework = new Mock<IFramework>();
        var settings = new ModRuleSettings { Enabled = true, Rules = [Rule(ModAction.Toggle)] };
        using var controller = new ModRuleController(framework.Object, Mock.Of<IPluginLog>(), reader, api, settings);
        controller.Poll(1000);
        Assert.Empty(api.Temporary);
        controller.Poll(2000);
        Assert.Single(api.Temporary);
        reader.IsTransitioning = true;
        controller.Poll(2500);
        Assert.Single(api.Temporary);
        reader.IsTransitioning = false;
        controller.Poll(3000);
        controller.Poll(4000);
        Assert.Equal(1, api.ApplyCalls);
        api.Available = false;
        api.Temporary.Clear();
        controller.Poll(4500);
        api.Available = true;
        controller.Poll(5000);
        Assert.Single(api.Temporary);
        Assert.Equal(2, api.ApplyCalls);
        reader.Current = null;
        controller.Poll(5500);
        Assert.Empty(api.Temporary);
        controller.Dispose();
        controller.Poll(10000);
        Assert.Empty(api.Temporary);
        framework.VerifyRemove(f => f.Update -= It.IsAny<IFramework.OnUpdateDelegate>(), Times.Once);
    }

    [Fact]
    public void DisablingDuringLoadingAndDisposingBothReleaseOverrides()
    {
        var reader = new FakeLocation { Current = Here };
        var api = new FakePenumbra();
        var settings = new ModRuleSettings { Enabled = true, Rules = [Rule()] };
        var controller = new ModRuleController(Mock.Of<IFramework>(), Mock.Of<IPluginLog>(), reader, api, settings);
        controller.Poll(1000);
        controller.Poll(2000);
        reader.IsTransitioning = true;
        settings.Enabled = false;
        controller.Poll(2500);
        Assert.Empty(api.Temporary);
        settings.Enabled = true;
        reader.IsTransitioning = false;
        controller.Poll(3000);
        controller.Poll(4000);
        Assert.Single(api.Temporary);
        controller.Dispose();
        Assert.Empty(api.Temporary);
    }

    [Fact]
    public void ExplicitCollectionCanWorkWithoutACharacterCollectionAndNeverFallsBack()
    {
        var selected = Guid.NewGuid();
        var rule = Rule();
        rule.CollectionId = selected;
        var api = new FakePenumbra();
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [rule] }, api);
        engine.Update(Here, null);
        Assert.Equal(selected, Assert.Single(api.Temporary).Key.Item1);
        engine.Update(null, null);
        Assert.Empty(api.Temporary);
        api.MissingCollectionId = selected;
        engine.Update(Here, Collection);
        Assert.Empty(api.Temporary);
    }

    [Fact]
    public void TheSameModCanHaveIndependentRulesInDifferentCollections()
    {
        var fixedId = Guid.NewGuid();
        var first = Rule(ModAction.Enable);
        var second = Rule(ModAction.Disable);
        second.CollectionId = fixedId;
        var api = new FakePenumbra();
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [first, second] }, api);
        engine.Update(Here, Collection);
        Assert.Equal(2, api.Temporary.Count);
        Assert.True(api.Temporary[(Collection, "test-mod")].Enabled);
        Assert.False(api.Temporary[(fixedId, "test-mod")].Enabled);
        engine.Update(null, null);
        Assert.Empty(api.Temporary);
    }

    [Fact]
    public void DynamicAndExplicitTargetsSharePrecedenceWhenTheyResolveToTheSameCollection()
    {
        var first = Rule(ModAction.Enable);
        var second = Rule(ModAction.Disable);
        second.CollectionId = Collection;
        var api = new FakePenumbra();
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [first, second] }, api);
        engine.Update(Here, Collection);
        Assert.False(Assert.Single(api.Temporary).Value.Enabled);
        Assert.Equal(1, api.ApplyCalls);
    }

    [Fact]
    public void SelectedCollectionIsStableAcrossCharacterChangesAndEditingCleansTheOriginal()
    {
        var previousDefault = Guid.NewGuid();
        var nextDefault = Guid.NewGuid();
        var rule = Rule();
        rule.CollectionId = previousDefault;
        rule.RefreshMode = ModRefreshMode.Furniture;
        var api = new FakePenumbra();
        var reader = new FakeLocation { Current = Here };
        var settings = new ModRuleSettings { Enabled = true, Rules = [rule] };
        using var controller = new ModRuleController(Mock.Of<IFramework>(), Mock.Of<IPluginLog>(), reader, api,
            settings);
        controller.Poll(1000);
        controller.Poll(2000);
        Assert.Equal(previousDefault, Assert.Single(api.Temporary).Key.Item1);
        api.CurrentCollectionId = Guid.NewGuid();
        controller.Poll(2500);
        Assert.Equal(previousDefault, Assert.Single(api.Temporary).Key.Item1);
        Assert.Equal(1, api.ApplyCalls);
        rule.CollectionId = nextDefault;
        settings.Changed();
        controller.Poll(3000);
        Assert.Equal(nextDefault, Assert.Single(api.Temporary).Key.Item1);
        Assert.All(api.RefreshRequests, r => Assert.Equal(ModRefreshMode.Furniture, r.Mode));
        controller.Dispose();
        Assert.Empty(api.Temporary);
    }

    [Theory]
    [InlineData(ModRefreshMode.CollectionMembers)]
    [InlineData(ModRefreshMode.All)]
    [InlineData(ModRefreshMode.Self)]
    [InlineData(ModRefreshMode.Furniture)]
    public void RefreshModeIsUsedOncePerBatchOnApplyAndRemoval(ModRefreshMode mode)
    {
        var api = new FakePenumbra();
        var rule = Rule(ModAction.Enable, "a", "b");
        rule.RefreshMode = mode;
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [rule] }, api);
        engine.Update(Here, Collection);
        Assert.Equal(new ModRefreshRequest(Collection, mode), Assert.Single(api.RefreshRequests));
        engine.Update(Here, Collection);
        Assert.Single(api.RefreshRequests);
        engine.Update(null, null);
        Assert.Equal(2, api.RefreshRequests.Count);
        Assert.All(api.RefreshRequests, r => Assert.Equal(mode, r.Mode));
    }

    [Fact]
    public void RedrawAllCoalescesActorRefreshesButDoesNotDropFurniture()
    {
        var modes = new[] { ModRefreshMode.All, ModRefreshMode.Self, ModRefreshMode.Furniture, ModRefreshMode.CollectionMembers };
        var rules = modes.Select((mode, i) =>
        {
            var rule = Rule(ModAction.Enable, $"mod-{i}");
            rule.RefreshMode = mode;
            return rule;
        }).ToList();
        var api = new FakePenumbra();
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = rules }, api);
        engine.Update(Here, Collection);
        Assert.Equal(new[] { ModRefreshMode.All, ModRefreshMode.Furniture }, api.RefreshRequests.Select(r => r.Mode));
        api.RefreshRequests.Clear();
        engine.Update(null, null);
        Assert.Equal(new[] { ModRefreshMode.All, ModRefreshMode.Furniture }, api.RefreshRequests.Select(r => r.Mode));
    }

    [Fact]
    public void ChangingRuleTargetAndRefreshCleansUpWithThePreviousPolicy()
    {
        var original = Guid.NewGuid();
        var replacement = Guid.NewGuid();
        var rule = Rule();
        rule.CollectionId = original;
        rule.RefreshMode = ModRefreshMode.Self;
        var settings = new ModRuleSettings { Enabled = true, Rules = [rule] };
        var api = new FakePenumbra();
        var engine = new ModRuleEngine(settings, api);
        engine.Update(Here, Collection);
        api.RefreshRequests.Clear();
        rule.CollectionId = replacement;
        rule.RefreshMode = ModRefreshMode.Furniture;
        settings.Changed();
        engine.Update(Here, Collection);
        Assert.Equal(replacement, Assert.Single(api.Temporary).Key.Item1);
        Assert.Contains(new ModRefreshRequest(original, ModRefreshMode.Self), api.RefreshRequests);
        Assert.Contains(new ModRefreshRequest(replacement, ModRefreshMode.Furniture), api.RefreshRequests);
    }

    [Fact]
    public void VersionFourRulesRetainTheirCollectionAndRefreshDefaults()
    {
        var original = new Configuration();
        original.ModRules.Enabled = true;
        original.ModRules.Rules.Add(Rule());
        var json = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(original));
        json["Version"] = 4;
        foreach (var entry in json["ModRules"]!["Rules"]!.Cast<Newtonsoft.Json.Linq.JObject>())
        {
            entry.Remove("CollectionId");
            entry.Remove("RefreshMode");
        }
        var restored = json.ToObject<Configuration>()!;
        restored.ValidateAndMigrate();
        Assert.True(restored.ModRules.Enabled);
        var rule = Assert.Single(restored.ModRules.Rules);
        Assert.Equal(ModRefreshMode.CollectionMembers, rule.RefreshMode);
        Assert.Null(rule.CollectionId);
        rule.CollectionId = Guid.NewGuid();
        rule.RefreshMode = ModRefreshMode.Furniture;
        var roundtrip = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(restored))!;
        roundtrip.ValidateAndMigrate();
        var copy = Assert.Single(roundtrip.ModRules.Rules);
        Assert.Equal(rule.CollectionId, copy.CollectionId);
        Assert.Equal(rule.RefreshMode, copy.RefreshMode);
        Assert.Equal(rule.CollectionId, copy.Copy().CollectionId);
    }

    [Fact]
    public void InvalidCollectionTargetsAndRefreshModesCannotRedirectAnEnabledRule()
    {
        var missing = Rule();
        missing.CollectionId = Guid.Empty;
        var refresh = Rule();
        refresh.RefreshMode = (ModRefreshMode)999;
        var settings = new ModRuleSettings { Enabled = true, Rules = [missing, refresh] };
        settings.Validate();
        Assert.Empty(settings.Rules);
    }

    [Fact]
    public void RefreshingCollectionListsDoesNotRetrySkippedRules()
    {
        var api = new FakePenumbra { MissingMod = "test-mod" };
        var reader = new FakeLocation { Current = Here };
        var settings = new ModRuleSettings { Enabled = true, Rules = [Rule()] };
        using var controller = new ModRuleController(Mock.Of<IFramework>(), Mock.Of<IPluginLog>(), reader, api, settings);
        controller.Poll(1000);
        controller.Poll(2000);
        Assert.Empty(api.Temporary);
        api.MissingMod = null;
        controller.RefreshLists();
        controller.Poll(2500);
        Assert.Empty(api.Temporary);
        controller.Refresh();
        controller.Poll(3000);
        Assert.Single(api.Temporary);
    }

    [Fact]
    public void IndoorModsApplyOnEntryStayStableAcrossInteriorChangesAndRestoreOnExit()
    {
        var outside = new LocationSnapshot(339, 21, 0, 339, 7, 18, null, 1, false);
        var inside = outside with { TerritoryId = 1001, Indoors = true };
        var rule = Rule();
        rule.Location = LocationCondition.Capture(outside) with { Indoors = true };
        rule.RefreshMode = ModRefreshMode.Furniture;
        var api = new FakePenumbra();
        var engine = new ModRuleEngine(new ModRuleSettings { Enabled = true, Rules = [rule] }, api);
        engine.Update(outside, Collection);
        Assert.Empty(api.Temporary);
        engine.Update(inside, Collection);
        Assert.True(Assert.Single(api.Temporary).Value.Enabled);
        engine.Update(inside with { TerritoryId = 1002 }, Collection);
        Assert.Equal(1, api.ApplyCalls);
        Assert.Single(api.RefreshRequests);
        engine.Update(outside, Collection);
        Assert.Empty(api.Temporary);
        Assert.Equal(2, api.RefreshRequests.Count);
        Assert.All(api.RefreshRequests, request => Assert.Equal(ModRefreshMode.Furniture, request.Mode));
    }

    private sealed class FakeLocation : ILocationReader
    {
        public bool IsTransitioning { get; set; }
        internal LocationSnapshot? Current { get; set; }
        public LocationSnapshot? Read() => Current;
    }

    private sealed class FakePenumbra : IPenumbraGateway
    {
        public bool Available { get; set; } = true;
        internal ModState Baseline { get; set; } = new(false, 0, new Dictionary<string, IReadOnlyList<string>>());
        internal readonly Dictionary<(Guid, string), ModState> Temporary = [];
        internal readonly List<Guid> Redraws = [];
        internal int ApplyCalls;
        internal string? MissingMod;
        internal bool FailRelease;
        internal bool FailApply;
        internal string? ThrowOnRead;
        internal string? ThrowOnRelease;
        internal bool FailRedraw;
        internal Guid? CurrentCollectionId = Collection;
        internal Guid? MissingCollectionId;
        internal readonly List<ModRefreshRequest> RefreshRequests = [];
        public Guid? CurrentCollection() => CurrentCollectionId;
        public IReadOnlyDictionary<Guid, string> GetCollections() => new Dictionary<Guid, string> { [Collection] = "Test collection" };
        public IReadOnlyDictionary<string, string> GetMods() => new Dictionary<string, string> { ["test-mod"] = "Test mod" };
        public ModState? ReadPermanent(Guid collection, string mod) => mod == ThrowOnRead
            ? throw new InvalidOperationException("Simulated read failure") : mod == MissingMod || collection == MissingCollectionId ? null : Baseline;
        public bool HasTemporary(Guid collection, string mod) => Temporary.ContainsKey((collection, mod));
        public bool IsApplied(Guid collection, string mod, ModState applied) => Temporary.GetValueOrDefault((collection, mod)) == applied;
        public bool Apply(Guid collection, string mod, ModState state)
        {
            ApplyCalls++;
            if (FailApply)
                return false;
            Temporary[(collection, mod)] = state;
            return true;
        }
        public bool Release(Guid collection, string mod, ModState applied)
        {
            if (mod == ThrowOnRelease)
                throw new InvalidOperationException("Simulated release failure");
            if (FailRelease)
                return false;
            if (IsApplied(collection, mod, applied))
                Temporary.Remove((collection, mod));
            return true;
        }
        public void Redraw(Guid collection, ModRefreshMode mode)
        {
            if (FailRedraw)
                throw new InvalidOperationException("Simulated redraw failure");
            Redraws.Add(collection);
            RefreshRequests.Add(new ModRefreshRequest(collection, mode));
        }
    }
}
