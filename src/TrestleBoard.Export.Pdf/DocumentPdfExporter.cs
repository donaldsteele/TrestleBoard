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

                // M78: the email addresses, web addresses and telephone numbers already on the
                // page become tappable. Half the lodge reads this on a phone.
                //
                // AFTER the page is drawn and never instead of any of it: an annotation is not a
                // drawing operation, it marks a rectangle of the page that is already finished.
                // Nothing above this line changed in M78 and a snapshot test holds it to that —
                // no underline, no blue, not a pixel. The reader's PDF app owns the affordance.
                foreach (DocumentRenderSource.PageLink link in source.GetLinksOnPage(i))
                {
                    canvas.DrawUrlAnnotation(
                        new SKRect(
                            link.Rect.X,
                            link.Rect.Y,
                            link.Rect.X + link.Rect.Width,
                            link.Rect.Y + link.Rect.Height),
                        link.Uri);
                }

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
