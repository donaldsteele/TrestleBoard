using System.Text;
using SkiaSharp;
using TrestleBoard.PdfPages;
using Xunit;

namespace TrestleBoard.PdfPages.Tests;

/// <summary>
/// Turning a page of somebody else's PDF into a picture (PLAN.md §11 M67).
///
/// <para>The fixtures are PDFs written byte by byte in this file. That is not showing off: a
/// two-page PDF built out of nothing is fictional by construction (§0 rule 2), it is small enough
/// to read in a diff, and the damaged cases cannot be produced any other way.</para>
///
/// <para><b>Nothing here asserts what a rendered page looks like.</b> PDFium is a native library
/// whose output may differ between platforms and between its own versions, and the whole design of
/// this milestone is that its output never reaches the layout engine — it becomes an ordinary
/// picture asset at import and stops being anybody's business. Asserting pixels here would be
/// asserting a promise the app deliberately does not make.</para>
/// </summary>
public sealed class PdfPageRasterizerTests
{
    /// <summary>
    /// The plan's own fallback, checked first: where PDFium will not load, everything in this class
    /// is skipped and the app is expected to refuse the feature in plain language instead.
    /// </summary>
    private static bool Here => PdfPageRasterizer.IsAvailable;

    [Fact]
    public void ThisComputerSaysWhetherItCanReadPdfsAtAllWithoutThrowing()
    {
        // Whatever the answer, asking must never throw — the catalog asks it on every refresh.
        bool answer = PdfPageRasterizer.IsAvailable;
        Assert.Equal(answer, PdfPageRasterizer.IsAvailable);
    }

    [Fact]
    public void TheRefusalSentenceSaysWhatToDoInstead()
    {
        Assert.Contains("cannot read PDFs", PdfPageRasterizer.NotAvailableReason, StringComparison.Ordinal);
        Assert.Contains("save the page as a picture", PdfPageRasterizer.NotAvailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPageInTheFileIsListedWithItsShape()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");

        IReadOnlyList<PdfPageInfo> pages = PdfPageRasterizer.ReadPages(TwoPagePdf());

        Assert.Equal(2, pages.Count);
        Assert.Equal([1, 2], pages.Select(p => p.Number));

        // US Letter, and the second page turned on its side — so the picker can show the shape a
        // page really is rather than assuming portrait.
        Assert.Equal((612, 792), (pages[0].WidthPt, pages[0].HeightPt));
        Assert.Equal((792, 612), (pages[1].WidthPt, pages[1].HeightPt));
    }

    [Fact]
    public void APageComesOutAsAPngOfAboutTheSizeAskedFor()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");

        byte[] png = PdfPageRasterizer.RenderPage(TwoPagePdf(), 1, longestSide: 800);

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);

        using SKBitmap bitmap = SKBitmap.Decode(png);
        Assert.NotNull(bitmap);
        Assert.InRange(Math.Max(bitmap.Width, bitmap.Height), 780, 820);

        // Portrait in, portrait out.
        Assert.True(bitmap.Height > bitmap.Width);
    }

    /// <summary>
    /// A PDF page is ink on whatever it is printed on, so it is composited onto white here. On
    /// transparency it would arrive as black text on nothing and print as a smear over a coloured
    /// panel.
    /// </summary>
    [Fact]
    public void ThePageArrivesOnWhiteRatherThanOnNothing()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");

        byte[] png = PdfPageRasterizer.RenderPage(TwoPagePdf(), 1, longestSide: 200);

        using SKBitmap bitmap = SKBitmap.Decode(png);
        SKColor corner = bitmap.GetPixel(2, 2);

        Assert.Equal(255, corner.Alpha);
        Assert.Equal(SKColors.White.Red, corner.Red);
    }

    [Fact]
    public void TheSecondPageIsTheSecondPage()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");

        byte[] png = PdfPageRasterizer.RenderPage(TwoPagePdf(), 2, longestSide: 400);

        using SKBitmap bitmap = SKBitmap.Decode(png);
        Assert.True(bitmap.Width > bitmap.Height, "page two is landscape, and came out portrait");
    }

    // ---- refusing, in sentences ----------------------------------------------------------------------

    [Fact]
    public void APageThatIsNotInTheFileIsRefusedByNumber()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");

        PdfPageException e = Assert.Throws<PdfPageException>(
            () => PdfPageRasterizer.RenderPage(TwoPagePdf(), 9));

        Assert.Contains("page 9", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatIsNotAPdfSaysSoRatherThanThrowingSomethingRaw()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");

        PdfPageException e = Assert.Throws<PdfPageException>(
            () => PdfPageRasterizer.ReadPages(Encoding.UTF8.GetBytes("This is not a PDF at all.")));

        Assert.Contains("could not", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ATruncatedPdfIsRefusedRatherThanHalfRead()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");

        byte[] whole = TwoPagePdf();

        Assert.Throws<PdfPageException>(() => PdfPageRasterizer.ReadPages(whole[..(whole.Length / 2)]));
    }

    [Fact]
    public void AnEmptyFileIsRefused()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");

        PdfPageException e = Assert.Throws<PdfPageException>(() => PdfPageRasterizer.ReadPages([]));
        Assert.Contains("empty", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the fixture ------------------------------------------------------------------------------------

    /// <summary>
    /// A two-page PDF, written by hand: page one US Letter portrait with a line of fictional text,
    /// page two the same size turned on its side. Built with a real cross-reference table, because
    /// PDFium checks it.
    /// </summary>
    private static byte[] TwoPagePdf()
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 7 0 R >> >> /Contents 4 0 R >>",
            Stream("BT /F1 24 Tf 72 700 Td (Indian Land Lodge 414 - notice) Tj ET"),
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 792 612] /Resources << /Font << /F1 7 0 R >> >> /Contents 6 0 R >>",
            Stream("BT /F1 24 Tf 72 500 Td (District calendar - placeholder) Tj ET"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append('\n');
        pdf.Append("0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        }

        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\n")
            .Append("startxref\n").Append(xref).Append("\n%%EOF\n");

        return Encoding.ASCII.GetBytes(pdf.ToString());

        static string Stream(string content) =>
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream";
    }
}
