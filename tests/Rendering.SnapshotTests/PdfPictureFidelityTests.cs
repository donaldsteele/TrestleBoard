using System.IO.Compression;
using System.Text.RegularExpressions;
using SkiaSharp;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Samples;
using TrestleBoard.Export.Pdf;
using TrestleBoard.Widgets;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// What a photograph looks like once it is inside the PDF (M90).
///
/// <para><b>The defect these exist for.</b> <c>SKDocumentPdfMetadata</c> is a struct, and both
/// exporters built one with <c>new SKDocumentPdfMetadata { … }</c>. An object initializer leaves
/// every field it does not mention at zero — and <c>EncodingQuality</c> at zero means "encode every
/// picture as a JPEG at quality nought". Skia's own default is 101, which means "do not re-encode
/// at all", and that is what this exporter has always claimed to do.</para>
///
/// <para>A real newsletter's cover banner came out of it in coloured blocks and bands: 625 by 253
/// pixels in a 3 KB JPEG. It had been that way since M1 and no test noticed, because <b>text is
/// vector</b> — the parity check, the font check and every snapshot were looking at the part of the
/// page the bug could not touch, and the one tolerant image comparison box-downsamples 4× with a
/// channel threshold of 64, which is roughly "you may destroy a photograph as long as you leave the
/// letters alone".</para>
///
/// <para>These tests read the PDF itself. They need no poppler, so they run on all three operating
/// systems rather than on Linux CI alone — which is where the parity tests that missed this live.
/// </para>
/// </summary>
public sealed class PdfPictureFidelityTests
{
    private static readonly WidgetLayoutProvider Widgets = WidgetLayoutProvider.CreateDefault();

    /// <summary>Every image object in a PDF, as its dictionary plus its raw stream bytes.</summary>
    private sealed record EmbeddedImage(string Dictionary, byte[] Stream)
    {
        public int Width => Number("Width");

        public int Height => Number("Height");

        public bool IsJpeg => Dictionary.Contains("/DCTDecode", StringComparison.Ordinal);

        public double BytesPerPixel => Width * Height == 0 ? 0 : (double)Stream.Length / (Width * Height);

