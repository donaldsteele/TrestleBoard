using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using TrestleBoard.PdfPages;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// A page of somebody else's PDF, brought into the newsletter (PLAN.md §11 M67).
///
/// <para>The rasterizer is held by <c>PdfPages.Tests</c>. What is left for here is the claim the
/// milestone rests on: <b>the page becomes an ordinary picture</b>, and the PDF it came from is
/// kept in the container beside it.</para>
/// </summary>
public sealed class PdfPageTests
{
    private static bool Here => PdfPageRasterizer.IsAvailable;

    [Fact]
    public async Task ThePageArrivesAsAnOrdinaryPictureAndOneUndoTakesItBack()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");
        using var file = new TemporaryFile(OnePagePdf());

        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();
                int before = PicturesOn(window);

                window.PdfPathForTest = file.Path;
                window.PdfPageAnswerForTest = 1;
                await window.BringInPdfPageAsync();

                Assert.Equal(before + 1, PicturesOn(window));

                // No new frame type: what landed is an ImageFrame like any other, and the
                // acceptance is worded exactly that way.
                Assert.IsType<ImageFrame>(Pictures(window).Last());

                window.Undo();
                Assert.Equal(before, PicturesOn(window));

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Gate 7's discipline: the original PDF is kept in the container beside the picture, so a
    /// later version can re-render the page sharper without the committee finding the file again.
    /// </summary>
    [Fact]
    public async Task ThePdfItselfIsKeptInTheContainerBesideThePicture()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");
        byte[] pdf = OnePagePdf();
        using var file = new TemporaryFile(pdf);

        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.PdfPathForTest = file.Path;
                window.PdfPageAnswerForTest = 1;
                await window.BringInPdfPageAsync();

                ImageFrame added = Pictures(window).Last();
                Assert.NotNull(added.SourcePdfAssetRef);
                Assert.Equal(1, added.SourcePdfPage);

                // Byte for byte, like every other original this app keeps.
                Assert.Equal(pdf, window.PackageForTest!.Assets[added.SourcePdfAssetRef!]);

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// It arrives described as the page it is — honest but nearly useless, which is why the app
    /// says so out loud and points at "Describe this picture". A screen reader cannot read a
    /// picture of writing, and a page of a PDF is exactly that.
    /// </summary>
    [Fact]
    public async Task ItArrivesDescribedAsThePageItIs()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");
        using var file = new TemporaryFile(OnePagePdf());

        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.PdfPathForTest = file.Path;
                window.PdfPageAnswerForTest = 1;
                await window.BringInPdfPageAsync();

                Assert.Contains("Page 1", Pictures(window).Last().AltText, StringComparison.Ordinal);
                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ClosingThePickerWithoutChoosingChangesNothing()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");
        using var file = new TemporaryFile(OnePagePdf());

        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();
                int before = PicturesOn(window);
                int assets = window.PackageForTest!.Assets.Count;

                window.PdfPathForTest = file.Path;
                window.PdfPageAnswerForTest = 0;   // 0 is never a page — stands for "closed it".
                await window.BringInPdfPageAsync();

                Assert.Equal(before, PicturesOn(window));

                // And no orphan PDF left behind in the container.
                Assert.Equal(assets, window.PackageForTest!.Assets.Count);
                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AFileThatIsNotAPdfRefusesInASentenceAndChangesNothing()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");
        using var file = new TemporaryFile(Encoding.UTF8.GetBytes("not a pdf"));

        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow { SwallowErrorsForTest = true };
                window.OpenSample();
                int before = PicturesOn(window);

                window.PdfPathForTest = file.Path;
                window.PdfPageAnswerForTest = 1;
                await window.BringInPdfPageAsync();

