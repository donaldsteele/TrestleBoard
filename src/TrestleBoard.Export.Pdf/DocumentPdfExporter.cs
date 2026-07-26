using SkiaSharp;
using TrestleBoard.Rendering;

namespace TrestleBoard.Export.Pdf;

/// <summary>
/// Whole-document PDF export: every page drawn by the SAME DocumentRenderSource the editor
/// canvas uses — WYSIWYG parity by construction (PLAN.md §1).
/// </summary>
public static class DocumentPdfExporter
{
    public static void Export(Stream output, DocumentRenderSource source, PdfMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(source);
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

        // "Not filled in yet" prompts are an on-screen nudge and must never reach a printed
        // newsletter (docs/M7-spec.md §8.4).
        bool prompts = source.ShowEmptyPrompts;
        source.ShowEmptyPrompts = false;
        try
        {
            for (int i = 0; i < source.PageCount; i++)
            {
                Core.Model.SizePt size = source.GetPageSize(i);
                SKCanvas canvas = document.BeginPage(size.Width, size.Height);
                source.RenderPage(canvas, i);
                document.EndPage();
            }
        }
        finally
        {
            source.ShowEmptyPrompts = prompts;
        }

        document.Close();
    }
}
