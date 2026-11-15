using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;
using Sidekick.Player;
using Sidekick.Focus;

namespace Sidekick.Chat;

internal sealed class ChatSubscription : IDisposable
{
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;
    private readonly ChatHighlighter highlighter;
    private readonly LocalCharacterTracker character;
    private readonly FocusController? focus;
    private bool reportedError;

    public ChatSubscription(IChatGui chatGui, IPluginLog log, ChatHighlighter highlighter, LocalCharacterTracker character,
        FocusController? focus = null)
    {
        this.chatGui = chatGui;
        this.log = log;
        this.highlighter = highlighter;
        this.character = character;
        this.focus = focus;
        chatGui.CheckMessageHandled += OnMessage;
    }

    public void Dispose() => chatGui.CheckMessageHandled -= OnMessage;

    private void OnMessage(IHandleableChatMessage message)
    {
        if (message.IsHandled)
            return;

        try
        {
            character.Refresh();
            var original = message.Message;
            var result = highlighter.Transform(original, character.Current);
            if (focus is not null)
                result = focus.Transform(message, result);
            if (!ReferenceEquals(original, result))
                message.Message = result;
        }
        catch (Exception exception)
        {
            if (!reportedError)
            {
                // Exception messages/stacks may contain chat data. Record only the failure type.
                log.Error("Sidekick could not transform a message ({ErrorType}); original retained. Further errors are suppressed until reload.",
                    exception.GetType().Name);
                reportedError = true;
            }
        }
    }
}

