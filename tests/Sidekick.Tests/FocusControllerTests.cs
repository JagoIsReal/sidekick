using System.Numerics;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using Moq;
using Sidekick.Chat;
using Sidekick.Focus;
using Sidekick.Player;

namespace Sidekick.Tests;

public sealed class FocusControllerTests
{
    private static readonly FocusCharacter Self = new("Ann Example", 21);
    private static readonly FocusCharacter Other = new("Iris Another", 22);

    [Fact]
    public void EachMessageUsesCurrentPositionAndDisablingDoesNotRewriteOldMessages()
    {
        var fixture = new Fixture();
        var body = new SeString(new TextPayload("Hello"));
        var context = Message(Other, body);
        var dimmed = fixture.Controller.Transform(context.Object, body);
        Assert.NotSame(body, dimmed);
        var saved = dimmed.Encode();
        fixture.Scene.Current = new FocusScene(Self, [new(Other, 10)]);
        Assert.Same(body, fixture.Controller.Transform(context.Object, body));
        fixture.Scene.Current = new FocusScene(Self, [new(Other, 100)]);
        fixture.Controller.SetEnabled(false);
        Assert.Same(body, fixture.Controller.Transform(context.Object, body));
        Assert.Equal(saved, dimmed.Encode());
        fixture.Controller.SetEnabled(true);
        Assert.NotSame(body, fixture.Controller.Transform(context.Object, body));
    }

    [Fact]
    public void ListedRemoteCharactersUseTheirHomeWorldEvenWithoutNearbyActors()
    {
        var fixture = new Fixture();
        fixture.Store.Value.Focus.Add(Other.Name, Other.HomeWorldId);
        fixture.Scene.Current = new FocusScene(Self, []);
        var body = new SeString(new TextPayload("Hello"));
        Assert.Same(body, fixture.Controller.Transform(Message(Other, body).Object, body));
        var otherWorld = Other with { HomeWorldId = 23 };
        Assert.NotSame(body, fixture.Controller.Transform(Message(otherWorld, body).Object, body));
    }

    [Fact]
    public void OriginalSenderTakesPrecedenceOverModifiedOrAbbreviatedDisplayNames()
    {
        var fixture = new Fixture();
        fixture.Store.Value.Focus.UseDistance = false;
        fixture.Store.Value.Focus.Add(Other.Name, Other.HomeWorldId);
        var body = new SeString(new TextPayload("Hello"));
        var message = Message(Self, body);
        message.SetupGet(x => x.OriginalSender).Returns(new ReadOnlySeString(new PlayerPayload(Other.Name, Other.HomeWorldId).Encode()));
        message.Object.Sender = new SeString(new TextPayload("I. A."));
        Assert.Same(body, fixture.Controller.Transform(message.Object, body));
    }

    [Fact]
    public void PlainNamesMustMatchOneNearbyCharacterAndAmbiguousNamesRemainReadable()
    {
        var scene = new FocusScene(Self, [new(Other, 100)]);
        var sender = new SeString(new TextPayload("Iris Another"));
        Assert.Equal(Other, FocusController.ResolveSender(sender, scene));
        Assert.Null(FocusController.ResolveSender(new SeString(new TextPayload("I. A.")), scene));
        Assert.Null(FocusController.ResolveSender(sender, scene with { Players = [new(Other, 100), new(Other with { HomeWorldId = 23 }, 10)] }));
        Assert.Null(FocusController.ResolveSender(sender, scene with { Players = [] }));
    }

