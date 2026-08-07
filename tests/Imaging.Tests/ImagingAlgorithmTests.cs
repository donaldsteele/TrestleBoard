using SkiaSharp;
using Xunit;

namespace TrestleBoard.Imaging.Tests;

/// <summary>M6 algorithm gates (docs/M6-spec.md §1–§4, PLAN.md §9).</summary>
public sealed class ImagingAlgorithmTests
{
    // ---- Decode / EXIF -----------------------------------------------------------------------

    [Fact]
    public void DecodeBakesExifRotationIntoThePixels()
    {
        // Orientation 6: the stored frame must be rotated 90° clockwise to look upright.
        using SKBitmap stored = TestImages.SplitHalves(40, 20, SKColors.Red, SKColors.Blue);
        byte[] jpeg = TestImages.EncodeJpegWithOrientation(stored, orientation: 6);

        using DecodedImage? decoded = ImageDecoder.Decode(jpeg);
        Assert.NotNull(decoded);
        Assert.Equal(SKEncodedOrigin.RightTop, decoded!.Origin);
        Assert.Equal(20, decoded.Width);
        Assert.Equal(40, decoded.Height);

        // The stored LEFT half (red) becomes the TOP half once upright.
        (byte r, _, byte b) = TestImages.At(decoded.Bitmap, 10, 5);
        Assert.True(r > 200 && b < 60, $"expected red at the top, got r={r} b={b}");
        (byte r2, _, byte b2) = TestImages.At(decoded.Bitmap, 10, 34);
        Assert.True(b2 > 200 && r2 < 60, $"expected blue at the bottom, got r={r2} b={b2}");
    }

    [Fact]
    public void DecodeHandlesMirroredOrientations()
    {
        // Orientation 2 is a horizontal flip — the case a rotate-only path gets wrong.
        using SKBitmap stored = TestImages.SplitHalves(40, 20, SKColors.Red, SKColors.Blue);
        byte[] jpeg = TestImages.EncodeJpegWithOrientation(stored, orientation: 2);

        using DecodedImage? decoded = ImageDecoder.Decode(jpeg);
        Assert.NotNull(decoded);
        Assert.Equal(40, decoded!.Width);
        (byte r, _, byte b) = TestImages.At(decoded.Bitmap, 5, 10);
        Assert.True(b > 200 && r < 60, $"expected blue on the left after the flip, got r={r} b={b}");
    }

    /// <summary>
    /// All eight EXIF origins, asserted against the standard's own definition. The fixture marks
    /// the stored top-left corner, so each expectation is just "where did that corner go".
    /// </summary>
    [Theory]
    [InlineData(1, 40, 20, 4, 4)]      // identity
    [InlineData(2, 40, 20, 36, 4)]     // mirror horizontally
    [InlineData(3, 40, 20, 36, 16)]    // rotate 180
    [InlineData(4, 40, 20, 4, 16)]     // mirror vertically
    [InlineData(5, 20, 40, 4, 4)]      // transpose
    [InlineData(6, 20, 40, 16, 4)]     // rotate 90 clockwise
    [InlineData(7, 20, 40, 16, 36)]    // transverse
    [InlineData(8, 20, 40, 4, 36)]     // rotate 90 counter-clockwise
    public void EveryExifOrientationLandsTheCornerWhereTheStandardSaysItShould(
        int orientation, int expectedWidth, int expectedHeight, int markerX, int markerY)
    {
        // A grey frame with a red square in its stored TOP-LEFT corner.
        using SKBitmap stored = TestImages.Solid(40, 20, new SKColor(0x80, 0x80, 0x80));
        using (var canvas = new SKCanvas(stored))
        using (var marker = new SKPaint { Color = SKColors.Red })
        {
            canvas.DrawRect(new SKRect(0, 0, 8, 8), marker);
        }

        byte[] png = TestImages.EncodePng(stored);
        using DecodedImage? plain = ImageDecoder.Decode(png);
        Assert.NotNull(plain);

        using SKBitmap upright = ImageDecoder.ApplyOrigin(plain!.Bitmap, (SKEncodedOrigin)orientation);
        Assert.Equal(expectedWidth, upright.Width);
        Assert.Equal(expectedHeight, upright.Height);

        (byte r, byte g, byte b) = TestImages.At(upright, markerX, markerY);
        Assert.True(
            r > 200 && g < 80 && b < 80,
            $"origin {orientation}: expected the marker at ({markerX},{markerY}), found r={r} g={g} b={b}");
    }

