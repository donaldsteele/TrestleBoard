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

        // Start from Default and override, NEVER from a bare `new` (M90).
        //
        // SKDocumentPdfMetadata is a STRUCT. `new SKDocumentPdfMetadata { ... }` zero-initialises
        // every field the initializer does not mention — including EncodingQuality, where 0 means
        // "encode every image as a JPEG at quality zero". Skia's own default is 101, which means
        // "do not re-encode at all", and that is what this exporter has always meant: PLAN.md M87
        // describes it as rastering pictures at 300 dpi and re-encoding nothing.
        //
        // The symptom was a photograph on the cover of a real newsletter arriving in blocks and
        // colour bands, in a 3 KB JPEG of a 625x253 picture. Text was untouched, because text is
        // vector — which is why it survived every parity check for eighty-nine milestones.
        var skMetadata = SKDocumentPdfMetadata.Default;
        skMetadata.Title = metadata.Title;
        skMetadata.Author = metadata.Author;
        skMetadata.Subject = metadata.Subject;
        skMetadata.Creator = metadata.Creator;
        skMetadata.Producer = metadata.Producer;
        skMetadata.RasterDpi = 300;

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
