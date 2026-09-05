using SkiaSharp;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Samples;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// M102: the line under underlined writing — M86's last deliverable.
///
/// <para><b>No golden image, deliberately.</b> M86 budgeted a <c>text-decorations</c> snapshot
/// fixture baked on all three operating systems, and a maintainer's machine can bake only its own.
/// These assert the properties a golden image would have stood in for, and every one is independent
/// of how a particular rasteriser antialiases an edge — so they hold on all three.</para>
/// </summary>
public sealed class UnderlineRenderTests
{
    private const float Baseline = 100f;

    private static (TboardPackage Package, DocumentRenderSource Source) Sample()
    {
        TboardPackage package = SampleDocument.CreatePackage(DocumentSnapshotTests.TestPhotoPng());
        DocumentRenderSource source = DocumentRenderSource.Create(
            package.Document, package.Assets, SnapshotInfra.Store.Value);
        return (package, source);
    }

    // ---- where the line goes -------------------------------------------------------------------

    /// <summary>
    /// The line sits BELOW the baseline, not through the words.
    ///
    /// <para><b>This test exists in this shape because its first shape was useless.</b> It began by
    /// counting dark pixels per row of the rendered page, and it <b>passed against a deliberate
    /// strike-through</b> — the same line moved above the baseline instead of below it — because an
    /// underline sits close enough to the baseline that both land in rows with little ink. A test
    /// that cannot tell an underline from a strike-through is not a test of an underline. The
    /// geometry is checkable where the pixels were not, so the geometry was pulled out of the
    /// drawing code into somewhere a test could reach it.</para>
    /// </summary>
    [Theory]
    [InlineData(11f)]
    [InlineData(30f)]
    public void TheLineSitsBelowTheBaseline(float sizePt)
    {
        SKRect rect = PageRenderer.UnderlineRectFor(
            facePosition: null, faceThickness: null, sizePt,
            originX: 40f, baselineY: Baseline, advanceWidthPt: 120f);

        Assert.True(rect.Top > Baseline, $"top {rect.Top} is not below the baseline {Baseline}");
        Assert.True(rect.Height > 0f, "the line has no thickness");
        Assert.Equal(40f, rect.Left, 3);
        Assert.Equal(120f, rect.Width, 3);
    }

    /// <summary>
    /// A face that reports its underline position the other way round still gets an underline. The
    /// sign is taken rather than trusted, because the alternative is a strike-through nobody asked
    /// for — and the bundled fonts are not the only ones this will ever meet.
    /// </summary>
    [Fact]
    public void AFaceThatStoresThePositionUpsideDownStillGetsAnUnderline()
    {
        SKRect rect = PageRenderer.UnderlineRectFor(-2f, null, 11f, 0f, Baseline, 100f);

        Assert.True(rect.Top > Baseline);
    }

    /// <summary>
    /// The line comes from the FACE when the face has an opinion. A hand-picked offset would be
    /// wrong for every font except the one it was eyeballed against, and wrong at every size but one.
    /// </summary>
    [Fact]
    public void TheFacesOwnMetricsAreUsedWhenItHasThem()
    {
        SKRect a = PageRenderer.UnderlineRectFor(1f, 0.5f, 11f, 0f, Baseline, 100f);
        SKRect b = PageRenderer.UnderlineRectFor(4f, 2f, 11f, 0f, Baseline, 100f);

        Assert.Equal(Baseline + 1f, a.Top, 3);
        Assert.Equal(Baseline + 4f, b.Top, 3);
        Assert.Equal(0.5f, a.Height, 3);
        Assert.Equal(2f, b.Height, 3);
    }

    /// <summary>
    /// With no metrics at all the line scales with the type size, rather than being a constant that
    /// looks right at one size and wrong at every other.
    /// </summary>
    [Fact]
    public void WithNoMetricsTheLineScalesWithTheTypeSize()
    {
        SKRect small = PageRenderer.UnderlineRectFor(null, null, 11f, 0f, Baseline, 100f);
        SKRect large = PageRenderer.UnderlineRectFor(null, null, 33f, 0f, Baseline, 100f);

        Assert.True(large.Height > small.Height, "a 33pt underline is no thicker than an 11pt one");
        Assert.True(large.Top > small.Top, "a 33pt underline sits no lower than an 11pt one");
    }

    /// <summary>The line spans the run, so an underlined phrase is not one word long.</summary>
    [Fact]
    public void TheLineSpansTheWholeRun()
    {
        SKRect rect = PageRenderer.UnderlineRectFor(
            facePosition: null, faceThickness: null, sizePt: 11f,
            originX: 72f, baselineY: Baseline, advanceWidthPt: 216f);

        Assert.Equal(72f, rect.Left, 3);
        Assert.Equal(288f, rect.Right, 3);
    }

    // ---- what reaches the page -----------------------------------------------------------------

    /// <summary>Underlining changes the page, and taking it off puts it back byte-for-byte.</summary>
    [Fact]
    public void UnderliningDrawsSomethingAndTakingItOffPutsThePageBack()
    {
        (TboardPackage package, DocumentRenderSource source) = Sample();
        using (source)
        {
            byte[] plain = DocumentSnapshotTests.RenderPagePng(source, 0);

            CharacterStyleDef body = package.Document.StyleSheet.GetCharacterStyle("body");
            body.Underline = true;
            source.Invalidate(new ChangeScope(ChangeKind.Metadata));
            byte[] underlined = DocumentSnapshotTests.RenderPagePng(source, 0);

            Assert.NotEqual(plain, underlined);

            body.Underline = false;
            source.Invalidate(new ChangeScope(ChangeKind.Metadata));
            Assert.Equal(plain, DocumentSnapshotTests.RenderPagePng(source, 0));
        }
    }

    /// <summary>
    /// The line takes the writing's colour. An underline is part of the words rather than a mark on
    /// top of them, so a red underlined heading is red underneath too.
    /// </summary>
    [Fact]
    public void TheLineTakesTheColourOfTheWriting()
    {
        (TboardPackage package, DocumentRenderSource source) = Sample();
        using (source)
        {
            CharacterStyleDef body = package.Document.StyleSheet.GetCharacterStyle("body");
            body.Underline = true;
            source.Invalidate(new ChangeScope(ChangeKind.Metadata));
            byte[] black = DocumentSnapshotTests.RenderPagePng(source, 0);

            body.ColorArgb = 0xFF8C2A2A;
            source.Invalidate(new ChangeScope(ChangeKind.Metadata));
            Assert.NotEqual(black, DocumentSnapshotTests.RenderPagePng(source, 0));
        }
    }

    /// <summary>
    /// Nothing in the sample newsletter is underlined, which is why no committed baseline moved:
    /// the change is additive, and only a document that opts in can look different.
    /// </summary>
    [Fact]
    public void ANewsletterThatUnderlinesNothingOptsIntoNothing()
    {
        (TboardPackage package, DocumentRenderSource source) = Sample();
        using (source)
        {
            Assert.All(
                package.Document.StyleSheet.CharacterStyles,
                style => Assert.False(style.Underline));
        }
    }
}
