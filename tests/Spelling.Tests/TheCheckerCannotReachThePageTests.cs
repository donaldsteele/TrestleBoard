using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace TrestleBoard.Spelling.Tests;

/// <summary>
/// PLAN.md §11 M52's central acceptance: <i>the checker is provably absent from the
/// layout/render/export pipeline</i>.
///
/// <para>The obvious way to test "a squiggle never reaches the PDF" is to export one and look. That
/// test would pass today and would keep passing right up until the afternoon somebody added a
/// spelling colour to the renderer "just for the editor" — because by then the test would be
/// checking a document that happens not to have a misspelling in it, or a code path that happens
/// not to be taken.</para>
///
/// <para>So the guarantee is made structural instead, and this is what checks it: the projects that
/// produce a page do not reference the project that knows about spelling, and cannot, because the
/// reference is not there. A squiggle in the PDF would require somebody to add a line to a csproj
/// first, and that line fails this.</para>
/// </summary>
public sealed class TheCheckerCannotReachThePageTests
{
    /// <summary>The projects whose output is a page: on screen, in a PNG, or in the PDF.</summary>
    private static readonly string[] ThePipeline =
    [
        "TrestleBoard.Core",
        "TrestleBoard.Layout",
        "TrestleBoard.Rendering",
        "TrestleBoard.Export.Pdf",
        "TrestleBoard.Widgets",
        "TrestleBoard.Editing",
    ];

    [Fact]
    public void NothingThatDrawsAPageReferencesTheSpellChecker()
    {
        foreach (string project in ThePipeline)
        {
            string csproj = Path.Combine(RepositoryRoot(), "src", project, project + ".csproj");
            string xml = File.ReadAllText(csproj);

            Assert.False(
                xml.Contains("TrestleBoard.Spelling", StringComparison.Ordinal),
                $"{project} now references TrestleBoard.Spelling. M52's guarantee that a spelling "
                + "mark can never reach the PDF is this reference not existing — see docs/M52-spec.md.");
        }
    }

    /// <summary>
    /// The projects that put ink on a page. Narrower than <see cref="ThePipeline"/> on purpose:
    /// <c>Editing</c> declares every command in the app, so it names <c>view.showSpelling</c> and
    /// always will, and <c>Widgets</c> talks about the spelling of office names. Neither can reach
    /// the checker — the reference test above covers that — but neither can be scanned for the
    /// word either.
    /// </summary>
    private static readonly string[] TheDrawingHalf =
    [
        "TrestleBoard.Core",
        "TrestleBoard.Layout",
        "TrestleBoard.Rendering",
        "TrestleBoard.Export.Pdf",
    ];

    /// <summary>
    /// The same rule at the level of the words in the files, which catches the other way in: a
    /// renderer growing its own idea of spelling without referencing anything.
    /// </summary>
    [Fact]
    public void NothingThatDrawsAPageMentionsSpellingAtAll()
    {
        var offenders = new List<string>();
        foreach (string project in TheDrawingHalf)
        {
            string root = Path.Combine(RepositoryRoot(), "src", project);
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    // ReviewFindingKind.SpellingToCheck is the sanctioned exception: M51's checklist
                    // carries a station M52 fills in, and it is a name in an enum, not a checker.
                    if (lines[i].Contains("Spelling", StringComparison.OrdinalIgnoreCase)
                        && !lines[i].Contains("SpellingToCheck", StringComparison.Ordinal)
                        && !lines[i].Contains("M52", StringComparison.Ordinal))
                    {
                        offenders.Add($"{project}/{Path.GetFileName(file)}:{i + 1}");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These are in the page-drawing half of the app and mention spelling: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// And the reference that IS allowed: the shell. A spelling mark is chrome, chrome is App's
    /// business, and App is the one project that may know both halves.
    /// </summary>
    [Fact]
    public void TheShellIsTheOnlyPlaceTheTwoHalvesMeet()
    {
        string app = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "TrestleBoard.App", "TrestleBoard.App.csproj"));

        Assert.Contains("TrestleBoard.Spelling", app, StringComparison.Ordinal);
    }

    /// <summary>
    /// The checker's own project stays a leaf. If it ever grew a reference to Layout or Rendering,
    /// the arrow the two tests above forbid would exist in the other direction, and "provably
    /// absent" would quietly stop being true.
    /// </summary>
    [Fact]
    public void TheCheckerItselfStaysALeaf()
    {
        string csproj = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "TrestleBoard.Spelling", "TrestleBoard.Spelling.csproj"));

        string[] referenced = [.. csproj
            .Split('\n')
            .Where(l => l.Contains("ProjectReference", StringComparison.Ordinal))];

        Assert.All(referenced, line => Assert.Contains("TrestleBoard.Core", line, StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TrestleBoard.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
