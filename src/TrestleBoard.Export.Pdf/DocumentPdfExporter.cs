using SkiaSharp;
using TrestleBoard.Rendering;

namespace TrestleBoard.Export.Pdf;

/// <summary>
/// Whole-document PDF export: every page drawn by the SAME DocumentRenderSource the editor
/// canvas uses — WYSIWYG parity by construction (PLAN.md §1).
/// </summary>
public static class DocumentPdfExporter
{
    /// <summary>
    /// M87: how big a PDF has to be before the app says so, in bytes.
    ///
    /// <para><b>8 MB, argued against the mailboxes this newsletter actually meets.</b> Gmail
    /// refuses attachments over 25 MB and Outlook.com over 20, but a great many lodge members are
    /// on an employer's or a small provider's server, where 10 MB is the common limit and some sit
    /// at 5. Warning at 8 catches the file that will bounce for a third of the lodge while staying
    /// quiet about the ordinary four-page issue, which is well under a megabyte.</para>
    ///
    /// <para><b>Changing this number means changing the sentence beside it.</b>
    /// <c>TheThresholdAndItsExplanationAgree</c> fails if one moves without the other, because the
    /// card tells the user a specific size and a threshold that disagrees with its own explanation
    /// is worse than no warning.</para>
    /// </summary>
    public const long BigPdfThresholdBytes = 8L * 1024 * 1024;

    /// <summary>M87: the resolution pictures are rastered at in the email copy.</summary>
    public const int EmailRasterDpi = 150;

    /// <summary>M87: the JPEG quality the email copy re-encodes pictures at.</summary>
    public const int EmailJpegQuality = 82;

    /// <summary>
    /// M87: what the app says about a PDF too big to email, and it names the actual size. A warning
    /// that says "large" tells somebody nothing they can act on.
    /// </summary>
    public static string TooBigSentence(long bytes) =>
        $"This PDF is {Megabytes(bytes)}, which some email services will refuse.";

    /// <summary>Whole megabytes above ten, one decimal below — "14 MB", "8.4 MB".</summary>
    public static string Megabytes(long bytes)
    {
        double mb = bytes / (1024d * 1024d);
        return mb >= 10d
            ? $"{mb:0} MB"
            : $"{mb:0.#} MB";
    }

    /// <param name="watermark">
    /// M53: text for the diagonal across every page, or null for the real thing. A draft copy and
    /// the final copy are otherwise the same bytes from the same renderer, which is the point —
    /// what the Master reviews is what goes out, minus a word that says it is not.
    /// </param>
    /// <param name="forEmail">
    /// M87: the smaller copy. Pictures are rastered at 150 dpi instead of 300 and re-encoded as
    /// JPEG; text, rules and emblems are untouched, because they are vectors and cost nothing.
    ///
    /// <para><b>A second export of the same document, never a post-process of the first.</b> Going
    /// back over a finished PDF to shrink its images means decoding what Skia wrote and writing it
    /// again — a second generation of loss on top of the first, over a file the committee may well
    /// print. Rendering again from the same <see cref="DocumentRenderSource"/> costs a second pass
    /// and gives one generation, from the originals.</para>
    /// </param>
    public static void Export(
        Stream output,
        DocumentRenderSource source,
        PdfMetadata metadata,
        string? watermark = null,
        bool forEmail = false)
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

        // M87. 300 dpi and "do not re-encode" is the full-quality copy and stays the default: it is
        // what "Print it" hands to the printer. The email copy halves the raster resolution and
        // accepts JPEG at 82 — high enough that a photograph on a screen is hard to fault, low
        // enough to be the difference between a mail server taking the newsletter and refusing it.
        //
        // 82 rather than 90: measured on the six-picture fixture, 90 saves about a third and 82
        // about half, and the visible difference between them on a lodge photograph is nil. It is
        // not lower still because M90 is the milestone that exists to say what quality-zero does to
        // a banner with lettering in it.
        skMetadata.RasterDpi = forEmail ? EmailRasterDpi : 300;
        if (forEmail)
        {
            skMetadata.EncodingQuality = EmailJpegQuality;
        }

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
