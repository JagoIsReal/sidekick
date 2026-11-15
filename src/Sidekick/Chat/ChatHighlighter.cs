using Dalamud.Game.Text.SeStringHandling;
using Sidekick.Core;
using System.Text;

namespace Sidekick.Chat;

public sealed class ChatHighlighter(Configuration configuration)
{
    private CharacterIdentity? keywordIdentity;
    private long keywordRevision = -1;
    private string[] activeKeywords = [];

    internal long RulesRevision => configuration.Keywords.Revision;

    public SeString Transform(SeString message, CharacterIdentity? identity)
    {
        return ShouldHighlight(message, identity)
            ? SeStringHighlighter.Apply(message, RgbColor.FromRgb(configuration.HighlightColorRgb))
            : message;
    }

    public bool ShouldHighlight(SeString message, CharacterIdentity? identity)
    {
        if (!configuration.Enabled || identity is null || message.Payloads.Count == 0)
            return false;

        if (!ReferenceEquals(keywordIdentity, identity) || keywordRevision != RulesRevision)
        {
            activeKeywords = configuration.Keywords.ActiveTerms(identity).ToArray();
            keywordIdentity = identity;
            keywordRevision = RulesRevision;
        }

        var text = SeStringHighlighter.GetVisibleText(message).Normalize(NormalizationForm.FormC);
        return WholeWordMatcher.Contains(text, identity.FirstName) || WholeWordMatcher.Contains(text, identity.LastName) ||
            activeKeywords.Any(keyword => WholeWordMatcher.Contains(text, keyword));
    }
}

