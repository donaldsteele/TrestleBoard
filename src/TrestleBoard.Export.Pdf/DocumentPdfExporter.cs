using SkiaSharp;
using TrestleBoard.Rendering;

namespace TrestleBoard.Export.Pdf;

/// <summary>
/// Whole-document PDF export: every page drawn by the SAME DocumentRenderSource the editor
/// canvas uses — WYSIWYG parity by construction (PLAN.md §1).
/// </summary>
public static class DocumentPdfExporter
{
    /// <param name="watermark">
    /// M53: text for the diagonal across every page, or null for the real thing. A draft copy and
    /// the final copy are otherwise the same bytes from the same renderer, which is the point —
    /// what the Master reviews is what goes out, minus a word that says it is not.
    /// </param>
    public static void Export(
        Stream output,
        DocumentRenderSource source,
        PdfMetadata metadata,
        string? watermark = null)
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
                if (!string.IsNullOrWhiteSpace(watermark))
                {
                    // Over the page, not under it: a draft mark beneath the text would be hidden by
                    // every picture and half the widgets, which is exactly where a reader stops
                    // looking.
                    source.RenderWatermark(canvas, i, watermark);
                }

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
