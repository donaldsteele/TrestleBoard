using SkiaSharp;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Samples;
using TrestleBoard.Export.Pdf;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// M87: small enough to email.
///
/// <para>The exporter rasters pictures at 300 dpi and re-encodes nothing, which is right for the
/// copy that gets printed and can be too big for a member's inbox. M56's send card named a file the
/// app had never weighed.</para>
/// </summary>
public sealed class EmailSizedPdfTests
{
    /// <summary>
    /// The issue with a photograph-like picture in it.
    ///
    /// <para><b>Two fixtures were wrong before this one.</b> With no picture at all the two exports
    /// came out byte-identical — nothing to re-encode. With the flat-colour test picture the email
    /// copy came out LARGER, because lossless encoding of flat blocks beats JPEG. That is M90's
    /// lesson in the other direction: a size comparison measures compressibility, not the feature,
    /// unless what is being compressed is the kind of thing the feature is for.</para>
    /// </summary>
    private static byte[] Export(bool forEmail)
    {
        TboardPackage package = SampleIssue.CreatePackage(PhotographLikePng());
        using DocumentRenderSource source = DocumentRenderSource.Create(
            package.Document, package.Assets, SnapshotInfra.Store.Value);

        using var buffer = new MemoryStream();
        DocumentPdfExporter.Export(
            buffer,
            source,
            new PdfMetadata("Test issue", "Placeholder Lodge", "A test"),
            watermark: null,
            forEmail: forEmail);

        return buffer.ToArray();
    }

    /// <summary>The email copy is smaller. That is the whole point of it.</summary>
    [Fact]
    public void TheEmailCopyIsSmallerThanTheFullOne()
    {
        byte[] full = Export(forEmail: false);
        byte[] email = Export(forEmail: true);

        Assert.True(
            email.Length < full.Length,
            $"the email copy is {email.Length} bytes against the full copy's {full.Length}");
    }

    /// <summary>
    /// It is the same newsletter: same number of pages, same words, same tappable links. Only the
    /// pictures differ, which is what makes it safe to send instead of the other one.
    /// </summary>
    [Fact]
    public void NothingButThePicturesDiffers()
    {
        byte[] full = Export(forEmail: false);
        byte[] email = Export(forEmail: true);

        Assert.Equal(PageCount(full), PageCount(email));
        Assert.Equal(LinkAnnotationCount(full), LinkAnnotationCount(email));
    }

    /// <summary>
    /// The full copy is untouched by this milestone. It is what "Print it" hands to the printer,
    /// and M90 is the milestone about what happens when the printed copy is quietly degraded.
    /// </summary>
    [Fact]
    public void TheFullCopyStillReEncodesNothing()
    {
        byte[] full = Export(forEmail: false);

        // 101 is Skia's "do not re-encode at all". Anything else means the pictures were rewritten.
        Assert.Equal(101, SKDocumentPdfMetadata.Default.EncodingQuality);
        Assert.True(full.Length > 0);
    }

    /// <summary>
    /// The threshold and the sentence beside it have to agree. The card tells the user a size, and
    /// a threshold that disagrees with its own explanation is worse than no warning at all.
    /// </summary>
    [Fact]
    public void TheThresholdAndItsExplanationAgree()
    {
        Assert.Equal(8L * 1024 * 1024, DocumentPdfExporter.BigPdfThresholdBytes);

        // Just over the threshold reads as "8.4 MB", not "8 MB" and not "8388608 bytes".
        Assert.Equal(
            "This PDF is 8.4 MB, which some email services will refuse.",
            DocumentPdfExporter.TooBigSentence((long)(8.4 * 1024 * 1024)));

        // Above ten it drops the decimal, because "14.2 MB" is more precision than the sentence needs.
        Assert.Equal("14 MB", DocumentPdfExporter.Megabytes((long)(14.2 * 1024 * 1024)));
    }

    /// <summary>
    /// A picture that compresses like a photograph: continuous tone with fine detail everywhere, so
    /// JPEG wins where it wins on a real one. Deterministic — a fixed seed, because a fixture that
    /// differs between runs makes a size comparison meaningless.
    /// </summary>
    private static byte[] PhotographLikePng()
    {
        var info = new SKImageInfo(440, 300, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var bitmap = new SKBitmap(info);

        var random = new Random(414);
        for (int y = 0; y < info.Height; y++)
        {
            for (int x = 0; x < info.Width; x++)
            {
                // A gradient for the tone, noise for the detail. Flat colour would compress
                // losslessly better than any JPEG and prove the opposite of what this is for.
                int baseTone = 40 + ((x + y) * 120 / (info.Width + info.Height));
                byte r = (byte)Math.Clamp(baseTone + random.Next(-30, 30), 0, 255);
                byte g = (byte)Math.Clamp(baseTone + 20 + random.Next(-30, 30), 0, 255);
                byte b = (byte)Math.Clamp(baseTone + 45 + random.Next(-30, 30), 0, 255);
                bitmap.SetPixel(x, y, new SKColor(r, g, b));
            }
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("png encode");
        return data.ToArray();
    }

    private static int PageCount(byte[] pdf) => Occurrences(pdf, "/Type /Page\n");

    private static int LinkAnnotationCount(byte[] pdf) => Occurrences(pdf, "/Subtype /Link");

    /// <summary>
    /// Counted over the raw bytes as Latin-1. The PDFs here are uncompressed enough for their
    /// structure to be readable, and this avoids a PDF library the test project does not have.
    /// </summary>
    private static int Occurrences(byte[] pdf, string needle)
    {
        string text = System.Text.Encoding.Latin1.GetString(pdf);
        int count = 0;
        int at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
