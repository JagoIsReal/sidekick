using System.Globalization;
using System.Text;

namespace Sidekick.Core;

// Both arguments are NFC-normalized by the caller. Used for character names and literal keywords.
internal static class WholeWordMatcher
{
    internal static bool Contains(string text, string term)
    {
        if (term.Length == 0)
            return false;

        var offset = 0;
        while (offset <= text.Length - term.Length)
        {
            var start = text.IndexOf(term, offset, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return false;

            var end = start + term.Length;
            if (!ContinuesWord(text, start, before: true) && !ContinuesWord(text, end, before: false))
                return true;

            offset = start + 1;
        }

        return false;
    }

    private static bool ContinuesWord(string text, int boundary, bool before)
    {
        var index = before ? PreviousRuneIndex(text, boundary) : boundary;
        if (index < 0 || index >= text.Length)
            return false;

        var rune = Rune.GetRuneAt(text, index);
        if (IsWordRune(rune))
            return true;

        if (!IsNameJoiner(rune))
            return false;

        var neighbor = before ? PreviousRuneIndex(text, index) : index + rune.Utf16SequenceLength;
        return neighbor >= 0 && neighbor < text.Length && IsWordRune(Rune.GetRuneAt(text, neighbor));
    }

    private static int PreviousRuneIndex(string text, int boundary)
    {
        var index = boundary - 1;
        if (index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]))
            index--;
        return index;
    }

    private static bool IsWordRune(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
        UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or
        UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or
        UnicodeCategory.ConnectorPunctuation;

    private static bool IsNameJoiner(Rune rune) => rune.Value is '\'' or '\u2019' or '\u02BC' or
        '-' or '\u2010' or '\u2011';
}
