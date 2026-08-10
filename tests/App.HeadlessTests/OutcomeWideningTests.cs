using System;
using System.Threading.Tasks;
using Avalonia;
using SkiaSharp;
using TrestleBoard.App.Actions;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// PLAN.md §11 M74 (c). M73 (f) gave <c>ActionRunner</c> three answers — refused, nothing, did —
/// and then widened four handlers. The rest kept the <c>Async(Func&lt;Task&gt;)</c> adapter, which
/// answers "it happened" unconditionally, so every window built on <c>ActionOutcome</c> went on
/// saying "Done: …" about pickers and dialogs the user had just cancelled.
///
/// <para>The worst of them are on the two windows the mechanism was built for: the review, which
/// walks a committee member through their newsletter one worry at a time and has them tick each one
/// off, and Help, which is where somebody uncertain goes. A false "Done" on the review gets a real
/// accessibility defect marked as dealt with.</para>
/// </summary>
public sealed class OutcomeWideningTests
{
    /// <summary>Synthetic photo — fictional pixels only (PLAN.md §0).</summary>
    private static byte[] PhotoBytes()
    {
        var info = new SKImageInfo(120, 90, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("raster surface");
        surface.Canvas.Clear(new SKColor(0xFF7788AA));
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
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

    /// <summary>Puts a picture on page 1 and chooses it, which is where the picture remedies act.</summary>
    private static string ChooseAPicture(MainWindow window)
    {
        string id = Assert.IsType<string>(window.PhotosForTest!.InsertPhoto(0, PhotoBytes(), "A placeholder photo"));
        window.FramesForTest!.Select(id);
        return id;
    }

    /// <summary>
    /// "Swap this picture…" is a review remedy. The headless picker answers with no file, which is
    /// exactly what pressing Cancel does, and before this the review then said "Done: Swap this
    /// picture…" — so the user ticked off a picture they had decided not to change.
    /// </summary>
    [Fact]
    public async Task ACancelledPictureSwapReportsThatNothingHappened()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                ChooseAPicture(window);

                ActionOutcome outcome = await window.ActionsForTest.RunAsync(ActionId.ReplacePicture);

                Assert.Equal(ActionResult.NothingHappened, outcome.Result);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Help → "Words for hard news" → Cancel. Before this the shelf's cancel path returned exactly
    /// as its insert path did, so Help said "Done." to somebody who had just closed the memorial
    /// wording without choosing any of it.
    /// </summary>
    [Fact]
    public async Task StoppingTheWordsForHardNewsReportsThatNothingHappened()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                // The caret in the first frame, which is what "click into some writing" leaves.
                string storyId = window.PackageForTest!.Document.Stories[0].Id;
                window.EditorForTest!.SelectRange(storyId, 0, 0, 0);

                // Nobody chose anything from the shelf, which is what Cancel amounts to.
                window.PhraseAnswerForTest = null;
                ActionOutcome outcome = await window.ActionsForTest.RunAsync(ActionId.InsertPhrase);

                Assert.Equal(ActionResult.NothingHappened, outcome.Result);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// <b>A guard, not a break-it-first test.</b> A modal dialog cannot be answered headlessly, so
    /// reaching the cancelled path of "Write a caption…" needed a seam
    /// (<c>PictureWordsAnswerForTest</c>) that did not exist before M74 (c) — the M73 (e) precedent.
    /// With the seam in and the widening out, this failed; what it guards from here is a caption
    /// dialog that is cancelled and reported as done.
    /// </summary>
    [Fact]
    public async Task ACancelledCaptionReportsThatNothingHappened()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                ChooseAPicture(window);
                window.PictureWordsAnswerForTest = null;

                ActionOutcome outcome = await window.ActionsForTest.RunAsync(ActionId.CaptionPicture);

                Assert.Equal(ActionResult.NothingHappened, outcome.Result);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// <b>A guard, as above.</b> This is the worst of the three: "Add a description" is the remedy
    /// the review offers for a picture nobody using a screen reader can see, and a "Done: …" said
    /// over a cancelled dialog is how a real accessibility worry gets ticked off unfixed.
    /// </summary>
    [Fact]
    public async Task ACancelledPictureDescriptionReportsThatNothingHappened()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                string id = ChooseAPicture(window);
                window.PictureWordsAnswerForTest = null;

                ActionOutcome outcome = await window.ActionsForTest.RunAsync(ActionId.DescribePicture);

                Assert.Equal(ActionResult.NothingHappened, outcome.Result);
                Assert.Equal(
                    "A placeholder photo",
                    ((Core.Model.ImageFrame)window.SessionForTest!.Document.FindBlock(id).Block).AltText);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// <b>A guard, as above</b> — the import wizard cannot be answered headlessly either.
    ///
    /// <para>This one was a live contradiction rather than a plain overclaim: the shell announced
    /// "The import was stopped. Nothing in your address book was changed." into the status bar while
    /// Help said "Done:" into its own live region, at the same moment, about the same press. Two
    /// live regions asserting opposite outcomes is worse than either being wrong alone, because
    /// there is no way for the user to tell which one to believe.</para>
    /// </summary>
    [Fact]
    public async Task StoppingTheImportIsNotAlsoReportedAsDone()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.ImportAnswerForTest = null;

                ActionOutcome outcome = await window.ActionsForTest.RunAsync(ActionId.ImportPeople);

                Assert.Equal(ActionResult.NothingHappened, outcome.Result);
                Assert.Contains(
                    "The import was stopped",
                    window.StatusLabelTextForTest ?? "",
                    StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The sweep, on the widget wizards. Cancelling "Change what this says" leaves the box exactly
    /// as it was and says so; nothing may then report it as a change.
    /// </summary>
    [Fact]
    public async Task ACancelledWidgetWizardReportsThatNothingHappened()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                string? widget = null;
                foreach (Core.Model.Block block in window.SessionForTest!.Document.Pages[0].Blocks)
                {
                    if (block is Core.Model.WidgetBlock)
                    {
                        widget = block.Id;
                        break;
                    }
                }

                Assert.NotNull(widget);
                window.FramesForTest!.Select(widget);
                window.CancelTheWizardForTest = true;

                ActionOutcome outcome = await window.ActionsForTest.RunAsync(ActionId.EditWidget);

                Assert.Equal(ActionResult.NothingHappened, outcome.Result);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
