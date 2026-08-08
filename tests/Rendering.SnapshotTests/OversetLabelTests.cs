using SkiaSharp;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Input;
using TrestleBoard.Rendering;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// M43, review §14.4: the overset marker says "does not fit" in words, not only as a red square.
///
/// <para>Deliberately NOT a snapshot test. A baseline would have to be baked on three operating
/// systems for a marker whose whole point is that it is words, and what matters here is not which
/// pixels the glyphs land on but that the words are drawn at all, to the left of the badge, where
/// nothing was drawn before. Counting ink answers that on any machine.</para>
/// </summary>
public sealed class OversetLabelTests
{
    private static readonly RectPt Frame = new(60f, 60f, 300f, 200f);

    /// <summary>How many pixels in a region are not the white the surface was cleared to.</summary>
    private static int InkIn(SKBitmap bitmap, SKRectI region)
    {
        int ink = 0;
        for (int y = Math.Max(0, region.Top); y < Math.Min(bitmap.Height, region.Bottom); y++)
        {
            for (int x = Math.Max(0, region.Left); x < Math.Min(bitmap.Width, region.Right); x++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.White)
                {
                    ink++;
                }
            }
        }

        return ink;
    }

    private static SKBitmap RenderBadge(SKTypeface? face)
    {
        var info = new SKImageInfo(400, 300, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("raster surface");
        surface.Canvas.DrawColor(SKColors.White);

        FrameOverlayRenderer.Draw(
            surface.Canvas,
            new FrameOverlay(
                Frame,
                ShowHandles: false,
                SnapGuides: [],
                OversetRects: [Frame],
                LinkedRects: [],
                LinkTargetRects: []),
            overlayScale: 1f,
            colours: null,
            labelTypeface: face);

        surface.Canvas.Flush();
        var bitmap = new SKBitmap(info);
        surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
        return bitmap;
    }

    /// <summary>
    /// The region the label occupies: left of the badge and BELOW the frame's own bottom edge, so
    /// the selection outline is not counted as ink. Empty before M43, which is what lets this test
    /// be strict without owning a per-OS baseline.
    /// </summary>
    private static SKRectI LabelRegion() => new(
        (int)Frame.Right - 130,
        (int)Frame.Bottom + 3,
        (int)Frame.Right - 16,
        (int)Frame.Bottom + 13);

    [Fact]
    public void TheOversetMarkerSaysDoesNotFitInWords()
    {
        using FontStore store = BundledFonts.CreateDefaultStore();
        SKTypeface face = store
            .Resolve(new FontKey(BundledFonts.BodyFamily, FontWeight.Regular, FontStyleSlant.Normal))
            .Typeface;

        using SKBitmap withWords = RenderBadge(face);
        using SKBitmap badgeOnly = RenderBadge(face: null);

        int wordsInk = InkIn(withWords, LabelRegion());
        int badgeOnlyInk = InkIn(badgeOnly, LabelRegion());

        // Nothing was ever drawn there before, and now there is a plate with words on it.
        Assert.Equal(0, badgeOnlyInk);
        // ~300 in practice. The plate is drawn in the handle colour, which is white on a white
        // sheet, so almost all of this ink is the glyphs themselves and the plate's red edge — and
        // over a photograph, which is where the marker actually matters, the plate is what keeps
        // them readable.
        Assert.True(wordsInk > 150, $"only {wordsInk} pixels of label were drawn");

        // The badge itself is untouched: this milestone added words beside it rather than making
        // the square bigger, which is what keeps every snapshot baseline where it was.
        var badge = new SKRectI(
            (int)Frame.Right - 12, (int)Frame.Bottom, (int)Frame.Right, (int)Frame.Bottom + 12);
        Assert.Equal(InkIn(badgeOnly, badge), InkIn(withWords, badge));
    }

    /// <summary>
    /// A caller with no font gets the badge and no words rather than an exception — the PDF export
    /// and the snapshot suite both draw overlays with no font in hand.
    /// </summary>
    [Fact]
    public void WithoutAFaceTheBadgeIsStillDrawn()
    {
        using SKBitmap badgeOnly = RenderBadge(face: null);

        var badge = new SKRectI(
            (int)Frame.Right - 12, (int)Frame.Bottom, (int)Frame.Right, (int)Frame.Bottom + 12);
        Assert.True(InkIn(badgeOnly, badge) > 100);
    }
}
