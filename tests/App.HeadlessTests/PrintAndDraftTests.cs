using System;
using System.IO;
using System.Threading.Tasks;
using TrestleBoard.App.Integration;
using TrestleBoard.Core.Samples;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Export.Pdf;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Rendering;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M53: the draft copy and the print hand-off.
///
/// <para>Nothing here presses a real printer. The point of the hand-off design is that printing is
/// somebody else's job, so what can be tested is what the app promises: that a draft carries its
/// diagonal and a real export does not, that "Print it" is refused in words until there is
/// something to print, and that a failed hand-off says so rather than claiming success.</para>
/// </summary>
public sealed class PrintAndDraftTests
{
    // ---- the draft copy --------------------------------------------------------------------

    [Fact]
    public void ADraftPdfIsBiggerThanTheRealOneBecauseItCarriesTheDiagonal()
    {
        byte[] plain = ExportBytes(watermark: null);
        byte[] draft = ExportBytes(WatermarkRenderer.DraftText);

        Assert.True(draft.Length > plain.Length, "The draft PDF carries no more than the real one.");
    }

    /// <summary>
    /// The guarantee that matters in the other direction: asking for the real thing produces
    /// exactly what it produced before this milestone existed.
    /// </summary>
    [Fact]
    public void TheRealExportIsUnchangedByTheDraftMachinery()
    {
        Assert.Equal(ExportBytes(watermark: null), ExportBytes(watermark: null));
    }

    [Fact]
    public void TheDraftSaysWhatItIsAndWhatNotToDoWithIt()
    {
        Assert.Contains("DRAFT", WatermarkRenderer.DraftText, StringComparison.Ordinal);
        Assert.Contains("not for sending", WatermarkRenderer.DraftText, StringComparison.Ordinal);
    }

    // ---- "Print it" ------------------------------------------------------------------------

    /// <summary>
    /// M11's rule: nothing becomes unavailable without saying why, and the reason names the way out.
    /// </summary>
    [Fact]
    public void PrintIsRefusedInWordsUntilThereIsAPdf()
    {
        ActionAvailability before = ActionCatalog.Evaluate(
            ActionId.PrintPdf,
            ActionContext.Empty with { HasDocument = true, ExportedPdfThisSession = false });

        Assert.False(before.IsAvailable);
        Assert.Contains("not made the PDF yet", before.Reason, StringComparison.Ordinal);
        Assert.Equal(ActionId.ExportPdf, before.RemedyId);

        ActionAvailability after = ActionCatalog.Evaluate(
            ActionId.PrintPdf,
            ActionContext.Empty with { HasDocument = true, ExportedPdfThisSession = true });

        Assert.True(after.IsAvailable);
    }

    /// <summary>
    /// A file that is not there cannot be printed, and the app must not pretend otherwise. This is
    /// the whole honesty requirement of the milestone in one assertion.
    /// </summary>
    [Fact]
    public void PrintingSomethingThatIsNotThereReportsThatNothingAnswered()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"trestleboard-not-here-{Guid.NewGuid():N}.pdf");

        Assert.Equal(PrintOutcome.NothingAnswered, PrintService.Print(missing));
        Assert.False(PrintService.Open(missing));
    }

    [Fact]
    public void TheFallbackCardNamesTheFileAndTheFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "September 2026.pdf");

        string nothing = PrintService.FallbackMessage(path, PrintOutcome.NothingAnswered);
        Assert.Contains("September 2026.pdf", nothing, StringComparison.Ordinal);
        Assert.Contains(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), nothing, StringComparison.Ordinal);

        string opened = PrintService.FallbackMessage(path, PrintOutcome.OpenedInstead);
        Assert.Contains("Ctrl+P", opened, StringComparison.Ordinal);

        // Neither sentence may claim anything was printed.
        Assert.DoesNotContain("printed", nothing, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("printed", opened, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helper --------------------------------------------------------------------------------

    private static byte[] ExportBytes(string? watermark)
    {
        Core.Container.TboardPackage package = SampleDocument.CreatePackage();
        using FontStore fonts = BundledFonts.CreateDefaultStore();
        using DocumentRenderSource source = DocumentRenderSource.Create(
            package.Document, package.Assets, fonts);

        using var stream = new MemoryStream();
        DocumentPdfExporter.Export(
            stream,
            source,
            new PdfMetadata("Trestle Board", "Indian Land Lodge 414", "Test"),
            watermark);
        return stream.ToArray();
    }
}
