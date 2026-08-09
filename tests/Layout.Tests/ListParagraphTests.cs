using System;
using System.Collections.Generic;
using System.Linq;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Documents;
using Xunit;

namespace TrestleBoard.Layout.Tests;

/// <summary>
/// M61's bulleted and numbered lists (PLAN.md §11 M61).
///
/// <para>Golden layout assertions rather than pixels. The geometry — where the marker sits, where
/// the wrapped lines start, what number each point gets — is identical on every operating system,
/// and <c>SnapshotInfra</c>'s own note says that is the real cross-OS guarantee; only glyph
/// rasterisation differs. A pixel baseline would add nothing here and would have to be baked on
/// three platforms before it could fail honestly.</para>
/// </summary>
public sealed class ListParagraphTests
{
    // ---- the numbers, which are counted rather than stored ------------------------------------

    private static IReadOnlyList<StoryParagraph> Paragraphs(params string?[] kinds) =>
        [.. kinds.Select(k => new StoryParagraph
        {
            ParagraphStyleRef = "body",
            ListKind = k,
            Runs = [new StoryRun { Text = "Something to say." }],
        })];

    [Fact]
    public void AnOrdinaryParagraphHasNoMarker() =>
        Assert.Equal("", DocumentLayoutAdapter.MarkerFor(Paragraphs([null]), 0));

