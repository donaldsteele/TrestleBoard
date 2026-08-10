using Avalonia;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M74 (a): "Make the PDF" gets the temp-then-rename discipline every other write path in the data
/// core already has (<c>TboardContainer.SaveToFile</c> is the reference). The old code opened the
/// user-chosen destination directly, truncate-on-open, so disk-full or an antivirus lock mid-write
/// destroyed last month's good PDF and left a partial file — right name, right icon — that is
/// exactly what the committee member would then email.
/// </summary>
public sealed class ExportAtomicityTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tb-export-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static MainWindow OpenLaidOut()
    {
        var window = new MainWindow();
        window.Show();
        window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
        window.OpenIssueSample();
        window.Measure(new Size(1280, 860));
        window.Arrange(new Rect(0, 0, 1280, 860));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>
    /// The defect itself: when the export cannot finish, the file that was already at the chosen
    /// name — last month's good PDF — must be exactly as it was, and the failure sentence must say
    /// so. The write is made to fail by a directory squatting on the temp name beside the target,
    /// which fails the temp open the same way a locked or unwritable temp would; with the old
    /// straight-to-destination write this scenario went ahead and replaced the sentinel.
    /// </summary>
    [Fact]
    public async Task AFailedExportLeavesThePdfThatWasThereExactlyAsItWas()
    {
        string dest = Path.Combine(_folder, "Trestle Board 2026-08.pdf");
        byte[] sentinel = "last month's good PDF"u8.ToArray();
        File.WriteAllBytes(dest, sentinel);

        // Something is squatting on the temp name beside the target.
        Directory.CreateDirectory(dest + ".tmp");

        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.SwallowErrorsForTest = true;
                window.PrintAnswerForTest = false;
                window.ExportPathForTest = dest;

                await window.ExportPdfAsync(draft: true);

                Assert.Equal(sentinel, File.ReadAllBytes(dest));
                Assert.NotNull(window.LastErrorForTest);
                Assert.Contains("has not been touched", window.LastErrorForTest, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// And when nothing goes wrong, the whole PDF lands at the chosen name, the temp is gone, and
    /// "Print it" is pointed at the right file.
    /// </summary>
    [Fact]
    public async Task ASuccessfulExportPutsTheWholePdfAtTheChosenNameAndCleansUp()
    {
        string dest = Path.Combine(_folder, "Trestle Board 2026-08.pdf");

        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.ReviewOfferAnswerForTest = MainWindow.ReviewOffer.MakeItNow;
                window.PrintAnswerForTest = false;
                window.ExportPathForTest = dest;

                await window.ExportPdfAsync();

                byte[] written = File.ReadAllBytes(dest);
                Assert.True(written.Length > 4);
                Assert.Equal("%PDF"u8.ToArray(), written[..4]);
                Assert.False(File.Exists(dest + ".tmp"));
                Assert.Equal(dest, window.LastExportedPdf);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A guard, not a regression test: this branch did not exist before M74 (a). A save picker can
    /// hand back a place with no path on the local disk — a cloud location — and a PDF cannot be
    /// written there temp-then-rename, so the export refuses and says so instead of writing
    /// dangerously or crashing.
    /// </summary>
    [Fact]
    public async Task APlaceWithNoPathOnThisComputerIsRefusedInPlainLanguage()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.SwallowErrorsForTest = true;
                window.ExportPickerHasNoLocalPathForTest = true;

                await window.ExportPdfAsync(draft: true);

                Assert.NotNull(window.LastErrorForTest);
                Assert.Contains("cannot tell where", window.LastErrorForTest, StringComparison.Ordinal);
                Assert.Contains("Nothing was written", window.LastErrorForTest, StringComparison.Ordinal);
                Assert.Null(window.LastExportedPdf);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
