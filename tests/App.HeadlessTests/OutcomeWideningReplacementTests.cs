using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using TrestleBoard.App.Actions;
using TrestleBoard.Editing.Actions;
using TrestleBoard.PdfPages;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// PLAN.md §11 M74 (f), finishing (c). M74 (c) widened the handlers it could reach and said in as
/// many words that it had stopped short of one family: the commands that <b>replace what is on
/// screen</b>, or hand the whole newsletter somewhere else. Every one of them is reachable from
/// Help's "Do it for me", so cancelling any of them had Help's live region say "Done." — the same
/// defect (c) was written to end, on eleven more commands.
///
/// <para>The family, and what "nothing happened" means in each: an empty file picker (Cancel), a
/// confirmation declined, <c>SaveFirst.Stay</c> ("Go back" to the unsaved-work question), a chosen
/// file that would not load, and a restore where every part failed to be written. None of those put
/// anything new in front of the user, so none of them may be reported as done.</para>
///
/// <para>§0: every file these tests touch is a temporary one holding the fictional sample issue.
/// The address book they read is the headless suite's temporary, empty one.</para>
/// </summary>
public sealed class OutcomeWideningReplacementTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "TrestleBoard-outcome-widening",
        Guid.NewGuid().ToString("N"));

    public OutcomeWideningReplacementTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test over.
        }
    }

    private static MainWindow OpenLaidOut()
    {
        var window = new MainWindow();
        window.Show();
        window.SwallowErrorsForTest = true;
        window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
        window.OpenIssueSample();
        window.Measure(new Size(1280, 860));
        window.Arrange(new Rect(0, 0, 1280, 860));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>Runs one command through the runner and hands back what it claims happened.</summary>
    private static async Task<ActionResult> RunAsync(MainWindow window, string id)
    {
        ActionAvailability availability = ActionCatalog.Evaluate(id, window.CurrentActionContext);
        Assert.True(
            availability.IsAvailable,
            $"{id} was refused before it could run, so this test would prove nothing: {availability.Reason}");

        ActionOutcome outcome = await window.ActionsForTest.RunAsync(id);
        return outcome.Result;
    }

    // ---- "Go back" to the unsaved-work question ------------------------------------------------

    /// <summary>
    /// The three commands that replace the newsletter on screen all ask about unsaved work first,
    /// and "Go back" means the newsletter is still there afterwards (M24). Every one of them then
    /// returned to the runner as though it had done what it asked about, so Help said "Done." to a
    /// committee member who had just chosen to keep the work they were being warned about.
    /// </summary>
    [Theory]
    [InlineData(ActionId.Open)]
    [InlineData(ActionId.NewFromTemplate)]
    [InlineData(ActionId.StartFromLastMonth)]
    public async Task GoingBackFromTheUnsavedWorkQuestionReportsThatNothingHappened(string id)
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                // The question is only asked when there is unsaved work to lose, so there has to
                // be some.
                Assert.True(window.EditorForTest!.TryBeginAt(0, 100f, 200f));
                window.EditorForTest.InsertText("Brethren, ");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(window.HasUnsavedChangesForTest);

                window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Stay;

                Assert.Equal(ActionResult.NothingHappened, await RunAsync(window, id));

                // And the newsletter they chose to keep is still the one on screen.
                Assert.True(window.HasUnsavedChangesForTest);
            }
            finally
            {
                window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// "Go back to an earlier version" with the generation picker closed without a generation. The
    /// newsletter on screen is untouched — the same newsletter, the same page count — and the
    /// command used to report that as a restore.
    /// </summary>
    [Fact]
    public async Task ClosingTheEarlierVersionPickerReportsThatNothingHappened()
    {
        string path = Path.Combine(_folder, "september.tboard");

        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                // Two saves, so the ring holds a copy and the command is offered at all.
                Assert.True(await window.SaveToPathForTest(path));
                window.RemovePage();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(await window.SaveToPathForTest(path));

                // No generation chosen — what closing the dialog leaves.
                window.RestoreChoiceForTest = null;
                int pagesBefore = window.SessionForTest!.Document.Pages.Count;

                Assert.Equal(ActionResult.NothingHappened, await RunAsync(window, ActionId.RestoreDocument));
                Assert.Equal(pagesBefore, window.SessionForTest.Document.Pages.Count);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    // ---- an empty file picker, which is what Cancel leaves --------------------------------------

    /// <summary>
    /// The four commands whose first question is a file picker. The headless picker answers with no
    /// file, which is exactly what pressing Cancel does, and all four then reported a file brought
    /// in or written.
    ///
    /// <para><c>BringInAPack</c> is the one where the false "Done." costs most: somebody on their
    /// first afternoon with a computer that has never had TrestleBoard on it, told their
    /// predecessor's address book is in when it is not.</para>
    /// </summary>
    [Theory]
    [InlineData(ActionId.BringInWriting)]
    [InlineData(ActionId.BringInAPack)]
    public async Task ACancelledFilePickerReportsThatNothingHappened(string id)
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.WritingPathForTest = null;
                window.BringInPackPathForTest = null;

                Assert.Equal(ActionResult.NothingHappened, await RunAsync(window, id));
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>"Bring in a page from a PDF" with the picker cancelled. Skipped where PDFium did
    /// not load, exactly as the rest of <see cref="PdfPageTests"/> is.</summary>
    [Fact]
    public async Task ACancelledPdfPickerReportsThatNothingHappened()
    {
        Assert.SkipUnless(PdfPageRasterizer.IsAvailable, "PDFium did not load on this machine.");

        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.PdfPathForTest = null;

                Assert.Equal(ActionResult.NothingHappened, await RunAsync(window, ActionId.BringInPdfPage));
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    // ---- a confirmation declined ----------------------------------------------------------------

    /// <summary>
    /// "Pack everything up for my successor" names, in as many words, that the lodge's address book
    /// is about to go into one file, and then asks. Answering no is the whole point of asking, and
    /// it was reported as a pack written.
    /// </summary>
    [Fact]
    public async Task DecliningTheSuccessorPackReportsThatNothingHappened()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.PackConfirmForTest = false;

                Assert.Equal(ActionResult.NothingHappened, await RunAsync(window, ActionId.PackUpForSuccessor));
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    // ---- a refusal with a card of its own ---------------------------------------------------------

    /// <summary>
    /// "Send it out" with nobody on either list. The command shows a card saying to open People and
    /// tick a group, and Help said "Done." over the top of it.
    ///
    /// <para>The headless suite's address book is the empty temporary one (PLAN.md §0 rule 5 — no
    /// test may reach the real <c>roster.json</c>), which is exactly the state being tested.</para>
    /// </summary>
    [Fact]
    public async Task SendingItOutWithNobodyOnAListReportsThatNothingHappened()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                Assert.Empty(window.Roster.Book.Members);

                Assert.Equal(ActionResult.NothingHappened, await RunAsync(window, ActionId.SendIt));
                Assert.Contains(
                    "Nobody is on a list yet",
                    window.LastErrorForTest ?? "",
                    StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// "Show me last year's…" when that month of last year is not in the old-issues folder. The
    /// card says so; nothing opened beside the newsletter; and the command reported it as done.
    /// </summary>
    [Fact]
    public async Task LastYearsIssueNotBeingThereReportsThatNothingHappened()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                // A month no folder can hold, so this passes whatever an earlier test left in the
                // remembered old-issues folder.
                window.PackageForTest!.Document.Metadata.IssueMonth = 1;
                window.PackageForTest.Document.Metadata.IssueYear = 1900;
                window.OldIssuesFolderAnswerForTest = _folder;

                Assert.Equal(ActionResult.NothingHappened, await RunAsync(window, ActionId.ShowLastYear));
                Assert.Null(window.LastYearWindowForTest);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
