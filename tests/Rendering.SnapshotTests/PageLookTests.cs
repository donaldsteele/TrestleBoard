using SkiaSharp;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Samples;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// M79: a line under it, a box round it.
///
/// <para>"Put a line under that heading" and "put a box round the fish-fry notice" are the two
/// layout requests a committee actually makes, and the model has carried both answers since M1 with
/// no command to reach either.</para>
/// </summary>
public sealed class PageLookTests
{
    /// <summary>
    /// <b>The floor exists because a hairline is invisible where it matters.</b> Half a point
    /// disappears at the 77% zoom the app opens at and can vanish entirely through a photocopier,
    /// which is what a good share of this newsletter's readership is holding.
    /// </summary>
    [Theory]
    [InlineData(PageLooks.BorderStyleName)]
    [InlineData(PageLooks.BorderAndShadeStyleName)]
    public void NoBorderIsEverThinnerThanTheFloor(string styleName)
    {
        FrameStyleDef style = PageLooks.Define(styleName);

        Assert.True(
            style.StrokeWidthPt >= PageLooks.MinimumStrokeWidthPt,
            $"{styleName} draws at {style.StrokeWidthPt}pt, under the {PageLooks.MinimumStrokeWidthPt}pt floor");
    }

    /// <summary>
    /// The shading has to keep the writing on it readable. §6's floor is 4.5:1 and this is checked
    /// against the black the body style actually uses, not against an assumed black.
    /// </summary>
    [Fact]
    public void BlackWritingOnTheShadingClearsTheContrastFloor()
    {
        double ratio = ContrastRatio(0xFF000000, PageLooks.ShadeArgb);

        Assert.True(ratio >= 4.5, $"body text on the shading is {ratio:F2}:1, under 4.5:1");
    }

    /// <summary>
    /// The lodge navy is a heading colour, so it is held to the same floor against the paper.
    /// </summary>
    [Fact]
    public void TheLodgeNavyIsReadableOnPaper()
    {
        double ratio = ContrastRatio(PageLooks.LodgeInkArgb, 0xFFFFFFFF);

        Assert.True(ratio >= 4.5, $"the lodge navy on white is {ratio:F2}:1, under 4.5:1");
    }

    /// <summary>
    /// "Nothing at all" is stored as NO reference rather than as a style that draws nothing, which
    /// is what makes turning a border on and off again leave the file as it was.
    /// </summary>
    [Fact]
    public void NothingAtAllIsStoredAsNothing()
    {
        Assert.Null(PageLooks.StyleNameFor(border: false, shade: false));
        Assert.Equal(PageLooks.BorderStyleName, PageLooks.StyleNameFor(true, false));
        Assert.Equal(PageLooks.ShadeStyleName, PageLooks.StyleNameFor(false, true));
        Assert.Equal(PageLooks.BorderAndShadeStyleName, PageLooks.StyleNameFor(true, true));
    }

    /// <summary>
    /// <b>The border is drawn, and taking it off puts the page back exactly as it was.</b> The
    /// second half is what makes the feature safe for a committee to try on a newsletter they have
    /// already finished.
    /// </summary>
    [Fact]
    public void ABorderIsDrawnAndTakingItOffPutsThePageBack()
    {
        TboardPackage package = SampleDocument.CreatePackage(DocumentSnapshotTests.TestPhotoPng());
        Document document = package.Document;
        using DocumentRenderSource source =
            DocumentRenderSource.Create(document, package.Assets, SnapshotInfra.Store.Value);

        byte[] plain = DocumentSnapshotTests.RenderPagePng(source, 0);

        var border = new SetFrameLookCommand("block-body-1", border: true, shade: false);
        border.Apply(document);
        source.Invalidate(new ChangeScope(ChangeKind.BlockContent));
        byte[] bordered = DocumentSnapshotTests.RenderPagePng(source, 0);
        Assert.NotEqual(plain, bordered);

        border.Revert(document);
        source.Invalidate(new ChangeScope(ChangeKind.BlockContent));
        Assert.Equal(plain, DocumentSnapshotTests.RenderPagePng(source, 0));

        // And the style definition it added went with it, so a document does not accumulate
        // definitions nothing refers to.
        Assert.DoesNotContain(document.StyleSheet.FrameStyles, s => s.Name == PageLooks.BorderStyleName);
    }

