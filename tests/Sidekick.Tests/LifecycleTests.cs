using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Sidekick.Chat;
using Sidekick.Keywords;
using Sidekick.Player;
using Moq;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Sidekick.Tests;

public sealed class LifecycleTests
{
    [Fact]
    public void CharacterProfilesUseHomeWorldAndRefreshDelayedWorldData()
    {
        var client = new Mock<IClientState>();
        var player = new Mock<IPlayerState>();
        var framework = new Mock<IFramework>();
        client.SetupGet(x => x.IsLoggedIn).Returns(true);
        player.SetupGet(x => x.IsLoaded).Returns(true);
        player.SetupGet(x => x.CharacterName).Returns("Ann Example");
        using var tracker = new LocalCharacterTracker(client.Object, player.Object, framework.Object);
        Assert.Equal(0u, tracker.Current?.HomeWorldId);
        player.SetupGet(x => x.HomeWorld).Returns(new RowRef<World>(null!, 21, Lumina.Data.Language.English));
        player.SetupGet(x => x.CurrentWorld).Returns(new RowRef<World>(null!, 22, Lumina.Data.Language.English));
        framework.Raise(x => x.Update += null, framework.Object);
        Assert.Equal(21u, tracker.Current?.HomeWorldId);
        player.SetupGet(x => x.HomeWorld).Returns(new RowRef<World>(null!, 23, Lumina.Data.Language.English));
        tracker.Refresh();
        Assert.Equal(23u, tracker.Current?.HomeWorldId);
        client.SetupGet(x => x.IsLoggedIn).Returns(false);
        client.Raise(x => x.Logout += null, 0, 0);
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void IdentityTracksAlreadyLoggedInLogoutDelayedLoginAndCharacterSwitch()
    {
        var client = new Mock<IClientState>();
        var player = new Mock<IPlayerState>();
        var framework = new Mock<IFramework>();
        client.SetupGet(x => x.IsLoggedIn).Returns(true);
        player.SetupGet(x => x.IsLoaded).Returns(true);
        player.SetupGet(x => x.CharacterName).Returns("Ann Example");
        var tracker = new LocalCharacterTracker(client.Object, player.Object, framework.Object);
        Assert.Equal("Ann Example", tracker.Current?.DisplayName);

        client.SetupGet(x => x.IsLoggedIn).Returns(false);
        client.Raise(x => x.Logout += null, 0, 0);
        Assert.Null(tracker.Current);
        client.SetupGet(x => x.IsLoggedIn).Returns(true);
        player.SetupGet(x => x.IsLoaded).Returns(false);
        client.Raise(x => x.Login += null);
        Assert.Null(tracker.Current);
        player.SetupGet(x => x.IsLoaded).Returns(true);
        player.SetupGet(x => x.CharacterName).Returns("Iris Another");
        framework.Raise(x => x.Update += null, framework.Object);
        Assert.Equal("Iris Another", tracker.Current?.DisplayName);

        player.SetupGet(x => x.CharacterName).Returns("Jean-Luc Another");
        tracker.Refresh();
        Assert.Equal("Jean-Luc", tracker.Current?.FirstName);
        tracker.Dispose();
        Assert.Null(tracker.Current);
        client.Raise(x => x.Login += null);
        framework.Raise(x => x.Update += null, framework.Object);
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void SubscriptionChangesOnlyBodyAndStopsAfterDisposal()
    {
        var client = new Mock<IClientState>();
        var player = new Mock<IPlayerState>();
        var chat = new Mock<IChatGui>();
        client.SetupGet(x => x.IsLoggedIn).Returns(true);
        player.SetupGet(x => x.IsLoaded).Returns(true);
        player.SetupGet(x => x.CharacterName).Returns("Ann Example");
        using var tracker = new LocalCharacterTracker(client.Object, player.Object, Mock.Of<IFramework>());
        var original = new SeString(new TextPayload("Hi Ann!"));
        var sender = new SeString(new TextPayload("Other Person"));
        var message = new Mock<IHandleableChatMessage>();
        message.SetupProperty(x => x.Message, original);
        message.SetupProperty(x => x.Sender, sender);
        using (var subscription = new ChatSubscription(chat.Object, Mock.Of<IPluginLog>(),
                   new ChatHighlighter(new Configuration()), tracker))
        {
            chat.Raise(x => x.CheckMessageHandled += null, message.Object);
            Assert.NotSame(original, message.Object.Message);
            Assert.Same(sender, message.Object.Sender);
            message.Verify(x => x.PreventOriginal(), Times.Never);

            message.Object.Message = new SeString(new TextPayload("Nothing here"));
            message.Object.Sender = new SeString(new TextPayload("Ann Example"));
            var body = message.Object.Message;
            chat.Raise(x => x.CheckMessageHandled += null, message.Object);
            Assert.Same(body, message.Object.Message);
        }

        message.Object.Message = original;
        chat.Raise(x => x.CheckMessageHandled += null, message.Object);
        Assert.Same(original, message.Object.Message);
    }

    [Theory]
    [InlineData("Ann", true, false)]
    [InlineData("Example", true, false)]
    [InlineData("Ann Example", true, false)]
    [InlineData("ANN", true, false)]
    [InlineData("Ann", false, true)]
    [InlineData("Example", false, true)]
    [InlineData("Ann Example", false, true)]
    [InlineData("Ann sunflower", true, true)]
    [InlineData("sunflower", true, true)]
    public void AutomaticNameMentionsIgnoreTheirAuthorButSavedKeywordsStillApply(string text, bool own, bool expected)
    {
        var client = new Mock<IClientState>();
        var player = new Mock<IPlayerState>();
        var chat = new Mock<IChatGui>();
        client.SetupGet(x => x.IsLoggedIn).Returns(true);
        player.SetupGet(x => x.IsLoaded).Returns(true);
        player.SetupGet(x => x.CharacterName).Returns("Ann Example");
        using var tracker = new LocalCharacterTracker(client.Object, player.Object, Mock.Of<IFramework>());
        var configuration = new Configuration();
        configuration.Keywords.TrySave(KeywordProfile.Global, "sunflower");
        var original = new SeString(new TextPayload(text));
        var message = new Mock<IHandleableChatMessage>();
        message.SetupProperty(x => x.Message, original);
        message.SetupGet(x => x.SourceKind).Returns(own ? XivChatRelationKind.LocalPlayer : default);
        using var subscription = new ChatSubscription(chat.Object, Mock.Of<IPluginLog>(), new ChatHighlighter(configuration), tracker);

        chat.Raise(x => x.CheckMessageHandled += null, message.Object);

        Assert.Equal(expected, !ReferenceEquals(original, message.Object.Message));
        message.Verify(x => x.PreventOriginal(), Times.Never);
    }

    [Fact]
    public void CallbackFailurePreservesMessageAndHandledState()
    {
        var client = new Mock<IClientState>();
        var player = new Mock<IPlayerState>();
        var chat = new Mock<IChatGui>();
        using var tracker = new LocalCharacterTracker(client.Object, player.Object, Mock.Of<IFramework>());
        client.SetupGet(x => x.IsLoggedIn).Throws<InvalidOperationException>();
        var original = new SeString(new TextPayload("Ann"));
        var message = new Mock<IHandleableChatMessage>();
        message.SetupProperty(x => x.Message, original);
        using var subscription = new ChatSubscription(chat.Object, Mock.Of<IPluginLog>(),
            new ChatHighlighter(new Configuration()), tracker);
        chat.Raise(x => x.CheckMessageHandled += null, message.Object);
        Assert.Same(original, message.Object.Message);
        message.Verify(x => x.PreventOriginal(), Times.Never);
    }

    [Fact]
    public void UnreadableConfigurationCannotBeOverwritten()
    {
        var pluginInterface = new Mock<IDalamudPluginInterface>();
        pluginInterface.Setup(x => x.GetPluginConfig()).Throws<InvalidDataException>();
        var store = new ConfigurationStore(pluginInterface.Object, Mock.Of<IPluginLog>());
        Assert.False(store.CanSave);
        store.Value.Enabled = false;
        store.Save();
        pluginInterface.Verify(x => x.SavePluginConfig(It.IsAny<Dalamud.Configuration.IPluginConfiguration>()), Times.Never);
    }
}
