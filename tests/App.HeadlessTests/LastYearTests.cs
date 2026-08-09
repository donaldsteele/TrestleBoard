using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using TrestleBoard.App.Integration;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Samples;
using TrestleBoard.Core.Templates;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M59: last year's issue, beside this one (PLAN.md §11 M59).
///
/// <para>The acceptance that matters is that the old issue cannot be damaged. It is met
/// structurally — nothing in <c>PastIssues</c> or <c>LastYearWindow</c> constructs a
/// <c>DocumentSession</c>, so there is no second undo stack, nothing for autosave to find, and no
/// command that could reach the old file — and the last two tests here hold that shape.</para>
/// </summary>
public sealed class LastYearTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "TrestleBoard-lastyear-tests", Guid.NewGuid().ToString("N"));

    public LastYearTests() => Directory.CreateDirectory(_folder);

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

    private string WriteIssue(int month, int year, string? article = null)
    {
        TboardPackage package = SampleIssue.CreatePackage();
        package.Document.Metadata.IssueMonth = month;
        package.Document.Metadata.IssueYear = year;
        if (article is not null)
        {
            package.Document.Stories[0].Paragraphs =
            [
                new Core.Model.StoryParagraph
                {
                    ParagraphStyleRef = "body",
                    Runs = [new Core.Model.StoryRun { Text = article }],
                },
            ];
        }

        string path = Path.Combine(_folder, $"{year}-{month:00}.tboard");
        TboardContainer.SaveToFile(package, path);
        return path;
    }

    // ---- finding it -------------------------------------------------------------------------

    [Fact]
    public void ItFindsTheSameMonthOfTheYearBefore()
    {
        WriteIssue(9, 2025, "The picnic was held at the lake.");
        WriteIssue(9, 2026);

        PastIssue found = PastIssues.Find(_folder, month: 9, year: 2026);

        Assert.True(found.Opened);
        Assert.Equal(2025, found.Package!.Document.Metadata.IssueYear);
        Assert.Contains(found.Articles, a => a.Text.Contains("picnic", StringComparison.Ordinal));
    }

    /// <summary>
    /// It must be *last* year's, not merely the same month.
    ///
    /// <para>The obvious version of this test — write September 2025 and September 2026, ask for
    /// 2025 — passes even with the year check removed, because the files sort oldest-first and the
    /// right answer is found by accident. This one names the current year's file so that it is
    /// examined first, so only a real year check can pass it.</para>
    /// </summary>
    [Fact]
    public void ThisYearsIssueIsNotMistakenForLastYears()
    {
        TboardPackage thisYear = SampleIssue.CreatePackage();
        thisYear.Document.Metadata.IssueMonth = 9;
        thisYear.Document.Metadata.IssueYear = 2026;
        TboardContainer.SaveToFile(thisYear, Path.Combine(_folder, "a-this-year.tboard"));

        TboardPackage lastYear = SampleIssue.CreatePackage();
        lastYear.Document.Metadata.IssueMonth = 9;
        lastYear.Document.Metadata.IssueYear = 2025;
        TboardContainer.SaveToFile(lastYear, Path.Combine(_folder, "z-last-year.tboard"));

        PastIssue found = PastIssues.Find(_folder, month: 9, year: 2026);

        Assert.True(found.Opened);
        Assert.Equal(2025, found.Package!.Document.Metadata.IssueYear);
    }

    [Fact]
    public void TheWrongMonthIsNotTheAnswer()
    {
        WriteIssue(8, 2025);

        PastIssue found = PastIssues.Find(_folder, month: 9, year: 2026);

        Assert.False(found.Opened);
        Assert.Equal(PastIssueProblem.NotFound, found.Problem);
        Assert.Contains("September 2025", found.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoFolderNamedYetItAsksRatherThanFailing()
    {
        PastIssue found = PastIssues.Find(null, month: 9, year: 2026);

        Assert.Equal(PastIssueProblem.NoFolderYet, found.Problem);
        Assert.Contains("where you keep", found.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderThatHasGoneSaysSoInsteadOfThrowing()
    {
        PastIssue found = PastIssues.Find(
            Path.Combine(_folder, "not-here"), month: 9, year: 2026);

        Assert.Equal(PastIssueProblem.NoFolderYet, found.Problem);
        Assert.Contains("not there any more", found.Message!, StringComparison.Ordinal);
    }

    /// <summary>
    /// PLAN.md's acceptance: a file too old to migrate fails with the M25-standard honest message
    /// rather than taking the app down.
    /// </summary>
    [Fact]
    public void AFileTooOldToReadSaysSoAndNamesIt()
    {
        string path = Path.Combine(_folder, "ancient.tboard");
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        {
            using Stream entry = zip.CreateEntry("manifest.json").Open();
            using var writer = new StreamWriter(entry);
            writer.Write("""{"formatName":"trestleboard","formatVersion":"99.0.0","minReaderVersion":"99.0.0"}""");
        }

        PastIssue found = PastIssues.Find(_folder, month: 9, year: 2026);

        Assert.False(found.Opened);
        Assert.Contains("ancient.tboard", found.Message!, StringComparison.Ordinal);
        Assert.Contains("cannot read", found.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SomethingThatIsNotANewsletterAtAllIsSteppedOver()
    {
        File.WriteAllText(Path.Combine(_folder, "rubbish.tboard"), "this is not a zip");
        WriteIssue(9, 2025, "The picnic was held at the lake.");

        PastIssue found = PastIssues.Find(_folder, month: 9, year: 2026);

        Assert.True(found.Opened);
    }

    // ---- what it offers to copy ---------------------------------------------------------------

    /// <summary>
    /// An empty frame from last September is not something anybody wants to copy across, and
    /// offering it would be the app suggesting the user paste a prompt into their newsletter.
    /// </summary>
    [Fact]
    public void AnArticleThatIsStillJustAPromptIsNotOffered()
    {
        TboardPackage package = SampleIssue.CreatePackage();
        foreach (Core.Model.Story story in package.Document.Stories)
        {
            story.Paragraphs =
            [
                new Core.Model.StoryParagraph
                {
                    ParagraphStyleRef = "body",
                    Runs = [new Core.Model.StoryRun { Text = PlaceholderPrompts.Article }],
                },
            ];
        }

        Assert.Empty(PastIssues.ArticlesIn(package.Document));
    }

    [Fact]
    public void EachArticleIsNamedByItsFirstFewWords()
    {
        TboardPackage package = SampleIssue.CreatePackage();
        package.Document.Stories[0].Paragraphs =
        [
            new Core.Model.StoryParagraph
            {
                ParagraphStyleRef = "body",
                Runs = [new Core.Model.StoryRun { Text = "The picnic was held at the lake, and a fine day it was." }],
            },
        ];

        PastArticle article = Assert.Single(
            PastIssues.ArticlesIn(package.Document),
            a => a.Text.StartsWith("The picnic", StringComparison.Ordinal));

        Assert.StartsWith("The picnic was held", article.Heading, StringComparison.Ordinal);
    }

    // ---- the read-only guarantee ----------------------------------------------------------------

    /// <summary>
    /// Opening last year's issue must leave this month's undo stack exactly as it was. If the old
    /// document were opened for editing, this is where it would show.
    /// </summary>
    [Fact]
    public async Task LookingAtLastYearDoesNotTouchThisMonthsUndoStack()
    {
        WriteIssue(9, 2025, "The picnic was held at the lake.");

        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenIssueSample();
            window.Measure(new Size(1280, 860));
            window.Arrange(new Rect(0, 0, 1280, 860));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            try
            {
                window.PackageForTest!.Document.Metadata.IssueMonth = 9;
                window.PackageForTest.Document.Metadata.IssueYear = 2026;
                window.OldIssuesFolderAnswerForTest = _folder;

                bool couldUndoBefore = window.SessionForTest!.CanUndo;
                await window.ShowLastYearAsync();

                Assert.NotNull(window.LastYearWindowForTest);
                Assert.Equal(couldUndoBefore, window.SessionForTest.CanUndo);
                Assert.False(window.HasUnsavedChangesForTest);

                window.LastYearWindowForTest!.Close();
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The structural half of the guarantee, stated as a test: the two types that handle last
    /// year's issue never mention a document session, so there is no second one to scope anything
    /// to. A future edit that introduced one would fail here before it could confuse autosave.
    /// </summary>
    [Fact]
    public void NothingThatOpensLastYearCanEverEditIt()
    {
        foreach (string file in new[]
        {
            Path.Combine(Root(), "src", "TrestleBoard.App", "Integration", "PastIssues.cs"),
            Path.Combine(Root(), "src", "TrestleBoard.App", "Dialogs", "LastYearWindow.cs"),
        })
        {
            string source = File.ReadAllText(file);
            Assert.DoesNotContain("new DocumentSession", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateEditable", source, StringComparison.Ordinal);
        }
    }

    private static string Root()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TrestleBoard.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