    [Fact]
    public void DecodeReturnsNullForRubbishInsteadOfThrowing()
    {
        Assert.Null(ImageDecoder.Decode([]));
        Assert.Null(ImageDecoder.Decode([0x00, 0x01, 0x02, 0x03, 0x04]));
    }

    // ---- Auto-levels -------------------------------------------------------------------------

    [Fact]
    public void AutoLevelsStretchesAWashedOutPhotoToTheFullRange()
    {
        using SKBitmap flat = TestImages.LowContrastRamp(120, 40, low: 80, high: 160);
        (byte beforeMin, byte beforeMax) = TestImages.LumaRange(flat);
        Assert.InRange(beforeMin, 75, 85);
        Assert.InRange(beforeMax, 155, 165);

        using SKBitmap stretched = AutoLevels.Apply(flat);
        (byte afterMin, byte afterMax) = TestImages.LumaRange(stretched);
        Assert.InRange(afterMin, 0, 12);
        Assert.InRange(afterMax, 243, 255);
    }

    /// <summary>
    /// Review §14.2: the histogram reads a premultiplied bitmap correctly.
    ///
    /// <para><c>ImageDecoder</c> decodes to <c>Premul</c>, so a half-transparent pixel arrives with
    /// its colour already scaled by its alpha — a mid grey at 50% alpha sits in memory as a dark
    /// grey. Counted raw, those pixels dragged the measured black point down, and the picture was
    /// then stretched against a level no pixel actually had.</para>
    ///
    /// <para>Two bitmaps here hold the SAME colours; only the storage differs. The levels must
    /// agree, because the picture does.</para>
    /// </summary>
    [Fact]
    public void PartlyTransparentPixelsAreMeasuredByTheirRealColourNotTheirPremultipliedOne()
    {
        const byte low = 80;
        const byte high = 160;

        using SKBitmap opaque = TestImages.LowContrastRamp(120, 40, low, high);
        AutoLevels.Levels expected = AutoLevels.Measure(opaque);

        // The same ramp at half alpha, stored premultiplied — which is what a decoded PNG with a
        // soft edge looks like in memory.
        using var premultiplied = new SKBitmap(new SKImageInfo(
            opaque.Width, opaque.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (int y = 0; y < opaque.Height; y++)
        {
            for (int x = 0; x < opaque.Width; x++)
            {
                // SetPixel takes an UNpremultiplied colour and premultiplies it for a Premul
                // bitmap, so the halving happens once, in Skia, exactly as it does on decode.
                SKColor c = opaque.GetPixel(x, y);
                premultiplied.SetPixel(x, y, c.WithAlpha(128));
            }
        }

        AutoLevels.Levels actual = AutoLevels.Measure(premultiplied);

        // Within a shade or two: premultiplication is lossy, so exact equality is not available.
        Assert.InRange(actual.RedLow, expected.RedLow - 3, expected.RedLow + 3);
        Assert.InRange(actual.RedHigh, expected.RedHigh - 3, expected.RedHigh + 3);

        // And the point of it: the black point is near the ramp's real floor, not near half of it.
        Assert.InRange(actual.RedLow, low - 8, low + 12);
    }

    [Fact]
    public void AutoLevelsClipsHalfAPercentAtEachEnd()
    {
        using SKBitmap ramp = TestImages.LowContrastRamp(200, 10, low: 40, high: 200);
        AutoLevels.Levels levels = AutoLevels.Measure(ramp);

        // The clip means the black/white points sit just inside the true extremes.
        Assert.InRange(levels.RedLow, 40, 48);
        Assert.InRange(levels.RedHigh, 192, 200);
    }

    [Fact]
    public void LuminanceModeKeepsAWarmPhotoWarmWhilePerChannelNeutralisesIt()
    {
        using SKBitmap warm = TestImages.WarmRamp(120, 40);
        (byte r0, byte g0, byte b0) = TestImages.At(warm, 60, 20);
        float warmthBefore = r0 - b0;

        using SKBitmap luminance = AutoLevels.Apply(warm, AutoLevelsMode.Luminance);
        (byte lr, byte lg, byte lb) = TestImages.At(luminance, 60, 20);
        Assert.True(lr > lg && lg > lb, "luminance mode must preserve the channel ordering");
        Assert.True(lr - lb > warmthBefore * 0.9f, $"warmth collapsed: before {warmthBefore}, after {lr - lb}");

        using SKBitmap perChannel = AutoLevels.Apply(warm, AutoLevelsMode.PerChannel);
        (byte pr, _, byte pb) = TestImages.At(perChannel, 60, 20);
        Assert.True(pr - pb < (lr - lb) / 2, "per-channel mode is expected to neutralise the tint");
    }

    [Fact]
    public void AutoLevelsLeavesAFlatImageAlone()
    {
        using SKBitmap flat = TestImages.Solid(20, 20, new SKColor(0x40, 0x40, 0x40));
        SKBitmap result = AutoLevels.Apply(flat);

        // Same instance back: nothing safe to stretch, so nothing is amplified.
        Assert.Same(flat, result);
    }

    // ---- Auto-crop ---------------------------------------------------------------------------

    [Fact]
    public void AutoCropProposesTheRequestedAspect()
    {
        using SKBitmap photo = TestImages.DetailPatch(400, 300, new SKRectI(40, 40, 140, 140));
        NormalizedRect crop = AutoCrop.Propose(photo, targetAspect: 1f);

        float croppedAspect = (crop.Width * photo.Width) / (crop.Height * photo.Height);
        Assert.InRange(croppedAspect, 0.94f, 1.06f);
        Assert.InRange(crop.X, 0f, 1f);
        Assert.True(crop.Right <= 1.001f && crop.Bottom <= 1.001f);
    }

    [Fact]
    public void AutoCropMovesTowardTheDetailedRegion()
    {
        using SKBitmap photo = TestImages.DetailPatch(400, 200, new SKRectI(300, 40, 390, 160));
        NormalizedRect crop = AutoCrop.Propose(photo, targetAspect: 1f);

        float cropCenter = crop.X + (crop.Width / 2f);
        Assert.True(cropCenter > 0.55f, $"expected the crop to favour the busy right side, center was {cropCenter:F2}");
    }

    [Fact]
    public void AutoCropIsPulledTowardSkinTones()
    {
        // Two equally sized patches: stripes on the right, a skin-tone oval on the left. The
        // skin-tone bonus is what should decide it (PLAN §9 — lodge photos are mostly people).
        using SKBitmap photo = TestImages.SkinPatch(400, 200, new SKRect(30, 40, 150, 160));
        NormalizedRect crop = AutoCrop.Propose(photo, targetAspect: 1f);

        float cropCenter = crop.X + (crop.Width / 2f);
        Assert.True(cropCenter < 0.45f, $"expected the crop to favour the faces, center was {cropCenter:F2}");
    }

    [Fact]
    public void AutoCropIsDeterministic()
    {
        using SKBitmap photo = TestImages.DetailPatch(360, 240, new SKRectI(100, 60, 220, 180));
        Assert.Equal(AutoCrop.Propose(photo, 1.5f), AutoCrop.Propose(photo, 1.5f));
    }

    [Fact]
    public void AutoCropLeavesAnAlreadyCorrectAspectAlone()
    {
        using SKBitmap photo = TestImages.DetailPatch(400, 200, new SKRectI(40, 40, 140, 140));
        Assert.Equal(NormalizedRect.Full, AutoCrop.Propose(photo, targetAspect: 2f));
    }

    [Fact]
    public void SkinToneEnvelopeAcceptsSkinAndRejectsWallsAndCloth()
    {
        Assert.True(AutoCrop.IsSkinTone(0xD0, 0xA0, 0x80));
        Assert.True(AutoCrop.IsSkinTone(0x8B, 0x67, 0x4F));
        Assert.False(AutoCrop.IsSkinTone(0x70, 0x74, 0x78)); // grey wall
        Assert.False(AutoCrop.IsSkinTone(0x20, 0x40, 0x90)); // blue regalia
        Assert.False(AutoCrop.IsSkinTone(0xFF, 0x00, 0x00)); // saturated red
    }
}
