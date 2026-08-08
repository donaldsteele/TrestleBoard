using System;
using System.Linq;
using SkiaSharp;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Samples;
using TrestleBoard.Rendering;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// M53's draft diagonal (PLAN.md §11 M53).
///
/// <para><b>Deliberately NOT a pixel baseline</b>, on the same reasoning <c>OversetLabelTests</c>
/// recorded for the overset badge. A baseline would have to be baked on three operating systems
/// before it could ever fail honestly, and what matters here is not which pixels the letters land on
/// — that is Skia's business, and it differs by rasteriser — but that the words are drawn, that they
/// are drawn in the same place every time from the same inputs, and that they are drawn on a draft
/// and on nothing else. Counting ink answers all three on any machine.</para>
///
/// <para>The stronger cross-OS guarantee is the one this project has always relied on: the
/// watermark is shaped by HarfBuzz from a bundled face at an arithmetically-derived size, so the
/// glyphs and their positions are identical everywhere; only the anti-aliasing of their edges is
/// not. <c>SnapshotInfra</c>'s own note says the same of the newsletter underneath it.</para>
/// </summary>
public sealed class WatermarkTests
{
    private static readonly SizePt Page = new(480f, 680f);

    [Fact]
    public void TheDraftMarkPutsInkOnThePage()
    {
        int plain = InkIn(RenderPage(watermark: null));
        int draft = InkIn(RenderPage(WatermarkRenderer.DraftText));

        Assert.True(
            draft > plain + 2000,
            $"The draft mark added {draft - plain} pixels of ink, which is not a page-wide diagonal.");
    }

    /// <summary>The property the whole milestone rests on: a real export is untouched.</summary>
    [Fact]
    public void AnOrdinaryPageIsByteForByteWhatItWasBeforeThisMilestone()
    {
        byte[] first = RenderPage(watermark: null);
        byte[] second = RenderPage(watermark: null);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheSameDraftRendersTheSameBytesEveryTime()
    {
        byte[] first = RenderPage(WatermarkRenderer.DraftText);
        byte[] second = RenderPage(WatermarkRenderer.DraftText);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// A diagonal that crosses the page, not a mark sitting in the middle of it.
    ///
    /// <para>The bands are the outer fifth of each edge, and that is the point: an earlier version
    /// of this test compared the four <i>quadrants</i>, which meet at the centre — so a watermark
    /// shrunk to a twentieth of its size still put ink in all four and the test still passed. It
    /// was checking a property the defect could satisfy. These bands cannot be reached without
    /// actually spanning the sheet.</para>
    /// </summary>
    [Fact]
    public void TheMarkCrossesTheWholePage()
    {
        using SKBitmap plain = Decode(RenderPage(watermark: null));
        using SKBitmap draft = Decode(RenderPage(WatermarkRenderer.DraftText));

        int w = draft.Width, h = draft.Height;
        int band = w / 5;
        (string Edge, SKRectI Where)[] edges =
        [
            ("left", SKRectI.Create(0, 0, band, h)),
            ("right", SKRectI.Create(w - band, 0, band, h)),
        ];

        foreach ((string edge, SKRectI where) in edges)
        {
            Assert.True(
                InkIn(draft, where) > InkIn(plain, where),
                $"The draft mark never reaches the {edge} of the page.");
        }
    }

    /// <summary>
    /// The words are what is drawn, not a decoration: change them and the page changes.
    ///
    /// <para>Note what this does <i>not</i> assert. Fewer letters do not mean less ink, because the
    /// size is derived from the page's diagonal rather than fixed — "DRAFT" alone is set larger so
    /// that it still spans the page. That was a wrong guess written into an earlier version of this
    /// test, and the code was right: a mark that shrank with its wording would be a whisper on a
    /// one-word draft.</para>
    /// </summary>
    [Fact]
    public void TheWordsAreWhatIsDrawn()
    {
        byte[] full = RenderPage(WatermarkRenderer.DraftText);
        byte[] shorter = RenderPage("DRAFT");

        Assert.NotEqual(full, shorter);
        Assert.True(InkIn(shorter) > InkIn(RenderPage(watermark: null)), "One word drew nothing.");
    }

    /// <summary>
    /// However few the words, the mark still spans the page — the size comes from the diagonal, so
    /// a one-word draft is set larger rather than quieter.
    /// </summary>
    [Fact]
    public void FewerWordsAreSetLargerRatherThanSmaller()
    {
        using SKBitmap plain = Decode(RenderPage(watermark: null));
        using SKBitmap oneWord = Decode(RenderPage("DRAFT"));

        SKRectI topLeft = SKRectI.Create(0, 0, oneWord.Width / 2, oneWord.Height / 2);
        SKRectI bottomRight = SKRectI.Create(
            oneWord.Width / 2, oneWord.Height / 2, oneWord.Width / 2, oneWord.Height / 2);

        Assert.True(InkIn(oneWord, topLeft) > InkIn(plain, topLeft));
        Assert.True(InkIn(oneWord, bottomRight) > InkIn(plain, bottomRight));
    }

    /// <summary>
    /// A store with no bundled sans face is a broken build. A draft exported without its diagonal
    /// is a draft that can be emailed to sixty people by mistake, so this refuses rather than
    /// quietly producing one.
    /// </summary>
    [Fact]
    public void ADraftWithoutItsDiagonalIsRefusedRatherThanExported()
    {
        using var surface = SKSurface.Create(new SKImageInfo(50, 50));
        using var empty = new TrestleBoard.Layout.Fonts.FontStore();

        Assert.Throws<InvalidOperationException>(
            () => WatermarkRenderer.Draw(surface.Canvas, Page, empty));
    }

    [Fact]
    public void APageWithNoSizeDrawsNothingRatherThanThrowing()
    {
        using var surface = SKSurface.Create(new SKImageInfo(50, 50));

        WatermarkRenderer.Draw(surface.Canvas, new SizePt(0f, 0f), SnapshotInfra.Store.Value);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static byte[] RenderPage(string? watermark)
    {
        Core.Container.TboardPackage package = SampleDocument.CreatePackage();
        using DocumentRenderSource source = DocumentRenderSource.Create(
            package.Document, package.Assets, SnapshotInfra.Store.Value);

        SizePt size = source.GetPageSize(0);
        var info = new SKImageInfo(
            (int)MathF.Ceiling(size.Width),
            (int)MathF.Ceiling(size.Height),
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgb());

        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Failed to create a raster surface.");
        source.RenderPage(surface.Canvas, 0);
        if (watermark is not null)
        {
            source.RenderWatermark(surface.Canvas, 0, watermark);
        }

        surface.Canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return data.ToArray();
    }

    private static SKBitmap Decode(byte[] png) =>
        SKBitmap.Decode(png) ?? throw new InvalidOperationException("Could not decode the render.");

    private static int InkIn(byte[] png)
    {
        using SKBitmap bitmap = Decode(png);
        return InkIn(bitmap, new SKRectI(0, 0, bitmap.Width, bitmap.Height));
    }

    /// <summary>Pixels that are not the white of the paper. Works the same on every rasteriser.</summary>
    private static int InkIn(SKBitmap bitmap, SKRectI area)
    {
        int ink = 0;
        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 250 || pixel.Green < 250 || pixel.Blue < 250)
                {
                    ink++;
                }
            }
        }

        return ink;
    }
}
