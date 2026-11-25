using Dalamud.Plugin.Services;
using Moq;
using Newtonsoft.Json;
using Sidekick.Core;
using Sidekick.Locations;
using Sidekick.Rules;
using Sidekick.Status;

namespace Sidekick.Tests;

public sealed class StatusRuleTests
{
    // OnlineStatus sheet rows, not condition flags.
    private const uint Novice = 32;
    private const uint Online = 47;
    private const uint Busy = 12;
    private const uint Away = 17;
    private static readonly CharacterIdentity Ann = CharacterIdentity.FromName("Ann Example", 21)!;
    private static readonly LocationSnapshot InGame = new(100, 22, 0, Character: Ann, OnlineStatusId: Novice, WeatherId: 7);

    [Fact]
    public void LoginRuleWaitsForTheFirstStableLoadedCharacterAndRunsOncePerLogin()
    {
        using var game = new Game();
        game.Settings.Rules.Add(new()
        {
            Name = "Novice to Online",
            AtLogin = true,
            TargetStatusId = Online,
            Conditions = new LocationCondition { OnlineStatusIds = [Novice] },
        });
        game.Login();
        game.Sample(0, null, transitioning: true);
        game.Sample(1500, InGame with { OnlineStatusId = null });
        game.Sample(3000, InGame with { AppearanceBlocked = true });
        game.Sample(4500, InGame);
        game.Sample(5000, InGame);
        Assert.Empty(game.Requests);
        game.Sample(5500, InGame);
        game.Sample(6000, InGame with { OnlineStatusId = 0 });
        game.Sample(7500, InGame);
        game.Sample(8500, InGame);
        Assert.Equal([Online], game.Requests);
        Assert.False(game.Controller.Failed);

        game.Logout();
        game.Login();
        game.Sample(9000, InGame);
        game.Sample(10000, InGame);
        Assert.Equal([Online, Online], game.Requests);
    }

    [Fact]
    public void LoginRulesAreNotRearmedByZoningOrLoadingSidekickMidSession()
    {
        using var game = new Game();
        game.Settings.Rules.Add(new() { Name = "On each login", AtLogin = true, TargetStatusId = Busy });
        game.Sample(0, InGame);
        game.Sample(1000, InGame);
        game.Sample(2000, null, transitioning: true);
        var newZone = InGame with { TerritoryId = 200 };
        game.Sample(3000, newZone);
        game.Sample(4000, newZone);
        Assert.Empty(game.Requests);
    }

    [Fact]
    public void LoginPrefersLastCharacterRuleAndDoesNotImmediatelyApplyAConditionalRule()
    {
        using var game = new Game();
        var profile = new RuleCharacterProfile { CharacterName = Ann.DisplayName, HomeWorldId = Ann.HomeWorldId };
        Assert.True(game.Profiles.TrySave(profile));
        game.Settings.Rules.AddRange([
            new() { Name = "First character rule", AtLogin = true, TargetStatusId = Away, ProfileId = profile.Id },
            new() { Name = "Last character rule", AtLogin = true, TargetStatusId = Online, ProfileId = profile.Id },
            new() { Name = "Last account rule", AtLogin = true, TargetStatusId = Busy },
            new() { Name = "Conditional", TargetStatusId = Busy, Conditions = new LocationCondition { WorldId = 22 } },
        ]);
        game.Login();
        game.Sample(0, InGame);
        game.Sample(1000, InGame);
        game.Sample(1500, InGame with { OnlineStatusId = Online });
        game.Sample(2500, InGame with { OnlineStatusId = Online });
        Assert.Equal([Online], game.Requests);

        game.Logout();
        game.Login();
        var sameNameOtherHomeWorld = InGame with { Character = CharacterIdentity.FromName(Ann.DisplayName, 22) };
        game.Sample(3000, sameNameOtherHomeWorld);
        game.Sample(4000, sameNameOtherHomeWorld);
        Assert.Equal([Online, Busy], game.Requests);
    }

    [Fact]
    public void StatusRuleUsesAllFiltersAndLeavesLaterManualChangesAloneWhileMatching()
    {
        using var game = new Game();
        game.Settings.Rules.Add(new()
        {
            Name = "Busy in this weather",
            TargetStatusId = Busy,
            Conditions = new LocationCondition { TerritoryId = 100, WeatherIds = [9, 10], Mounted = false },
        });
        var matchingWeather = InGame with { WeatherId = 9 };
        game.Sample(0, matchingWeather);
        game.Sample(1000, matchingWeather);
        Assert.Empty(game.Requests); // Mounted is unknown.

        var matching = matchingWeather with { Mounted = false };
        game.Sample(1500, matching);
        game.Sample(2500, matching);
        game.Sample(3000, matching with { OnlineStatusId = Busy });
        game.Sample(3500, matching with { OnlineStatusId = Away });
        game.Sample(4500, matching with { OnlineStatusId = Away });
        Assert.Equal([Busy], game.Requests);

        var elsewhere = matching with { TerritoryId = 200 };
        game.Sample(5000, elsewhere);
        game.Sample(6000, elsewhere);
        game.Sample(6500, matching);
        game.Sample(7500, matching);
        Assert.Equal([Busy, Busy], game.Requests);
    }