        private int Number(string key)
        {
            Match m = Regex.Match(Dictionary, @"/" + key + @"\s+(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        }
    }

    /// <summary>
    /// The picture in the exported newsletter is not re-encoded (M90).
    ///
    /// <para>At quality 101 Skia writes the picture losslessly, so a JPEG in this file means the
    /// quality was set to something — and the only value that had ever been set was the zero a
    /// struct initializer leaves behind.</para>
    /// </summary>
    [Fact]
    public void ThePhotographIsNotReEncodedOnItsWayIntoThePdf()
    {
        List<EmbeddedImage> images = ImagesInExportedIssue();

        // Not vacuous: a run that embedded no pictures at all would otherwise pass this silently.
        Assert.NotEmpty(images);

        EmbeddedImage[] jpegs = [.. images.Where(i => i.IsJpeg)];
        Assert.True(
            jpegs.Length == 0,
            "the exported newsletter re-encoded "
            + string.Join(", ", jpegs.Select(j => $"a {j.Width}x{j.Height} picture as JPEG "
                + $"({j.Stream.Length} bytes, {j.BytesPerPixel:F3} bytes per pixel)"))
            + " — check SKDocumentPdfMetadata.EncodingQuality, which is 0 unless it is set");
    }

    // A third test was written here and deleted: "the picture carries at least a quarter of a byte
    // per pixel". It failed the fixed code as loudly as the broken code, because this fixture is
    // flat blocks of colour and Skia's LOSSLESS encoding of it is 0.03 bytes per pixel — nine times
    // smaller than the quality-zero JPEG it was meant to catch. A size floor measures how
    // compressible a picture is, not whether it survived; the two tests kept here measure what was
    // actually asked, and both fail against the bug.

    /// <summary>
    /// The picture that comes back out of the PDF still looks like the one that went in.
    ///
    /// <para>The two tests above describe the file; this one looks at the pixels. The embedded
    /// image is decoded, scaled to the source photo's size, and compared channel by channel. A
    /// quality-zero JPEG of this fixture is off by more than 20 levels on average; a faithful
    /// encoding is inside 8, and a lossless one inside 3 (the remaining difference is the
    /// downsampling the pipeline does on purpose at 300 dpi).</para>
    /// </summary>
    [Fact]
    public void TheDecodedPictureStillResemblesTheOneThatWasPutOnThePage()
    {
        EmbeddedImage biggest = ImagesInExportedIssue().MaxBy(i => i.Width * i.Height)!;

        using SKBitmap embedded = Decode(biggest)
            ?? throw new InvalidOperationException("the embedded picture could not be decoded");
        using SKBitmap original = SKBitmap.Decode(PhotoJpeg())
            ?? throw new InvalidOperationException("the fixture photo could not be decoded");

        double difference = MeanChannelDifference(original, embedded);

        Assert.True(
            difference <= 8,
            $"the picture in the PDF is {difference:F1} levels per channel away from the one on the "
            + "page, which is not the same photograph");
    }

    private static List<EmbeddedImage> ImagesInExportedIssue()
    {
        TboardPackage package = SampleIssue.CreatePackage(PhotoJpeg());
        using DocumentRenderSource source = DocumentRenderSource.Create(
            package.Document, package.Assets, SnapshotInfra.Store.Value, options: null, widgets: Widgets);

        using var buffer = new MemoryStream();
        DocumentPdfExporter.Export(buffer, source, new PdfMetadata("Fidelity", "A. Placeholder", "", "", ""));
        return ImagesIn(buffer.ToArray());
    }

    /// <summary>
    /// Pulls every image object out of a PDF by hand. Small and unsubtle on purpose: a PDF library
    /// here would be a second implementation of the thing under test, and this only has to cope
    /// with files Skia wrote.
    /// </summary>
    private static List<EmbeddedImage> ImagesIn(byte[] pdf)
    {
        string text = System.Text.Encoding.Latin1.GetString(pdf);
        var images = new List<EmbeddedImage>();

        foreach (Match match in Regex.Matches(text, @"/Subtype\s*/Image"))
        {
            int open = text.LastIndexOf("<<", match.Index, StringComparison.Ordinal);
            int streamWord = text.IndexOf("stream", match.Index, StringComparison.Ordinal);
            if (open < 0 || streamWord < 0)
            {
                continue;
            }

            string dictionary = text[open..streamWord];
            Match length = Regex.Match(dictionary, @"/Length\s+(\d+)");
            if (!length.Success)
            {
                continue;
            }

            // "stream" is followed by CRLF or LF, then exactly /Length bytes.
            int start = streamWord + "stream".Length;
            while (start < pdf.Length && (pdf[start] == '\r' || pdf[start] == '\n'))
            {
                start++;
            }

            int count = int.Parse(length.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (start + count > pdf.Length)
            {
                continue;
            }

            images.Add(new EmbeddedImage(dictionary, pdf[start..(start + count)]));
        }

        return images;
    }

    /// <summary>A JPEG decodes directly; a Flate stream is raw rows and has to be rebuilt.</summary>
    private static SKBitmap? Decode(EmbeddedImage image)
    {
        if (image.IsJpeg)
        {
            return SKBitmap.Decode(image.Stream);
        }

        byte[] raw = Inflate(image.Stream);
        int components = raw.Length / Math.Max(1, image.Width * image.Height);
        if (components is not (1 or 3 or 4))
        {
            return null;
        }

        var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int at = ((y * image.Width) + x) * components;
                byte r = raw[at];
                byte g = components >= 3 ? raw[at + 1] : r;
                byte b = components >= 3 ? raw[at + 2] : r;
                bitmap.SetPixel(x, y, new SKColor(r, g, b));
            }
        }

        return bitmap;
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var inflater = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflater.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Mean absolute channel difference, with the smaller picture scaled up to the larger. The
    /// pipeline downsamples for 300 dpi, so the two are not the same size and never will be — what
    /// is being asked is whether it is still the same photograph, not whether it is the same file.
    /// </summary>
    private static double MeanChannelDifference(SKBitmap a, SKBitmap b)
    {
        const int Sample = 64;
        double total = 0;
        for (int y = 0; y < Sample; y++)
        {
            for (int x = 0; x < Sample; x++)
            {
                SKColor left = a.GetPixel(x * (a.Width - 1) / (Sample - 1), y * (a.Height - 1) / (Sample - 1));
                SKColor right = b.GetPixel(x * (b.Width - 1) / (Sample - 1), y * (b.Height - 1) / (Sample - 1));
                total += Math.Abs(left.Red - right.Red)
                    + Math.Abs(left.Green - right.Green)
                    + Math.Abs(left.Blue - right.Blue);
            }
        }

        return total / (Sample * Sample * 3);
    }

    /// <summary>The fixture photograph, drawn rather than shipped — the same one M8's tests use.</summary>
    private static byte[] PhotoJpeg()
    {
        var info = new SKImageInfo(440, 300, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("raster surface");
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xFF7FA8C9));

        using (var ground = new SKPaint { Color = new SKColor(0xFF4F6B3A) })
        {
            canvas.DrawRect(SKRect.Create(0, 190, 440, 110), ground);
        }

        // Hard edges and saturated blocks: the shapes a quality-zero JPEG turns into coloured
        // squares, which is exactly the failure being watched for.
        using (var wall = new SKPaint { Color = new SKColor(0xFFE8DCC0) })
        {
            canvas.DrawRect(SKRect.Create(120, 90, 200, 120), wall);
        }

        using (var door = new SKPaint { Color = new SKColor(0xFF7A3B1E) })
        {
            canvas.DrawRect(SKRect.Create(200, 150, 40, 60), door);
        }

        using SKImage image = surface.Snapshot();
        using SKData encoded = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return encoded.ToArray();
    }
}
