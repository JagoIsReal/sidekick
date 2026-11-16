using Sidekick.Appearance;
using Sidekick.Core;
using Sidekick.Locations;
using Sidekick.Rules;
using Dalamud.Plugin.Services;
using Moq;

namespace Sidekick.Tests;

public sealed class GlamourerRuleTests
{
    private static readonly CharacterIdentity Ann = CharacterIdentity.FromName("Ann Example", 21)!;
    private static readonly LocationSnapshot Rain = new(100, 21, 0, Character: Ann, WeatherId: 7, Daytime: true);
    private static GlamourerRule Rule() => new() { Name = "Rain", DesignId = Guid.NewGuid(), Conditions = new() { WeatherId = 7 } };

    [Fact]
    public void AppliesOnceAndRestoresThePreviousStateWhenTheRuleEnds()
    {
        var api = new FakeGlamourer();
        var original = api.State;
        var settings = new GlamourerSettings { Enabled = true, Rules = [Rule()] };
        var engine = new GlamourerRuleEngine(settings, new(), api);
        for (var i = 0; i < 8; i++) engine.Update(Rain);
        Assert.Equal(1, api.Applies);
        Assert.Equal(1, engine.ActiveCount);
        engine.Update(Rain with { WeatherId = 1 });
        Assert.Equal(original, api.State);
        Assert.Equal(1, api.Restores);
        Assert.Equal(0, engine.ActiveCount);
    }

    [Fact]
    public void ManualChangesWinUntilTheNextActivationAndBecomeTheNewBaseline()
    {
        var api = new FakeGlamourer();
        var engine = new GlamourerRuleEngine(new() { Enabled = true, Rules = [Rule()] }, new(), api);
        engine.Update(Rain);
        api.State = "{\"Equipment\":{\"Body\":1234}}";
        var manual = api.State;
        engine.Update(Rain); engine.Update(Rain);
        Assert.Equal(manual, api.State);
        Assert.Equal(1, api.Applies);
        Assert.Equal(0, api.Restores);
        engine.Update(Rain with { WeatherId = 1 });
        engine.Update(Rain);
        Assert.Equal(2, api.Applies);
        engine.Update(Rain with { WeatherId = 1 });
        Assert.Equal(manual, api.State);
    }

    [Fact]
    public void ImmediateExplicitRetryAdoptsAManualChangeWithoutRestoringOverIt()
    {
        var api = new FakeGlamourer();
        var engine = new GlamourerRuleEngine(new() { Enabled = true, Rules = [Rule()] }, new(), api);
        engine.Update(Rain);
        api.State = "{\"Equipment\":{\"Body\":1234}}";
        var manual = api.State;
        engine.Retry();
        engine.Update(Rain);
        Assert.Equal(2, api.Applies);
        Assert.Equal(0, api.Restores);
        engine.Update(Rain with { WeatherId = 1 });
        Assert.Equal(manual, api.State);
    }

    [Fact]
    public void EditingAnUnrelatedProfileDoesNotReassertAnOverriddenOutfit()
    {
        var api = new FakeGlamourer();
        var profiles = new RuleProfileSettings();
        var engine = new GlamourerRuleEngine(new() { Enabled = true, Rules = [Rule()] }, profiles, api);
        engine.Update(Rain);
        api.State = "{\"Equipment\":{\"Body\":1234}}";
        var manual = api.State;
        engine.Update(Rain);
        profiles.TrySave(new() { CharacterName = "Iris Another", HomeWorldId = 22 });
        engine.Update(Rain);
        Assert.Equal(1, api.Applies);
        Assert.Equal(manual, api.State);
    }

    [Fact]
    public void ChangingBetweenMatchingRulesNeverMakesThePreviousDesignTheBaseline()
    {
        var api = new FakeGlamourer();
        var original = api.State;
        var first = Rule();
        var second = Rule(); second.Conditions = new() { Swimming = true };
        var engine = new GlamourerRuleEngine(new() { Enabled = true, Rules = [first, second] }, new(), api);
        engine.Update(Rain);
        engine.Update(Rain with { Swimming = true });
        Assert.Contains(second.DesignId.ToString(), api.State);
        engine.Update(Rain with { WeatherId = 1, Swimming = false });
        Assert.Equal(original, api.State);
        Assert.Equal(2, api.Applies);
        Assert.Equal(2, api.Restores);
    }