    [Fact]
    public void MissingSceneUnknownAuthorAndOwnMessagesArePreserved()
    {
        var fixture = new Fixture();
        var body = new SeString(new TextPayload("Hello"));
        var unknown = new Mock<IHandleableChatMessage>();
        unknown.SetupGet(x => x.LogKind).Returns(XivChatType.Say);
        unknown.SetupProperty(x => x.Sender, new SeString(new TextPayload("Unknown")));
        Assert.Same(body, fixture.Controller.Transform(unknown.Object, body));
        var own = Message(Other, body);
        own.SetupGet(x => x.SourceKind).Returns(XivChatRelationKind.LocalPlayer);
        Assert.Same(body, fixture.Controller.Transform(own.Object, body));
        fixture.Scene.Current = null;
        Assert.Same(body, fixture.Controller.Transform(Message(Other, body).Object, body));
    }

    [Fact]
    public void PlainSenderNamesUseNfcAndAnAuthorIsNeverInferredFromABodyLink()
    {
        var author = new FocusCharacter("Irís Another", 22);
        var scene = new FocusScene(Self, [new(author, 100)]);
        Assert.Equal(author, FocusController.ResolveSender(new SeString(new TextPayload("Iri\u0301s Another")), scene));
        var fixture = new Fixture();
        var body = SeString.Parse(new PlayerPayload(Other.Name, Other.HomeWorldId).Encode());
        var message = new Mock<IHandleableChatMessage>();
        message.SetupGet(x => x.LogKind).Returns(XivChatType.Say);
        message.SetupProperty(x => x.Sender, new SeString());
        Assert.Same(body, fixture.Controller.Transform(message.Object, body));
    }

    [Fact]
    public void FullOpacityAndExcludedChannelsAvoidReadingGameObjects()
    {
        var fixture = new Fixture();
        var body = new SeString(new TextPayload("Hello"));
        var message = Message(Other, body);
        message.SetupGet(x => x.LogKind).Returns(XivChatType.SystemMessage);
        Assert.Same(body, fixture.Controller.Transform(message.Object, body));
        fixture.Store.Value.Focus.OtherOpacityPercent = 100;
        Assert.Same(body, fixture.Controller.Transform(Message(Other, body).Object, body));
        Assert.Equal(0, fixture.Scene.Reads);
    }

    [Theory]
    [InlineData(25, 0x40F7F7F7u)]
    [InlineData(60, 0x99F7F7F7u)]
    public void NoEnabledCriteriaDimsOthersUsingConfiguredOpacityAndIgnoresSavedExemptions(int percent, uint expected)
    {
        var fixture = new Fixture();
        fixture.Store.Value.Focus.UseDistance = false;
        fixture.Store.Value.Focus.UseParticipants = false;
        fixture.Store.Value.Focus.OtherOpacityPercent = percent;
        fixture.Store.Value.Focus.Add(Other.Name, Other.HomeWorldId);
        fixture.Scene.Current = new FocusScene(Self, [new(Other, 1)]);
        var body = new SeString(new TextPayload("Hello"));
        var output = fixture.Controller.Transform(Message(Other, body).Object, body);
        Assert.Equal(expected, Assert.Single(SeStringOpacityTests.ColorsAtText(output)));
        Assert.Same(body, fixture.Controller.Transform(Message(Self, body).Object, body));
        fixture.Controller.SetEnabled(false);
        Assert.Same(body, fixture.Controller.Transform(Message(Other, body).Object, body));
    }

    [Fact]
    public void UnknownChannelColorPreservesMessageAndReportsADataFreeStatus()
    {
        var fixture = new Fixture();
        fixture.Colors.BaseColor = null;
        var body = new SeString(new TextPayload("Hello"));
        Assert.Same(body, fixture.Controller.Transform(Message(Other, body).Object, body));
        Assert.Equal(1, fixture.Controller.UnresolvedColors);
    }