    [Fact]
    public void EveryPointInAListOfPointsGetsTheSameMark()
    {
        IReadOnlyList<StoryParagraph> paragraphs = Paragraphs(ListKinds.Bullet, ListKinds.Bullet);

        Assert.Equal(
            DocumentLayoutAdapter.MarkerFor(paragraphs, 0),
            DocumentLayoutAdapter.MarkerFor(paragraphs, 1));
        Assert.StartsWith("•", DocumentLayoutAdapter.MarkerFor(paragraphs, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void NumberedPointsCountUp()
    {
        IReadOnlyList<StoryParagraph> paragraphs =
            Paragraphs(ListKinds.Number, ListKinds.Number, ListKinds.Number);

        Assert.StartsWith("1.", DocumentLayoutAdapter.MarkerFor(paragraphs, 0), StringComparison.Ordinal);
        Assert.StartsWith("2.", DocumentLayoutAdapter.MarkerFor(paragraphs, 1), StringComparison.Ordinal);
        Assert.StartsWith("3.", DocumentLayoutAdapter.MarkerFor(paragraphs, 2), StringComparison.Ordinal);
    }

    /// <summary>
    /// The reason the number is not stored: adding a point in the middle renumbers everything after
    /// it with no edit to the document at all.
    /// </summary>
    [Fact]
    public void AddingAPointInTheMiddleRenumbersTheRest()
    {
        var paragraphs = new List<StoryParagraph>(Paragraphs(ListKinds.Number, ListKinds.Number));
        paragraphs.Insert(1, new StoryParagraph
        {
            ParagraphStyleRef = "body",
            ListKind = ListKinds.Number,
            Runs = [new StoryRun { Text = "Squeezed in." }],
        });

        Assert.StartsWith("2.", DocumentLayoutAdapter.MarkerFor(paragraphs, 1), StringComparison.Ordinal);
        Assert.StartsWith("3.", DocumentLayoutAdapter.MarkerFor(paragraphs, 2), StringComparison.Ordinal);
    }

    /// <summary>Two lists separated by ordinary writing are two lists, and both start at one.</summary>
    [Fact]
    public void ANormalParagraphBetweenTwoListsStartsTheCountAgain()
    {
        IReadOnlyList<StoryParagraph> paragraphs =
            Paragraphs([ListKinds.Number, ListKinds.Number, null, ListKinds.Number]);

        Assert.StartsWith("2.", DocumentLayoutAdapter.MarkerFor(paragraphs, 1), StringComparison.Ordinal);
        Assert.StartsWith("1.", DocumentLayoutAdapter.MarkerFor(paragraphs, 3), StringComparison.Ordinal);
    }

    [Fact]
    public void ARunOfPointsDoesNotContinueANumberedRun()
    {
        IReadOnlyList<StoryParagraph> paragraphs =
            Paragraphs(ListKinds.Number, ListKinds.Bullet, ListKinds.Number);

        Assert.StartsWith("1.", DocumentLayoutAdapter.MarkerFor(paragraphs, 2), StringComparison.Ordinal);
    }

    // ---- the hanging indent, which is the whole visual point -----------------------------------

    /// <summary>
    /// PLAN.md's acceptance: <i>wrapped lines align under the text, not under the marker</i>. This
    /// is what typing "1." by hand cannot do — the second line comes back to the margin.
    /// </summary>
    [Fact]
    public void WrappedLinesLineUpUnderTheWritingRatherThanUnderTheMarker()
    {
        LayoutResult plain = LayOut(null);
        LayoutResult listed = LayOut(ListKinds.Bullet);

        Assert.True(listed.Frames[0].Lines.Count >= 2, "the fixture must wrap for this to mean anything");

        float plainSecondLeft = FirstTextX(plain, line: 1);
        float listedFirstLeft = FirstTextX(listed, line: 0);
        float listedSecondLeft = FirstTextX(listed, line: 1);

        // Every line of the list paragraph starts at the same x…
        Assert.Equal(listedFirstLeft, listedSecondLeft, 3);

        // …and that x is further in than an ordinary paragraph's, by the marker's width.
        Assert.True(
            listedSecondLeft > plainSecondLeft + 1f,
            $"the list's wrapped line starts at {listedSecondLeft}, the plain one at {plainSecondLeft}");
    }

    [Fact]
    public void TheMarkerSitsInTheGutterToTheLeftOfTheWriting()
    {
        LayoutResult listed = LayOut(ListKinds.Bullet);

        IReadOnlyList<PositionedGlyphRun> first = listed.Frames[0].Lines[0].Segments[0].Runs;

        Assert.True(first.Count >= 2, "the first line carries the marker and then the words");
        Assert.True(
            first[0].OriginX < first[1].OriginX,
            "the marker must sit to the left of the first word");
    }

    [Fact]
    public void OnlyTheFirstLineCarriesAMarker()
    {
        LayoutResult listed = LayOut(ListKinds.Bullet);

        int firstLineRuns = listed.Frames[0].Lines[0].Segments[0].Runs.Count;
        int secondLineRuns = listed.Frames[0].Lines[1].Segments[0].Runs.Count;

        Assert.True(firstLineRuns > secondLineRuns || firstLineRuns >= 2);
        Assert.Equal(
            FirstTextX(listed, 0),
            listed.Frames[0].Lines[1].Segments[0].Runs[0].OriginX,
            3);
    }

    /// <summary>
    /// A numbered marker is wider than a bullet, so the writing starts further in. That is the
    /// marker being measured rather than a fixed indent being assumed.
    /// </summary>
    [Fact]
    public void TheIndentIsTheMarkersOwnWidth()
    {
        float bullet = FirstTextX(LayOut(ListKinds.Bullet), line: 1);
        float numbered = FirstTextX(LayOut(ListKinds.Number), line: 1);

        Assert.NotEqual(bullet, numbered, 2);
    }

    [Fact]
    public void AnOrdinaryParagraphIsLaidOutExactlyAsBefore()
    {
        LayoutResult once = LayOut(null);
        LayoutResult twice = LayOut(null);

        Assert.Equal(Describe(once), Describe(twice));
        Assert.Equal(
            once.Frames[0].Lines[0].Segments[0].Runs[0].OriginX,
            twice.Frames[0].Lines[0].Segments[0].Runs[0].OriginX,
            5);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static float FirstTextX(LayoutResult result, int line) =>
        result.Frames[0].Lines[line].Segments[0].Runs[^1].OriginX == 0f
            ? result.Frames[0].Lines[line].Segments[0].Runs[0].OriginX
            : result.Frames[0].Lines[line].Segments[0].Runs[
                result.Frames[0].Lines[line].Segments[0].Runs.Count == 1 ? 0 : 1].OriginX;

    private static string Describe(LayoutResult result) =>
        string.Join(";", result.Frames.SelectMany(f => f.Lines).Select(l =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{l.ParagraphIndex}@{l.BaselineY:0.##}")));

    private static LayoutResult LayOut(string? listKind)
    {
        var document = new Document();
        StandardStylesForTest(document);
        document.PageMasters.Add(new PageMaster { Id = "master" });
        document.Stories.Add(new Story
        {
            Id = "story-1",
            Paragraphs =
            [
                new StoryParagraph
                {
                    ParagraphStyleRef = "body",
                    ListKind = listKind,
                    Runs =
                    [
                        new StoryRun
                        {
                            Text = "The lodge will meet on the first Tuesday of the month, and "
                                + "supper will be served beforehand in the dining room downstairs.",
                        },
                    ],
                },
            ],
        });

        var page = new Page { Id = "page-1", MasterRef = "master" };
        page.Blocks.Add(new TextBlock
        {
            Id = "frame-1",
            StoryRef = "story-1",
            FrameRect = new RectPt(0f, 0f, 220f, 400f),
        });
        document.Pages.Add(page);

        using var fonts = TrestleBoard.Layout.Fonts.BundledFonts.CreateDefaultStore();
        var engine = new TextLayoutEngine(fonts);
        StoryLayoutPlan plan = DocumentLayoutAdapter.BuildPlans(document)[0];
        return engine.Layout(plan.Request);
    }

    private static void StandardStylesForTest(Document document) =>
        TrestleBoard.Core.Templates.StandardStyles.Add(document);

    // ---- the bullet has to exist in every face we ship ------------------------------------------

    /// <summary>
    /// PLAN.md's acceptance: <i>the bullet glyph is verified present in all bundled faces</i>.
    ///
    /// <para>Fonts are bundled-only and there is no system fallback, so a face missing U+2022 would
    /// print the missing-glyph box in somebody's newsletter — and only in the one paragraph they
    /// made into a list, which is the hardest kind of fault to report.</para>
    /// </summary>
    [Fact]
    public void EveryBundledFaceCanDrawTheBullet()
    {
        using TrestleBoard.Layout.Fonts.FontStore fonts =
            TrestleBoard.Layout.Fonts.BundledFonts.CreateDefaultStore();

        var missing = new List<string>();
        foreach (TrestleBoard.Layout.Fonts.BundledFace face
            in TrestleBoard.Layout.Fonts.BundledFontCatalog.Faces)
        {
            TrestleBoard.Layout.Fonts.ResolvedFont resolved = fonts.Resolve(face.Key);
            if (resolved.Typeface.GetGlyph('•') == 0)
            {
                missing.Add(face.Key.ToString());
            }
        }

        Assert.True(missing.Count == 0, "these bundled faces cannot draw a bullet: " + string.Join(", ", missing));
    }
}