    [Fact]
    public void CharacterRulesOverrideAccountRegardlessOfTheirStoredInterleaving()
    {
        var profiles = new RuleProfileSettings();
        var profile = new RuleCharacterProfile { CharacterName = Ann.DisplayName, HomeWorldId = 21 };
        profiles.TrySave(profile);
        var personal = Rule(); personal.ProfileId = profile.Id;
        var account = Rule();
        var api = new FakeGlamourer();
        var settings = new GlamourerSettings { Enabled = true, Rules = [personal, account] };
        var engine = new GlamourerRuleEngine(settings, profiles, api);
        engine.Update(Rain);
        Assert.Contains(personal.DesignId.ToString(), api.State);
        personal.Enabled = false;
        engine.Update(Rain);
        Assert.Contains(account.DesignId.ToString(), api.State);
    }

    [Fact]
    public void ACharacterWithTheSameNameOnAnotherHomeWorldNeverReceivesTheOldAppearance()
    {
        var api = new FakeGlamourer();
        var original = api.State;
        var settings = new GlamourerSettings { Enabled = true, Rules = [Rule()] };
        var engine = new GlamourerRuleEngine(settings, new(), api);
        engine.Update(Rain);
        api.Character = null;
        engine.Update(null);
        Assert.Equal(1, engine.DeferredCount);
        var other = CharacterIdentity.FromName(Ann.DisplayName, 22)!;
        api.Character = other;
        var otherOriginal = api.State;
        engine.Update(Rain with { Character = other, WeatherId = 1 });
        Assert.Equal(otherOriginal, api.State);
        Assert.Equal(0, api.Restores);
        api.Character = Ann;
        engine.Update(Rain with { WeatherId = 1 });
        Assert.Equal(original, api.State);
        Assert.Equal(1, api.Restores);
    }

    [Fact]
    public void LockedUnavailableOrMissingDesignsDoNotCauseRepeatedWritesAndCanBeRetried()
    {
        var api = new FakeGlamourer { RejectApply = true };
        var engine = new GlamourerRuleEngine(new() { Enabled = true, Rules = [Rule()] }, new(), api);
        engine.Update(Rain); engine.Update(Rain);
        Assert.Equal(1, api.Applies);
        Assert.True(engine.Skipped);
        api.RejectApply = false;
        engine.Retry(); engine.Update(Rain);
        Assert.Equal(2, api.Applies);
        api.RejectRestore = true;
        engine.Update(Rain with { WeatherId = 1 });
        Assert.True(engine.CleanupPending);
        Assert.Equal(1, engine.ActiveCount);
        api.RejectRestore = false;
        engine.Update(Rain with { WeatherId = 1 });
        Assert.False(engine.CleanupPending);
        Assert.Equal(0, engine.ActiveCount);
    }

    [Fact]
    public void BlockingStatesHoldTheOutfitAndExplicitDisableOrUnloadRestoresIt()
    {
        var api = new FakeGlamourer();
        var original = api.State;
        var engine = new GlamourerRuleEngine(new() { Enabled = true, Rules = [Rule()] }, new(), api);
        engine.Update(Rain with { AppearanceBlocked = true });
        Assert.Equal(0, api.Applies);
        engine.Update(Rain);
        engine.Update(Rain with { WeatherId = 1, AppearanceBlocked = true });
        Assert.Equal(0, api.Restores);
        engine.Update(Rain with { AppearanceBlocked = true }, true);
        Assert.Equal(original, api.State);
    }