    [Fact]
    public void AConfirmedStatusDoesNotTriggerASecondStatusRule()
    {
        using var game = new Game();
        game.Settings.Rules.AddRange([
            new() { Name = "Online to Busy", TargetStatusId = Busy, Conditions = new LocationCondition { OnlineStatusIds = [0, Online] } },
            new() { Name = "Busy to Online", TargetStatusId = Online, Conditions = new LocationCondition { OnlineStatusIds = [Busy] } },
        ]);
        var online = InGame with { OnlineStatusId = 0 };
        var busy = InGame with { OnlineStatusId = Busy };
        game.Sample(0, online);
        game.Sample(1000, online);
        game.Sample(1500, busy);
        game.Sample(2500, busy);
        game.Sample(4000, busy);
        Assert.Equal([Busy], game.Requests);
    }

    [Fact]
    public void UnconfirmedChangesTimeOutWithoutRetryingOrLeakingIntoAnotherSession()
    {
        using var game = new Game();
        game.Settings.Rules.Add(new()
        {
            Name = "Set Away",
            TargetStatusId = Away,
            Conditions = new LocationCondition { TerritoryId = 100 },
        });
        game.Sample(0, InGame);
        game.Sample(1000, InGame);
        game.Sample(6000, InGame);
        game.Sample(9000, InGame);
        Assert.True(game.Controller.Failed);
        Assert.Equal([Away], game.Requests);
        game.Logout();
        Assert.False(game.Controller.Failed);
        game.Settings.Enabled = false;
        game.Login();
        game.Sample(10000, InGame);
        game.Sample(11000, InGame);
        Assert.Equal([Away], game.Requests);
    }

    [Fact]
    public void DisposeDetachesUpdateAndSessionEvents()
    {
        using var game = new Game();
        game.Framework.VerifyAdd(f => f.Update += It.IsAny<IFramework.OnUpdateDelegate>(), Times.Once);
        game.Controller.Dispose();
        game.Framework.VerifyRemove(f => f.Update -= It.IsAny<IFramework.OnUpdateDelegate>(), Times.Once);
        game.Client.VerifyRemove(c => c.Login -= It.IsAny<Action>(), Times.Once);
        game.Logout();
        game.Login();
        game.Framework.Raise(f => f.Update += null, game.Framework.Object);
        Assert.Null(game.Controller.Current);
        Assert.Empty(game.Requests);
    }

    [Fact]
    public void MigratedConfigIsOptInAndStatusRulesPersistWithIndependentDrafts()
    {
        var migrated = JsonConvert.DeserializeObject<Configuration>(
            """{"Version":9,"LanguageCode":"fr","Glamourer":{"Enabled":true}}""")!;
        migrated.ValidateAndMigrate();
        Assert.Equal(10, migrated.Version);
        Assert.True(migrated.Glamourer.Enabled);
        Assert.Equal("fr", migrated.LanguageCode);
        Assert.False(migrated.StatusRules.Enabled);
        Assert.Empty(migrated.StatusRules.Rules);

        var saved = new StatusRule
        {
            Name = "Novice to Online",
            AtLogin = true,
            TargetStatusId = Online,
            Conditions = new LocationCondition { OnlineStatusIds = [Novice] },
        };
        migrated.StatusRules.Enabled = true;
        migrated.StatusRules.Rules = [
            saved,
            new() { Name = "System-only status", AtLogin = true, TargetStatusId = 15 },
            new() { Name = "Unconditional during play", TargetStatusId = Online },
            new() { Name = "Invalid login filter", AtLogin = true, TargetStatusId = Online, Conditions = new() { WorldId = 0 } },
        ];
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(migrated))!;
        restored.ValidateAndMigrate();
        restored.Focus.Reset();
        restored.Reset();
        var rule = Assert.Single(restored.StatusRules.Rules);
        Assert.True(restored.StatusRules.Enabled);
        Assert.Equal(saved.Id, rule.Id);
        Assert.True(rule.AtLogin);
        Assert.Equal(Online, rule.TargetStatusId);
        rule.Copy().Conditions.OnlineStatusIds.Clear();
        Assert.Equal([Novice], rule.Conditions.OnlineStatusIds);
    }

    private sealed class Game : ILocationReader, IDisposable
    {
        internal readonly Mock<IClientState> Client = new();
        internal readonly Mock<IFramework> Framework = new();
        internal readonly StatusRuleSettings Settings = new() { Enabled = true };
        internal readonly RuleProfileSettings Profiles = new();
        internal readonly List<uint> Requests = [];
        internal readonly StatusRuleController Controller;
        private LocationSnapshot? snapshot;
        public bool IsTransitioning { get; private set; }

        internal Game()
        {
            Controller = new StatusRuleController(Framework.Object, Client.Object, this, (_, target) =>
            {
                Requests.Add(target);
                return true;
            }, Mock.Of<IPluginLog>(), Settings, Profiles);
        }

        internal void Sample(long now, LocationSnapshot? context, bool transitioning = false)
        {
            snapshot = context;
            IsTransitioning = transitioning;
            Controller.Poll(now);
        }

        public LocationSnapshot? Read() => snapshot;
        internal void Login() => Client.Raise(c => c.Login += null);
        internal void Logout() => Client.Raise(c => c.Logout += null, 0, 0);
        public void Dispose() => Controller.Dispose();
    }
}