    /// <summary>The shading and the border are independent, and both together is a fourth look.</summary>
    [Fact]
    public void ShadingAndABorderAreIndependent()
    {
        TboardPackage package = SampleDocument.CreatePackage(DocumentSnapshotTests.TestPhotoPng());
        Document document = package.Document;
        using DocumentRenderSource source =
            DocumentRenderSource.Create(document, package.Assets, SnapshotInfra.Store.Value);

        byte[] plain = DocumentSnapshotTests.RenderPagePng(source, 0);
        byte[] shaded = Look(document, source, border: false, shade: true);
        byte[] bordered = Look(document, source, border: true, shade: false);
        byte[] both = Look(document, source, border: true, shade: true);

        Assert.NotEqual(plain, shaded);
        Assert.NotEqual(plain, bordered);
        Assert.NotEqual(shaded, bordered);
        Assert.NotEqual(shaded, both);
        Assert.NotEqual(bordered, both);
    }

    /// <summary>
    /// A document written by hand, or by an older build, can carry any stroke width at all — so the
    /// floor is applied where the line is DRAWN as well as where it is chosen. A hairline that
    /// vanishes through a photocopier is the same failure whichever way it arrived.
    /// </summary>
    [Fact]
    public void AHairlineArrivingFromAnOlderFileIsStillDrawnAtTheFloor()
    {
        TboardPackage package = SampleDocument.CreatePackage(DocumentSnapshotTests.TestPhotoPng());
        Document document = package.Document;
        document.StyleSheet.FrameStyles.Add(new FrameStyleDef
        {
            Name = "hairline",
            StrokeArgb = 0xFF000000,
            StrokeWidthPt = 0.1f,
        });
        document.FindBlock("block-body-1").Block.FrameStyleRef = "hairline";

        using DocumentRenderSource source =
            DocumentRenderSource.Create(document, package.Assets, SnapshotInfra.Store.Value);
        byte[] drawn = DocumentSnapshotTests.RenderPagePng(source, 0);

        // The same style at the floor must produce the same pixels: the renderer raised the
        // hairline rather than drawing it.
        document.StyleSheet.FrameStyles.Find(s => s.Name == "hairline")!.StrokeWidthPt =
            PageLooks.MinimumStrokeWidthPt;
        source.Invalidate(new ChangeScope(ChangeKind.BlockContent));

        Assert.Equal(drawn, DocumentSnapshotTests.RenderPagePng(source, 0));
    }

    // ---- One page as a picture (M81) --------------------------------------------------------------

    /// <summary>
    /// <b>A JPEG, not a PNG, and not the full print resolution.</b> A page of text as a PNG is
    /// several megabytes and some services refuse it; the same page as a JPEG at 150 dots per inch
    /// is a few hundred kilobytes and reads identically on a telephone.
    /// </summary>
    [Fact]
    public void ASharedPageIsAJpegAtTheSharingSize()
    {
        TboardPackage package = SampleDocument.CreatePackage(DocumentSnapshotTests.TestPhotoPng());
        using DocumentRenderSource source =
            DocumentRenderSource.Create(package.Document, package.Assets, SnapshotInfra.Store.Value);

        byte[] jpeg = source.RenderPageToJpeg(0);

        Assert.Equal(
            TrestleBoard.Imaging.PictureFormatKind.Jpeg,
            TrestleBoard.Imaging.PictureFormat.Sniff(jpeg));

        using SKBitmap decoded = SKBitmap.Decode(jpeg);
        Core.Model.SizePt page = source.GetPageSize(0);
        float expected = DocumentRenderSource.SharingDpi / 72f;

        Assert.Equal((int)MathF.Ceiling(page.Width * expected), decoded.Width);
        Assert.Equal((int)MathF.Ceiling(page.Height * expected), decoded.Height);

        // Bigger than a thumbnail, smaller than the print raster — the point of the whole choice.
        Assert.True(decoded.Width > (int)page.Width, "a shared page must be sharper than the screen");
    }

    private static byte[] Look(Document document, DocumentRenderSource source, bool border, bool shade)
    {
        new SetFrameLookCommand("block-body-1", border, shade).Apply(document);
        source.Invalidate(new ChangeScope(ChangeKind.BlockContent));
        return DocumentSnapshotTests.RenderPagePng(source, 0);
    }

    /// <summary>
    /// WCAG relative luminance. The same arithmetic <c>ThemeCompositionTests</c> applies to the
    /// chrome, applied here to the page — which is the one surface in this app that never changes
    /// with the user's theme, because it is going to be printed.
    /// </summary>
    private static double ContrastRatio(uint foregroundArgb, uint backgroundArgb)
    {
        double a = Luminance(foregroundArgb);
        double b = Luminance(backgroundArgb);
        (double lighter, double darker) = a >= b ? (a, b) : (b, a);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(uint argb)
    {
        var colour = new SKColor(argb);
        return (0.2126 * Channel(colour.Red)) + (0.7152 * Channel(colour.Green)) + (0.0722 * Channel(colour.Blue));
    }

    private static double Channel(byte value)
    {
        double c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