    [Fact]
    public void SemanticJsonComparisonDoesNotMistakePropertyOrderForAManualEdit()
    {
        Assert.True(GlamourerGateway.SameState("{\"A\":1,\"B\":2}", "{\"B\":2,\"A\":1}"));
        Assert.False(GlamourerGateway.SameState("{\"A\":1}", "{\"A\":2}"));
    }

    [Fact]
    public void ControllerDebouncesHoldsThroughLoadingRestoresOnLogoutAndUnsubscribes()
    {
        var api = new FakeGlamourer();
        var original = api.State;
        var reader = new FakeLocation { Current = Rain };
        var client = new Mock<IClientState>();
        var framework = new Mock<IFramework>();
        var settings = new GlamourerSettings { Enabled = true, Rules = [Rule()] };
        var controller = new GlamourerRuleController(framework.Object, client.Object, reader, api, Mock.Of<IPluginLog>(), settings, new());
        controller.Poll(1000);
        Assert.Equal(0, api.Applies);
        controller.Poll(2000);
        Assert.Equal(1, api.Applies);
        reader.IsTransitioning = true;
        controller.Poll(2500);
        Assert.Equal(0, api.Restores);
        reader.IsTransitioning = false;
        controller.Poll(3000); controller.Poll(4000);
        Assert.Equal(1, api.Applies);
        client.Raise(x => x.Logout += null, 0, 0);
        Assert.Equal(original, api.State);
        settings.Enabled = false;
        controller.Dispose(); controller.Poll(9000);
        framework.Raise(x => x.Update += null, framework.Object);
        Assert.Equal(1, api.Applies);
        framework.VerifyRemove(x => x.Update -= It.IsAny<IFramework.OnUpdateDelegate>(), Times.Once);
    }

    [Fact]
    public void ProviderReloadTreatsTheRuleAsANewActivationWithoutRestoringOverChangedState()
    {
        var api = new FakeGlamourer();
        var reader = new FakeLocation { Current = Rain };
        using var controller = new GlamourerRuleController(Mock.Of<IFramework>(), Mock.Of<IClientState>(), reader,
            api, Mock.Of<IPluginLog>(), new() { Enabled = true, Rules = [Rule()] }, new());
        controller.Poll(1000); controller.Poll(2000);
        api.Available = false;
        controller.Poll(2500);
        api.State = "{\"Equipment\":{\"Body\":1234}}";
        var reloaded = api.State;
        api.Available = true;
        controller.Poll(3000);
        Assert.Equal(2, api.Applies);
        Assert.Equal(0, api.Restores);
        reader.Current = Rain with { WeatherId = 1 };
        controller.Poll(3500); controller.Poll(4500);
        Assert.Equal(reloaded, api.State);
    }

    private sealed class FakeLocation : ILocationReader
    {
        public bool IsTransitioning { get; set; }
        internal LocationSnapshot? Current { get; set; }
        public LocationSnapshot? Read() => Current;
    }

    private sealed class FakeGlamourer : IGlamourerGateway
    {
        private readonly Dictionary<uint, string> states = [];
        public CharacterIdentity? Character { get; set; } = Ann;
        public bool Available { get; set; } = true;
        internal bool RejectApply { get; set; }
        internal bool RejectRestore { get; set; }
        internal int Applies { get; private set; }
        internal int Restores { get; private set; }
        internal string State
        {
            get => states.GetValueOrDefault(Character!.HomeWorldId) ?? "{\"Equipment\":{\"Body\":1}}";
            set => states[Character!.HomeWorldId] = value;
        }
        public IReadOnlyDictionary<Guid, string> GetDesigns() => new Dictionary<Guid, string>();
        public string? Capture(CharacterIdentity character) => Character == character ? State : null;
        public bool Apply(CharacterIdentity character, Guid design)
        {
            Applies++;
            if (RejectApply || Character != character) return false;
            State = "{\"Equipment\":{\"Design\":\"" + design + "\"}}";
            return true;
        }
        public bool Restore(CharacterIdentity character, string state)
        {
            Restores++;
            if (RejectRestore || Character != character) return false;
            State = state;
            return true;
        }
    }
}