    [Fact]
    public void ChatSubscriptionComposesHighlightThenOpacityAndUnsubscribes()
    {
        var fixture = new Fixture();
        var chat = new Mock<IChatGui>();
        var client = new Mock<IClientState>();
        var player = new Mock<IPlayerState>();
        client.SetupGet(x => x.IsLoggedIn).Returns(true);
        player.SetupGet(x => x.IsLoaded).Returns(true);
        player.SetupGet(x => x.CharacterName).Returns(Self.Name);
        using var tracker = new LocalCharacterTracker(client.Object, player.Object, Mock.Of<IFramework>());
        var body = new SeString(new TextPayload("Hello Ann!"));
        var message = Message(Other, body);
        var sender = message.Object.Sender;
        using (var subscription = new ChatSubscription(chat.Object, Mock.Of<IPluginLog>(), new ChatHighlighter(fixture.Store.Value), tracker, fixture.Controller))
        {
            chat.Raise(x => x.CheckMessageHandled += null, message.Object);
            Assert.Equal(0x40FFD54Fu, Assert.Single(SeStringOpacityTests.ColorsAtText(message.Object.Message)));
            Assert.Same(sender, message.Object.Sender);
            message.Verify(x => x.PreventOriginal(), Times.Never);
        }
        message.Object.Message = body;
        chat.Raise(x => x.CheckMessageHandled += null, message.Object);
        Assert.Same(body, message.Object.Message);
    }

    [Fact]
    public void FocusWorksWithHighlightingDisabledAndDoesNotSavePerMessage()
    {
        var fixture = new Fixture();
        fixture.Store.Value.Enabled = false;
        var body = new SeString(new TextPayload("Hello Ann!"));
        var highlighted = new ChatHighlighter(fixture.Store.Value).Transform(body, Sidekick.Core.CharacterIdentity.FromName(Self.Name));
        var result = fixture.Controller.Transform(Message(Other, body).Object, highlighted);
        Assert.Equal(0x40F7F7F7u, Assert.Single(SeStringOpacityTests.ColorsAtText(result)));
        fixture.PluginInterface.Verify(x => x.SavePluginConfig(It.IsAny<Dalamud.Configuration.IPluginConfiguration>()), Times.Never);
    }

    [Fact]
    public void ActivationUsesOneConfigurationAndSavesOnlyExplicitActions()
    {
        var fixture = new Fixture();
        Assert.False(fixture.Controller.SetEnabled());
        Assert.False(fixture.Store.Value.Focus.Enabled);
        Assert.True(fixture.Controller.SetEnabled(true));
        Assert.True(fixture.Store.Value.Focus.Enabled);
        fixture.PluginInterface.Verify(x => x.SavePluginConfig(It.IsAny<Dalamud.Configuration.IPluginConfiguration>()), Times.Exactly(2));
    }

    [Fact]
    public void UiOnlyRequestsSnapshotsAndDisposalStopsFrameworkAndChatWork()
    {
        var fixture = new Fixture();
        Assert.Null(fixture.Controller.Nearby());
        Assert.Equal(0, fixture.Scene.Reads);
        fixture.Framework.Raise(x => x.Update += null, fixture.Framework.Object);
        Assert.NotNull(fixture.Controller.Nearby());
        Assert.Equal(1, fixture.Scene.Reads);
        fixture.Controller.Dispose();
        fixture.Framework.Raise(x => x.Update += null, fixture.Framework.Object);
        Assert.Null(fixture.Controller.Nearby());
        var body = new SeString(new TextPayload("Hello"));
        Assert.Same(body, fixture.Controller.Transform(Message(Other, body).Object, body));
        Assert.Equal(1, fixture.Scene.Reads);
    }

    [Fact]
    public void NearbyReadFailureIsIsolatedAndVisibleWithoutLeakingGameData()
    {
        var fixture = new Fixture();
        fixture.Scene.Fail = true;
        fixture.Controller.Nearby();
        fixture.Framework.Raise(x => x.Update += null, fixture.Framework.Object);
        Assert.True(fixture.Controller.NearbyUnavailable);
        Assert.Null(fixture.Controller.Nearby());
    }

