using System;
using System.Collections.Generic;
using System.Linq;
using TrestleBoard.Core.Phrases;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// M54's shelf of hard-moment paragraphs (PLAN.md §11 M54).
///
/// <para>Most of what matters about this milestone is tone, and no test can judge tone — that is
/// the owner's sign-off, which PLAN.md requires before it ships. What tests can hold is everything
/// around the tone: that no real person is named, that a blank left empty reads as a blank rather
/// than as a fault in the program, and that the text a committee sees is the text we wrote.</para>
/// </summary>
public sealed class PhraseLibraryTests
{
    [Fact]
    public void TheShelfCarriesTheFiveHardMomentsPlanNamed()
    {
        string[] ids = [.. PhraseLibrary.Bundled.Select(p => p.Id)];

        Assert.Contains("memorial", ids);
        Assert.Contains("sickness-and-distress", ids);
        Assert.Contains("get-well", ids);
        Assert.Contains("newly-raised", ids);
        Assert.Contains("thank-the-degree-team", ids);
    }

    /// <summary>
    /// §0 rule 2. A shipped paragraph naming a real brother would put a real person's name into
    /// every copy of the application — the worst shape this project's privacy rule can take.
    /// </summary>
    [Fact]
    public void NoShippedParagraphNamesAnybody()
    {
        foreach (Phrase phrase in PhraseLibrary.Bundled)
        {
            // Every place a person could be named is a blank, and the blanks are the only place.
            Assert.DoesNotContain("Brother John", phrase.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Smith", phrase.Text, StringComparison.OrdinalIgnoreCase);

            if (phrase.Text.Contains("Brother ", StringComparison.Ordinal))
            {
                Assert.Contains("Brother {name}", phrase.Text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void EveryBlankInTheTextIsAQuestionSomebodyIsAsked()
    {
        foreach (Phrase phrase in PhraseLibrary.Bundled)
        {
            foreach (PhraseBlank blank in phrase.Blanks)
            {
                Assert.Contains(blank.Token, phrase.Text, StringComparison.Ordinal);
                Assert.EndsWith("?", blank.Question, StringComparison.Ordinal);
                Assert.False(string.IsNullOrWhiteSpace(blank.Hint));
            }

            // And the other way round: no token in the text that nobody is ever asked about, which
            // would print as "{name}" in somebody's newsletter.
            Assert.DoesNotContain('{', phrase.Fill(AnswerEverything(phrase)));
        }
    }

    /// <summary>
    /// A memorial is often written before the date is settled. The wizard lets the blank through,
    /// so what prints has to be something a person can see and fill in — not <c>{date}</c>, which
    /// reads as the program having gone wrong.
    /// </summary>
    [Fact]
    public void ABlankLeftEmptyReadsAsABlankRatherThanAsAFault()
    {
        Phrase memorial = PhraseLibrary.Find("memorial")!;

        string filled = memorial.Fill(new Dictionary<string, string> { ["{name}"] = "A. Placeholder" });

        Assert.Contains("A. Placeholder", filled, StringComparison.Ordinal);
        Assert.Contains("__________", filled, StringComparison.Ordinal);
        Assert.DoesNotContain("{date}", filled, StringComparison.Ordinal);
    }

    [Fact]
    public void WhitespaceOnlyAnswersCountAsUnanswered()
    {
        Phrase getWell = PhraseLibrary.Find("get-well")!;

        Assert.Contains(
            "__________",
            getWell.Fill(new Dictionary<string, string> { ["{name}"] = "   " }),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnswersAreTrimmedBecauseNobodyMeantTheSpaces()
    {
        Phrase getWell = PhraseLibrary.Find("get-well")!;

        Assert.Contains(
            "Brother A. Placeholder for",
            getWell.Fill(new Dictionary<string, string> { ["{name}"] = "  A. Placeholder  " }),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryParagraphSaysWhenAPersonWouldReachForIt()
    {
        Assert.All(PhraseLibrary.Bundled, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Title));
            Assert.False(string.IsNullOrWhiteSpace(p.WhenToUse));
            Assert.EndsWith(".", p.WhenToUse, StringComparison.Ordinal);
            Assert.True(p.Text.Length > 60, $"'{p.Title}' is too short to be a starting point.");
        });
    }

    /// <summary>
    /// The vocabulary rule `ActionCatalogTests` applies to commands, applied to the shipped prose:
    /// a committee member reads these words, and typesetting jargon has no place in them.
    /// </summary>
    [Fact]
    public void NoShippedParagraphSpeaksLikeATypesetter()
    {
        string[] banned = ["placeholder", "text frame", "overset", "z-order", "widget", "string"];

        foreach (Phrase phrase in PhraseLibrary.Bundled)
        {
            foreach (string word in banned)
            {
                Assert.DoesNotContain(word, phrase.Text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(word, phrase.Title, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void AParagraphOfTheUsersOwnHasNoBlanksToFillIn()
    {
        Phrase mine = PhraseLibrary.Mine("mine-ours", "Our own wording", "The lodge will miss him.");

        Assert.True(mine.IsMine);
        Assert.Empty(mine.Blanks);
        Assert.Equal("The lodge will miss him.", mine.Fill(null));
    }

    private static Dictionary<string, string> AnswerEverything(Phrase phrase) =>
        phrase.Blanks.ToDictionary(b => b.Token, _ => "A. Placeholder");
}
