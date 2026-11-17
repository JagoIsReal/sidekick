using Dalamud.Game.ClientState;
using Dalamud.Plugin.Services;
using Moq;
using Sidekick.Core;
using Sidekick.Locations;

namespace Sidekick.Tests;

public sealed class EarlyHousingTests
{
    private static readonly CharacterIdentity Character = CharacterIdentity.FromName("Test Player", 21)!;
    private static readonly OutdoorHousingAddress Main = new(339, 21, 19, 1);
    private static readonly HousingZoneNotice Notice = new(339, 7);

    [Fact]
    public void PreviousWardCannotBeUsedJustBecauseZoneInitWasReceived()
    {
        var context = new EarlyHousingContext();
        context.Observe(false, Character, 339, Main, false, null);
        Assert.Null(context.Observe(true, Character, 339, Main, false, Notice));
        Assert.Null(context.Observe(true, Character, 339, Main, false, null));
        var subdivision = Assert.IsType<LocationSnapshot>(context.Observe(true, Character, 339,
            Main with { Division = 2 }, false, null));
        Assert.Equal(20, subdivision.Ward);
        Assert.Equal(2, subdivision.Division);
        Assert.Equal(21u, subdivision.WorldId);
    }

    [Fact]
    public void ReenteringTheSameWardRequiresAnObservedTeardown()
    {
        var context = new EarlyHousingContext();
        context.Observe(false, Character, 339, Main, false, null);
        Assert.Null(context.Observe(true, Character, 0, null, true, null));
        Assert.Null(context.Observe(true, Character, 339, Main, false, null));
        Assert.NotNull(context.Observe(true, Character, 339, Main, false, Notice));
    }

    [Fact]
    public void HousingTeardownIsNotRequiredWhenTheNewWardIsAlreadyObservable()
    {
        var context = new EarlyHousingContext();
        context.Observe(false, Character, 339, Main with { WardIndex = 18 }, false, null);
        Assert.NotNull(context.Observe(true, Character, 339, Main, false, Notice));
    }

    [Fact]
    public void ANoticeForAnotherTerritoryCannotConfirmAnAddress()
    {
        var context = new EarlyHousingContext();
        context.Observe(false, Character, 100, null, true, null);
        Assert.Null(context.Observe(true, Character, 339, Main, false, new(340, 7)));
        Assert.Null(context.Observe(true, Character, 340, Main, false, Notice));
        Assert.NotNull(context.Observe(true, Character, 339, Main, false, null));
    }

    [Fact]
    public void StartingInAMidTransitionDoesNotAssumeTheLoadedAddressIsFresh()
    {
        var context = new EarlyHousingContext();
        Assert.Null(context.Observe(true, Character, 339, Main, false, Notice));
        context.Observe(true, Character, 0, null, true, null);
        Assert.NotNull(context.Observe(true, Character, 339, Main, false, null));
    }

    [Fact]
    public void UnknownConditionsStayUnknownInsteadOfMatchingFalseOrAny()
    {
        var context = new EarlyHousingContext();
        context.Observe(false, Character, 100, null, true, null);
        var location = Assert.IsType<LocationSnapshot>(context.Observe(true, Character, 339, Main, false, Notice));
        var condition = new LocationCondition { HousingDistrictId = 339, Ward = 20, Division = 1, WorldId = 21 };
        Assert.True(condition.Matches(location));
        Assert.True((condition with { WeatherIds = [7] }).Matches(location));
        Assert.False((condition with { Plot = 1 }).Matches(location));
        Assert.False((condition with { Room = 1 }).Matches(location));
        Assert.False((condition with { Mounted = false }).Matches(location));
        Assert.False((condition with { Daytime = true }).Matches(location));
        Assert.False((condition with { OnlineStatusIds = [0] }).Matches(location));
        Assert.False((condition with { PublicInstanceId = 1 }).Matches(location));
        Assert.Null(location.Plot);
        Assert.Null(location.Room);
        Assert.Null(location.OnlineStatusId);
        Assert.Null(location.Mounted);
        Assert.Equal(Character, location.Character);
    }

    [Theory]
    [InlineData(0, 21, 19, 1)]
    [InlineData(339, 0, 19, 1)]
    [InlineData(339, 21, -1, 1)]
    [InlineData(339, 21, 30, 1)]
    [InlineData(339, 21, 19, 0)]
    [InlineData(339, 21, 19, 3)]
    public void InvalidNativeAddressNeverBecomesAnEarlyDestination(uint district, uint world, int ward, int division)
    {
        var context = new EarlyHousingContext();
        context.Observe(false, Character, 100, null, true, null);
        Assert.Null(context.Observe(true, Character, district, new(district, world, ward, division), false, Notice));
    }

    [Fact]
    public void ArrivalAndResetDiscardOldZoneAnnouncements()
    {
        var context = new EarlyHousingContext();
        context.Observe(false, Character, 100, null, true, null);
        Assert.NotNull(context.Observe(true, Character, 339, Main, false, Notice));
        context.Observe(false, Character, 339, Main, false, null);
        Assert.Null(context.Observe(true, Character, 339, Main with { WardIndex = 20 }, false, null));
        context.Reset();
        Assert.Null(context.Observe(true, Character, 339, Main, true, null));
    }

    [Fact]
    public void MissingCharacterCannotActivateCharacterOrAccountRules()
    {
        var context = new EarlyHousingContext();
        context.Observe(false, Character, 100, null, true, null);
        Assert.Null(context.Observe(true, null, 339, Main, false, Notice));
    }

    [Fact]
    public void AnInvalidBaselineIsNotEvidenceThatThePreviousOutdoorTerritoryWasUnloaded()
    {
        var context = new EarlyHousingContext();
        context.Observe(false, Character, 339, Main with { WorldId = 0 }, false, null);
        Assert.Null(context.Observe(true, Character, 339, Main, false, Notice));
    }

    [Fact]
    public void ReaderUnsubscribesAndNeverTouchesNativeStateAfterDispose()
    {
        var client = new Mock<IClientState>();
        var reader = new EarlyHousingReader(client.Object, Mock.Of<IPlayerState>());
        client.VerifyAdd(c => c.ZoneInit += It.IsAny<Action<ZoneInitEventArgs>>(), Times.Once);
        Assert.Null(reader.Read(true)); // Disconnected, safe without a game process.
        reader.Dispose();
        reader.Dispose();
        Assert.Null(reader.Read(true));
        client.VerifyRemove(c => c.ZoneInit -= It.IsAny<Action<ZoneInitEventArgs>>(), Times.Once);
    }
}
