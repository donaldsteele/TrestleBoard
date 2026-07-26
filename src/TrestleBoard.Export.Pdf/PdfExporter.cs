using SkiaSharp;
using TrestleBoard.Layout;
using TrestleBoard.Rendering;

namespace TrestleBoard.Export.Pdf;

public sealed record PdfMetadata(
    string Title,
    string Author,
    string Subject,
    string Creator = "TrestleBoard",
    string Producer = "TrestleBoard");

public sealed record PdfPage(int WidthPt, int HeightPt, LayoutResult Layout);

/// <summary>
/// PDF export through SKDocument.CreatePdf using the SAME PageRenderer code path as the
/// screen/PNG renderer — WYSIWYG parity by construction (PLAN.md §1). Text is drawn as
/// glyph blobs from real typefaces, so Skia embeds subset fonts and a ToUnicode CMap
/// (selectable, extractable text).
/// </summary>
public sealed class PdfExporter
{
    private readonly PageRenderer _renderer;

    public PdfExporter(PageRenderer renderer)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public void Export(Stream output, IReadOnlyList<PdfPage> pages, PdfMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(metadata);

        var skMetadata = new SKDocumentPdfMetadata
        {
            Title = metadata.Title,
            Author = metadata.Author,
            Subject = metadata.Subject,
            Creator = metadata.Creator,
            Producer = metadata.Producer,
            RasterDpi = 300,
        };

        using var stream = new SKManagedWStream(output);
        using SKDocument document = SKDocument.CreatePdf(stream, skMetadata)
            ?? throw new InvalidOperationException("SKDocument.CreatePdf returned null.");
        foreach (PdfPage page in pages)
        {
            SKCanvas canvas = document.BeginPage(page.WidthPt, page.HeightPt);
            _renderer.Render(canvas, page.Layout);
            document.EndPage();
        }

        document.Close();
    }
}
