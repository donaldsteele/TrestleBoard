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

    // ---- the office members are told to speak to (the owner's ruling of 2026-08-09) -------------

    /// <summary>
    /// docs/M54-spec.md §3, choice 2. The drafted paragraph named the Almoner; at Indian Land 414
    /// it is the Secretary, and nobody who never opens the settings should have to say so.
    /// </summary>
    [Fact]
    public void TheSicknessWordsSayTheSecretaryUnlessSomebodySaysOtherwise()
    {
        Phrase sickness = PhraseLibrary.Find("sickness-and-distress")!;

        string filled = sickness.Fill(new Dictionary<string, string> { ["{name}"] = "A. Placeholder" });

        Assert.Contains("speak to the Secretary, who is keeping", filled, StringComparison.Ordinal);
        Assert.DoesNotContain("Almoner", filled, StringComparison.Ordinal);
        Assert.Equal("Secretary", PhraseLibrary.DefaultOffice);
    }

    /// <summary>
    /// The office varies by lodge and can change year to year, so it is a blank the app answers
    /// from a setting rather than a word baked into the sentence.
    /// </summary>
    [Fact]
    public void ALodgeThatSaysChaplainGetsChaplainInTheWords()
    {
        Phrase sickness = PhraseLibrary.Find("sickness-and-distress")!;

        string filled = sickness.Fill(new Dictionary<string, string>
        {
            ["{name}"] = "A. Placeholder",
            ["{office}"] = "  Chaplain  ",
        });

        Assert.Contains("speak to the Chaplain, who is keeping", filled, StringComparison.Ordinal);
        Assert.DoesNotContain("Secretary", filled, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whoever supplies the office may supply nothing. "Please speak to the , who is keeping in
    /// touch" is not a sentence to print, and neither is a row of underscores where an office the
    /// app already knows should be — so a blank with a default falls back to it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyOfficeFallsBackToTheDefaultRatherThanLeavingAHole(string given)
    {
        Phrase sickness = PhraseLibrary.Find("sickness-and-distress")!;

        string filled = sickness.Fill(new Dictionary<string, string>
        {
            ["{name}"] = "A. Placeholder",
            ["{office}"] = given,
        });

        Assert.Contains("speak to the Secretary, who is keeping", filled, StringComparison.Ordinal);
        Assert.DoesNotContain("speak to the ,", filled, StringComparison.Ordinal);
        Assert.DoesNotContain("speak to the __________", filled, StringComparison.Ordinal);
    }

    /// <summary>
    /// A blank carrying a default is one the app already has an answer for — the wizard shows it
    /// filled in instead of asking. A blank without one is a question somebody must answer.
    /// </summary>
    [Fact]
    public void OnlyTheOfficeArrivesAlreadyAnswered()
    {
        foreach (Phrase phrase in PhraseLibrary.Bundled)
        {
            foreach (PhraseBlank blank in phrase.Blanks)
            {
                if (string.Equals(blank.Token, "{office}", StringComparison.Ordinal))
                {
                    Assert.Equal(PhraseLibrary.DefaultOffice, blank.Default);
                }
                else
                {
                    Assert.Null(blank.Default);
                }
            }
        }
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
