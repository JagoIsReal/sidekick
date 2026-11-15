using System.Globalization;
using Sidekick.Core;

namespace Sidekick.Tests;

public sealed class NameMatcherTests
{
    [Theory]
    [InlineData("Ann Example", "Hello, Ann!", true)]
    [InlineData("Ann Example", "EXAMPLE, over here.", true)]
    [InlineData("Ann Example", "Ann Example", true)]
    [InlineData("Ann Example", "Ann and Ann and Example", true)]
    [InlineData("Ann Example", "Annabelle", false)]
    [InlineData("Ann Example", "examples", false)]
    [InlineData("Ann Example", "Ann2", false)]
    [InlineData("Ann Example", "Ann_test", false)]
    [InlineData("Ann Example", "éAnn", false)]
    [InlineData("Ann Example", "漢Ann", false)]
    [InlineData("Ann Example", "𝒜Ann", false)]
    [InlineData("Ann Example", "Ann\u0301", false)]
    [InlineData("Ann Example", "'Ann'", true)]
    [InlineData("Ann Example", "‘Ann’", true)]
    [InlineData("Ann Example", "Ann-Marie", false)]
    [InlineData("Ann Example", "D'Ann", false)]
    [InlineData("Ann Example", "D’Ann", false)]
    [InlineData("Ann Example", "Ann’s", false)]
    [InlineData("G'raha Tia", "G'raha, hello!", true)]
    [InlineData("G'raha Tia", "Hey G!", false)]
    [InlineData("Jean-Luc Example", "Jean-Luc!", true)]
    [InlineData("Jean-Luc Example", "Jean!", false)]
    [InlineData("Élia Example", "E\u0301LIA", true)]
    [InlineData("Élia Example", "Elia", false)]
    [InlineData("Ann Example", "Nothing to see here", false)]
    [InlineData("Ann Example", "", false)]
    public void MatchesWholeNames(string character, string text, bool expected) =>
        Assert.Equal(expected, NameMatcher.IsMatch(text, CharacterIdentity.FromName(character)));

    [Fact]
    public void DoesNotMatchWithoutIdentity() => Assert.False(NameMatcher.IsMatch("Ann", null));

    [Fact]
    public void DoesNotGuessPartsOfUnusualNames()
    {
        Assert.Null(CharacterIdentity.FromName("   "));
        Assert.Null(CharacterIdentity.FromName("One Two Three"));
        var single = CharacterIdentity.FromName("  Ann  ");
        Assert.Equal("Ann", single?.DisplayName);
        Assert.True(NameMatcher.IsMatch("Ann!", single));
        Assert.False(NameMatcher.IsMatch("Anything", single));
    }

    [Fact]
    public void ComparisonDoesNotDependOnCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.True(NameMatcher.IsMatch("IRIS!", CharacterIdentity.FromName("Iris Example")));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}