                Assert.Contains("could not", window.LastErrorForTest ?? "", StringComparison.OrdinalIgnoreCase);
                Assert.Equal(before, PicturesOn(window));
                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ThePickerShowsOneTilePerPage()
    {
        Assert.SkipUnless(Here, "PDFium did not load on this machine.");
        byte[] pdf = OnePagePdf();

        await HeadlessSession.Instance.Dispatch(() =>
        {
            var picker = new Dialogs.PdfPageWindow(pdf, PdfPageRasterizer.ReadPages(pdf), "notice.pdf");

            Assert.Single(picker.TilesForTest);
            Assert.Null(picker.ChosenPage);

            // A screen-reader user choosing between pages of a bulletin has the number and the
            // shape and nothing else — "upright" is often the whole distinction they need.
            string spoken = Avalonia.Automation.AutomationProperties.GetName(picker.TilesForTest[0]) ?? "";
            Assert.Contains("Page 1", spoken, StringComparison.Ordinal);
            Assert.Contains("upright", spoken, StringComparison.Ordinal);

            picker.ChooseForTest(1);
            Assert.Equal(1, picker.ChosenPage);
        }, TestContext.Current.CancellationToken);
    }

    // ---- the catalog, including the platform that cannot ------------------------------------------

    /// <summary>
    /// The plan's explicit fallback: where the rasterizer will not load, the feature refuses plainly
    /// through the catalog rather than half-working. Both refusals are checked here — and the order
    /// matters, because telling somebody with no newsletter open about a native library would be
    /// answering a question they did not ask.
    /// </summary>
    [Fact]
    public void WhereThisComputerCannotReadPdfsTheCatalogSaysSo()
    {
        ActionAvailability noNewsletter = ActionCatalog.Evaluate(
            ActionId.BringInPdfPage, new ActionContext { CanReadPdfs = true });
        Assert.False(noNewsletter.IsAvailable);
        Assert.Contains("no newsletter", noNewsletter.Reason, StringComparison.OrdinalIgnoreCase);

        ActionAvailability noPdfium = ActionCatalog.Evaluate(
            ActionId.BringInPdfPage, new ActionContext { HasDocument = true, CanReadPdfs = false });
        Assert.False(noPdfium.IsAvailable);
        Assert.Contains("cannot read PDFs", noPdfium.Reason, StringComparison.Ordinal);

        // …and it says what to do instead, which is the whole point of refusing plainly.
        Assert.Contains("save the page as a picture", noPdfium.Reason, StringComparison.Ordinal);

        Assert.True(ActionCatalog
            .Evaluate(ActionId.BringInPdfPage, new ActionContext { HasDocument = true, CanReadPdfs = true })
            .IsAvailable);
    }

    /// <summary>
    /// <see cref="ActionContext.CanReadPdfs"/> defaults to false, so a context built without
    /// thinking about it refuses the feature. The safe direction: what it guards against is a crash.
    /// </summary>
    [Fact]
    public void TheDefaultAnswerIsThatThisComputerCannot() =>
        Assert.False(new ActionContext().CanReadPdfs);

    // ---- plumbing ------------------------------------------------------------------------------------

    private static System.Collections.Generic.List<ImageFrame> Pictures(MainWindow window) =>
        [.. window.PackageForTest!.Document.Pages.SelectMany(p => p.Blocks).OfType<ImageFrame>()];

    private static int PicturesOn(MainWindow window) => Pictures(window).Count;

    /// <summary>A one-page PDF, written by hand out of fictional words (§0 rule 2).</summary>
    private static byte[] OnePagePdf()
    {
        string content = "BT /F1 24 Tf 72 700 Td (Indian Land Lodge 414 - notice) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new System.Collections.Generic.List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        }

        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(xref).Append("\n%%EOF\n");

        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private sealed class TemporaryFile : IDisposable
    {
        internal TemporaryFile(byte[] bytes)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "TrestleBoard-pdf-tests",
                Guid.NewGuid().ToString("N") + ".pdf");

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllBytes(Path, bytes);
        }

        internal string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The operating system's business, not this test's.
            }
        }
    }
}
