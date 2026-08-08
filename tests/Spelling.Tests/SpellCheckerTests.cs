using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Spelling.Tests;

/// <summary>
/// M52's checker, its personal dictionary and its walk over a newsletter (PLAN.md §11 M52).
///
/// <para><b>§0 rule 7.</b> Every name below is fictional. The personal dictionary this milestone
/// introduces will hold real members' surnames on a real machine; none may ever reach a fixture.</para>
/// </summary>
public sealed class SpellCheckerTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "TrestleBoard-spelling-tests",
        Guid.NewGuid().ToString("N"));

    private SpellChecker NewChecker(out PersonalDictionary personal)
    {
        personal = new PersonalDictionary(Path.Combine(_folder, "personal-dictionary.txt"));
        return new SpellChecker(personal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    // ---- the dictionary that ships ------------------------------------------------------------

    [Fact]
    public void TheDictionaryIsInsideTheAssemblyRatherThanOnTheMachine()
    {
        string[] resources = typeof(BundledDictionary).Assembly.GetManifestResourceNames();

        Assert.Contains("TrestleBoard.Spelling.Dictionaries.en_US.dic", resources);
        Assert.Contains("TrestleBoard.Spelling.Dictionaries.en_US.aff", resources);
        Assert.Contains("TrestleBoard.Spelling.Dictionaries.README_en_US.txt", resources);
    }

    /// <summary>
    /// The SCOWL word lists may be redistributed only if their copyright notice goes with them, and
    /// the affix file adds Kuenning's BSD terms and WordNet's on top. Same obligation as the fonts'
    /// OFL, same answer.
    /// </summary>
    [Fact]
    public void TheDictionarysLicenceTravelsWithIt()
    {
        string licence = BundledDictionary.ReadLicenceText();

        Assert.Contains("Copyright 2000-2018 by Kevin Atkinson", licence, StringComparison.Ordinal);
        Assert.Contains("WordNet", licence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("brother")]
    [InlineData("newsletter")]
    [InlineData("secretary")]
    [InlineData("refreshment")]
    public void OrdinaryEnglishIsLeftAlone(string word) =>
        Assert.True(NewChecker(out _).IsSpelledRight(word));

    [Theory]
    [InlineData("chruch")]
    [InlineData("teh")]
    [InlineData("recieve")]
    public void AMisspellingIsCaught(string word) =>
        Assert.False(NewChecker(out _).IsSpelledRight(word));

    // ---- what the checker refuses to have an opinion about -------------------------------------

    /// <summary>
    /// The wall-of-red problem. Everything here is correct and none of it is in a dictionary; a
    /// checker that asked about all of it would be turned off within one issue.
    /// </summary>
    [Theory]
    [InlineData("2026")]
    [InlineData("1st")]
    [InlineData("555-0100")]
    [InlineData("F&AM")]
    [InlineData("OES")]
    [InlineData("PM")]
    [InlineData("a")]
    public void ThingsThatAreNotSpellingAreNotAskedAbout(string word)
    {
        Assert.False(SpellChecker.IsWorthChecking(word));
        Assert.True(NewChecker(out _).IsSpelledRight(word));
    }

    // ---- the personal dictionary ---------------------------------------------------------------

    [Fact]
    public void TheCraftsOwnVocabularyIsKnownBeforeTheFirstIssue()
    {
        PersonalDictionary personal = new(Path.Combine(_folder, "personal-dictionary.txt"));
        personal.SeedIfEmpty();
        var checker = new SpellChecker(personal);

        Assert.True(checker.IsSpelledRight("Worshipful"));
        Assert.True(checker.IsSpelledRight("Tyler"));
        Assert.True(checker.IsSpelledRight("Fellowcraft"));
        Assert.True(checker.IsSpelledRight("trestleboard"));
    }

    /// <summary>
    /// The seed keeps only what the bundled dictionary actually rejects, so the file is a record of
    /// what we had to add rather than a list of what we guessed we had to add. SCOWL turns out to
    /// know more of the craft's vocabulary than expected — "Worshipful", "Senior" and "Grand" are
    /// all ordinary English — and the file is smaller for it.
    /// </summary>
    [Fact]
    public void TheSeedKeepsOnlyWhatTheDictionaryDoesNotAlreadyKnow()
    {
        PersonalDictionary personal = new(Path.Combine(_folder, "personal-dictionary.txt"));
        personal.SeedIfEmpty();

        Assert.NotEmpty(personal.Words);
        Assert.All(personal.Words, w => Assert.False(
            BundledDictionary.WordList.Check(w, TestContext.Current.CancellationToken),
            $"'{w}' is ordinary English; seeding it makes the file a list of guesses."));
        Assert.DoesNotContain("Senior", personal.Words);
        Assert.Contains("Fellowcraft", personal.Words);
    }

    [Fact]
    public void ItsANameNeverAskAgainSurvivesClosingTheApp()
    {
        string path = Path.Combine(_folder, "personal-dictionary.txt");
        new SpellChecker(new PersonalDictionary(path)).NeverAskAgain("Fauntleroy");

        var afterRestart = new SpellChecker(new PersonalDictionary(path));

        Assert.True(afterRestart.IsSpelledRight("Fauntleroy"));
    }

    /// <summary>
    /// A dictionary that cannot be written still works for this sitting, and says that it could
    /// not be written. Accepting "never ask again" and then asking again next month, with no word
    /// said, is the kind of small betrayal that makes somebody stop trusting a program.
    /// </summary>
    [Fact]
    public void APersonalDictionaryThatCannotBeWrittenSaysSoRatherThanPretending()
    {
        // A directory where a file should be: every read and write throws, and none of it escapes.
        string path = Path.Combine(_folder, "not-a-file");
        Directory.CreateDirectory(path);
        var personal = new PersonalDictionary(path);

        Assert.True(personal.Add("Fauntleroy"));

        Assert.True(personal.CouldNotBeSaved);
        Assert.True(personal.Contains("Fauntleroy"));
        Assert.True(new SpellChecker(personal).IsSpelledRight("brother"));
    }

    // ---- suggestions ----------------------------------------------------------------------------

    [Fact]
    public void ASuggestionIsOfferedForAnOrdinaryTypo() =>
        Assert.Contains("church", NewChecker(out _).Suggest("chruch"), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The mistake that stings: a brother's surname misspelled by one letter. The general
    /// dictionary can never fix it, because it has never heard the name.
    /// </summary>
    [Fact]
    public void ANameOneKeystrokeAwayIsOfferedBeforeAnythingTheDictionaryKnows()
    {
        SpellChecker checker = NewChecker(out PersonalDictionary personal);
        personal.Add("Fauntleroy");

        IReadOnlyList<string> offered = checker.Suggest("Fauntleroi");

        Assert.NotEmpty(offered);
        Assert.Equal("Fauntleroy", offered[0]);
    }

    [Fact]
    public void NoMoreThanThreeSuggestionsAreEverOffered() =>
        Assert.True(NewChecker(out _).Suggest("recieve").Count <= 3);

    [Theory]
    [InlineData("Steele", "Steele", false)]
    [InlineData("Steele", "Steel", true)]
    [InlineData("Steele", "Steale", true)]
    [InlineData("Steele", "Steeles", true)]
    [InlineData("Steele", "Stevens", false)]
    public void OneKeystrokeMeansOneKeystroke(string a, string b, bool expected) =>
        Assert.Equal(expected, SpellChecker.IsOneKeystrokeAway(a, b));

    // ---- the walk over a newsletter -------------------------------------------------------------

    [Fact]
    public void TheWalkFindsTheWordAndWhereItIs()
    {
        Document document = OneStory("Brethren, the chruch supper is on Tuesday.");

        Misspelling found = Assert.Single(SpellCheckScan.Run(document, NewChecker(out _)));

        Assert.Equal("chruch", found.Word);
        Assert.Equal("story-1", found.StoryId);
        Assert.Equal(0, found.ParagraphIndex);
        Assert.Equal(14, found.Offset);
        Assert.Equal(6, found.Length);
    }

    /// <summary>"chruch" means nothing alone and everything in the sentence it sits in.</summary>
    [Fact]
    public void TheWalkBringsTheSentenceWithIt()
    {
        Document document = OneStory(
            "The lodge meets on Tuesday. Brethren, the chruch supper follows. All are welcome.");

        Misspelling found = Assert.Single(SpellCheckScan.Run(document, NewChecker(out _)));

        Assert.Equal("Brethren, the chruch supper follows.", found.Sentence);
    }

    [Fact]
    public void AnApostropheDoesNotSplitAWordInTwo()
    {
        Document document = OneStory("The brother's apron was presented.");

        Assert.Empty(SpellCheckScan.Run(document, NewChecker(out _)));
    }

    /// <summary>
    /// The officers table and the birthday list are filled in from the address book. Asking the
    /// user whether a surname there is spelled right is asking them to proofread the computer.
    /// </summary>
    [Fact]
    public void WhatTheAppFilledInIsNotProofread()
    {
        Document document = OneStory("All is well.");
        document.Pages[0].Blocks.Add(new WidgetBlock
        {
            Id = "w-1",
            WidgetType = "officersTable",
            DataVersion = 1,
            Data = System.Text.Json.JsonDocument.Parse("""{"rows":[{"name":"Fauntleroi"}]}""").RootElement,
        });

        Assert.Empty(SpellCheckScan.Run(document, NewChecker(out _)));
    }

    [Fact]
    public void NothingTheWalkDoesTouchesTheNewsletter()
    {
        Document document = OneStory("Brethren, the chruch supper is on Tuesday.");
        string before = System.Text.Json.JsonSerializer.Serialize(document);

        SpellCheckScan.Run(document, NewChecker(out _));

        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(document));
    }

    private static Document OneStory(string text)
    {
        var document = new Document();
        document.PageMasters.Add(new PageMaster { Id = "master" });
        document.Stories.Add(new Story
        {
            Id = "story-1",
            Paragraphs = [new StoryParagraph { ParagraphStyleRef = "body", Runs = [new StoryRun { Text = text }] }],
        });

        var page = new Page { Id = "page-1", MasterRef = "master" };
        page.Blocks.Add(new TextBlock { Id = "frame-1", StoryRef = "story-1" });
        document.Pages.Add(page);
        return document;
    }
}