    [Fact]
    public void NearbySnapshotsUseThreeDimensionalDistanceAndHomeWorldAndClearDuringLoading()
    {
        var client = new Mock<IClientState>();
        var objects = new Mock<IObjectTable>();
        var condition = new Mock<ICondition>();
        var framework = new Mock<IFramework>();
        framework.SetupGet(x => x.IsInFrameworkUpdateThread).Returns(true);
        client.SetupGet(x => x.IsLoggedIn).Returns(true);
        var local = Player(Self, new Vector3(1, 2, 3));
        var other = Player(Other, new Vector3(1, 5, 7));
        objects.SetupGet(x => x.LocalPlayer).Returns(local);
        objects.SetupGet(x => x.PlayerObjects).Returns(new IBattleChara[] { local, other });
        var reader = new NearbyFocusPlayers(client.Object, objects.Object, condition.Object, framework.Object);
        var scene = reader.Read();
        Assert.NotNull(scene);
        Assert.Equal(Self, scene.Self);
        Assert.Equal(Other, Assert.Single(scene.Players).Character);
        Assert.Equal(5, scene.Players[0].DistanceYalms);
        objects.SetupGet(x => x.LocalPlayer).Returns(Player(Self, new Vector3(float.NaN, 0, 0)));
        Assert.Null(reader.Read());
        objects.SetupGet(x => x.LocalPlayer).Returns(local);
        framework.SetupGet(x => x.IsInFrameworkUpdateThread).Returns(false);
        Assert.Null(reader.Read());
        framework.SetupGet(x => x.IsInFrameworkUpdateThread).Returns(true);
        condition.Setup(x => x[ConditionFlag.BetweenAreas]).Returns(true);
        Assert.Null(reader.Read());
        condition.Setup(x => x[ConditionFlag.BetweenAreas]).Returns(false);
        client.SetupGet(x => x.IsLoggedIn).Returns(false);
        Assert.Null(reader.Read());
    }

    private static IPlayerCharacter Player(FocusCharacter character, Vector3 position)
    {
        var player = new Mock<IPlayerCharacter>();
        player.SetupGet(x => x.Name).Returns(new SeString(new TextPayload(character.Name)));
        player.SetupGet(x => x.HomeWorld).Returns(new RowRef<World>(null!, character.HomeWorldId, Lumina.Data.Language.English));
        player.SetupGet(x => x.Position).Returns(position);
        return player.Object;
    }

    private static Mock<IHandleableChatMessage> Message(FocusCharacter sender, SeString body)
    {
        var message = new Mock<IHandleableChatMessage>();
        message.SetupGet(x => x.LogKind).Returns(XivChatType.Say);
        message.SetupProperty(x => x.Message, body);
        message.SetupProperty(x => x.Sender, SeString.Parse(new PlayerPayload(sender.Name, sender.HomeWorldId).Encode()));
        return message;
    }

    private sealed class Fixture
    {
        internal readonly Mock<IDalamudPluginInterface> PluginInterface = new();
        internal readonly ConfigurationStore Store;
        internal readonly SceneSource Scene = new() { Current = new FocusScene(Self, [new(Other, 100)]) };
        internal readonly TestChatColors Colors = new();
        internal readonly FocusController Controller;
        internal readonly Mock<IFramework> Framework = new();

        internal Fixture()
        {
            var configuration = new Configuration();
            configuration.Focus.Enabled = true;
            PluginInterface.Setup(x => x.GetPluginConfig()).Returns(configuration);
            Store = new ConfigurationStore(PluginInterface.Object, Mock.Of<IPluginLog>());
            Controller = new FocusController(Store, Scene, Colors, Framework.Object, Mock.Of<IPluginLog>());
        }
    }

    private sealed class SceneSource : IFocusSceneSource
    {
        internal FocusScene? Current;
        internal int Reads;
        internal bool Fail;
        public FocusScene? Read()
        {
            Reads++;
            if (Fail)
                throw new InvalidOperationException("Simulated scene failure");
            return Current;
        }
    }
}
