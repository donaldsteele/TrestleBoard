using SkiaSharp;

namespace TrestleBoard.Imaging;

/// <summary>
/// A decoded photo in UPRIGHT orientation: the EXIF rotation is already baked into the pixels
/// (docs/M6-spec.md §1), so crop rects, energy maps and histograms all work in the coordinates
/// the user actually sees.
/// </summary>
public sealed class DecodedImage(SKBitmap bitmap, SKEncodedOrigin origin, SKSizeI encodedSize) : IDisposable
{
    private bool _disposed;

    public SKBitmap Bitmap { get; } = bitmap;

    /// <summary>The origin found in the file, already applied. Kept for diagnostics/tests.</summary>
    public SKEncodedOrigin Origin { get; } = origin;

    /// <summary>Pixel size as stored in the file, BEFORE orientation was applied.</summary>
    public SKSizeI EncodedSize { get; } = encodedSize;

    public int Width => Bitmap.Width;

    public int Height => Bitmap.Height;

    public float Aspect => Height == 0 ? 1f : (float)Width / Height;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Bitmap.Dispose();
    }
}

/// <summary>Decodes encoded photo bytes, applying EXIF orientation up front.</summary>
public static class ImageDecoder
{
    /// <summary>
    /// Returns null for anything that will not decode — a corrupt or unsupported asset must fall
    /// back to the placeholder, never take the editor down.
    /// </summary>
    public static DecodedImage? Decode(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (encoded.Length == 0)
        {
            return null;
        }

        using var data = SKData.CreateCopy(encoded);
        using SKCodec? codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        SKImageInfo info = codec.Info with { ColorType = SKColorType.Rgba8888, AlphaType = SKAlphaType.Premul };
        var decoded = new SKBitmap(info);
        if (codec.GetPixels(info, decoded.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            decoded.Dispose();
            return null;
        }

        SKEncodedOrigin origin = codec.EncodedOrigin;
        var encodedSize = new SKSizeI(info.Width, info.Height);
        SKBitmap upright = ApplyOrigin(decoded, origin);
        if (!ReferenceEquals(upright, decoded))
        {
            decoded.Dispose();
        }

        return new DecodedImage(upright, origin, encodedSize);
    }

    /// <summary>
    /// Bakes one of the eight EXIF origins into the pixels. Mirrored origins are the ones people
    /// forget: a phone held "selfie" writes them and a naive rotate-only path flips the photo.
    /// </summary>
    public static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return source;
        }

        bool swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int width = swapsAxes ? source.Height : source.Width;
        int height = swapsAxes ? source.Width : source.Height;

        var rotated = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
        using (var canvas = new SKCanvas(rotated))
        {
            canvas.SetMatrix(OriginMatrix(origin, source.Width, source.Height));
            canvas.DrawBitmap(source, 0, 0);
        }

        return rotated;
    }

    /// <summary>Transform that maps encoded pixels to upright ones (w/h are the ENCODED size).</summary>
    public static SKMatrix OriginMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => SKMatrix.CreateScale(-1, 1).PostConcat(SKMatrix.CreateTranslation(w, 0)),
        SKEncodedOrigin.BottomRight => SKMatrix.CreateScale(-1, -1).PostConcat(SKMatrix.CreateTranslation(w, h)),
        SKEncodedOrigin.BottomLeft => SKMatrix.CreateScale(1, -1).PostConcat(SKMatrix.CreateTranslation(0, h)),
        // Axis-swapping origins: rotate, then fix up the translation into the new frame.
        SKEncodedOrigin.LeftTop => SKMatrix.CreateRotationDegrees(90).PostConcat(SKMatrix.CreateScale(-1, 1)),
        SKEncodedOrigin.RightTop => SKMatrix.CreateRotationDegrees(90).PostConcat(SKMatrix.CreateTranslation(h, 0)),
        SKEncodedOrigin.RightBottom => SKMatrix.CreateRotationDegrees(-90)
            .PostConcat(SKMatrix.CreateScale(-1, 1))
            .PostConcat(SKMatrix.CreateTranslation(h, w)),
        SKEncodedOrigin.LeftBottom => SKMatrix.CreateRotationDegrees(-90).PostConcat(SKMatrix.CreateTranslation(0, w)),
        _ => SKMatrix.Identity,
    };
}
