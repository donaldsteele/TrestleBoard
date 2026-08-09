using System;
using System.Linq;
using System.Threading.Tasks;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// Verification gate 23, part three: a command that does nothing still says so
/// (PLAN.md §11 M70 (c) and (d)).
///
/// <para><b>Why.</b> Ten commands could complete having done nothing at all and say nothing about
/// it, each in ordinary use — "Move it to the front" on the frontmost frame, "Make it fit" straight
/// after a widget edit, Paste with an empty clipboard, Zoom at the end of the ladder, and Undo,
/// which announced nothing although the catalog already knows the step's name. Two others were
/// worse than silent: they reported success when nothing had happened.</para>
///
/// <para>The distinction this milestone insists on is between a command that <i>cannot run</i> —
/// which the catalog refuses, with a reason, and has since M11 — and a command that runs, has
/// nothing to do, and used to shrug.</para>
/// </summary>
public sealed class SilentNoOpTests
{
    /// <summary>
    /// Bringing the frontmost frame further forward: the commonest no-op in the app.
    /// </summary>
    [Fact]
    public async Task RestackingWhatIsAlreadyInFrontSaysSo()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                string blockId = window.FramesForTest!.AddTextFrame(0);
                window.FramesForTest.Select(blockId);

                // A frame just added is already at the top of the z-order.
                await window.ActionsForTest.RunAsync(ActionId.BringToFront);

                Assert.False(
                    string.IsNullOrWhiteSpace(window.StatusLabelTextForTest),
                    "bringing the frontmost frame to the front said nothing at all");

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Paste with an empty clipboard. The catalog's own description promises the app "says so out
    /// loud when there is nothing to paste" — a promise only the picture branch was keeping.
    /// </summary>
    [Fact]
    public async Task PastingNothingSaysSo()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                string blockId = window.FramesForTest!.AddTextFrame(0);
                window.EditorForTest!.TryBeginAt(
                    0,
                    window.SourceForTest!.GetEffectiveRect(blockId).X + 2f,
                    window.SourceForTest.GetEffectiveRect(blockId).Y + 2f);

                await window.ActionsForTest.RunAsync(ActionId.Paste);

                Assert.False(
                    string.IsNullOrWhiteSpace(window.StatusLabelTextForTest),
                    "pasting an empty clipboard said nothing — and the catalog promises otherwise");

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Undo and Redo announce what they took back. The catalog already knows the step's name and
    /// puts it in the menu header; the roster's own undo has always said it out loud.
    /// </summary>
    [Fact]
    public async Task UndoSaysWhatItTookBack()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.FramesForTest!.AddTextFrame(0);
                await window.ActionsForTest.RunAsync(ActionId.Undo);

                string said = window.StatusLabelTextForTest ?? "";
                Assert.False(string.IsNullOrWhiteSpace(said), "Undo said nothing at all");

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Bold with a caret and no selection arms the next-typed run. The behaviour is right; the
    /// silence was the bug, because nothing on screen changes.
    /// </summary>
    [Fact]
    public async Task ArmingBoldForTheNextWordsSaysSo()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                string blockId = window.FramesForTest!.AddTextFrame(0);
                window.EditorForTest!.TryBeginAt(
                    0,
                    window.SourceForTest!.GetEffectiveRect(blockId).X + 2f,
                    window.SourceForTest.GetEffectiveRect(blockId).Y + 2f);

                Assert.True(window.EditorForTest.Selection.IsEmpty, "the caret should have no selection");
                await window.ActionsForTest.RunAsync(ActionId.Bold);

                Assert.False(
                    string.IsNullOrWhiteSpace(window.StatusLabelTextForTest),
                    "arming bold for the next words said nothing, and nothing on screen changed");

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// (d) The inverse defect: reporting success when nothing happened. Clearing a font override
    /// where there is none must not claim to have put anything back.
    /// </summary>
    [Fact]
    public async Task ClearingAFontOverrideThatIsNotThereDoesNotClaimToHaveDoneIt()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                string blockId = window.FramesForTest!.AddTextFrame(0);
                window.EditorForTest!.TryBeginAt(
                    0,
                    window.SourceForTest!.GetEffectiveRect(blockId).X + 2f,
                    window.SourceForTest.GetEffectiveRect(blockId).Y + 2f);
                window.EditorForTest.InsertText("Ordinary writing with no override at all.");
                window.EditorForTest.SelectAll();

                window.ClearFontOverrideHere();

                string said = window.StatusLabelTextForTest ?? "";
                Assert.DoesNotContain("Put back to the usual font", said, StringComparison.OrdinalIgnoreCase);
                Assert.False(string.IsNullOrWhiteSpace(said), "it should say that there was nothing to put back");

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The rule behind (c), asserted once rather than command by command: invoking an AVAILABLE
    /// command must leave the user with something to read. A command the catalog refuses is a
    /// different case and is covered by <see cref="AnswerDeliveryTests"/>.
    /// </summary>
    [Fact]
    public async Task AnAvailableCommandAlwaysLeavesSomethingToRead()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                string blockId = window.FramesForTest!.AddTextFrame(0);
                window.FramesForTest.Select(blockId);
                window.RefreshActions();

                // The z-order and fit commands are the ones the audit found; they are available with
                // a frame selected and each has a "nothing to do" case reachable immediately.
                string[] available =
                [
                    ActionId.BringToFront,
                    ActionId.SendToBack,
                ];

                foreach (string id in available.Where(
                    i => ActionCatalog.Evaluate(i, window.CurrentActionContext).IsAvailable))
                {
                    await window.ActionsForTest.RunAsync(id);
                    await window.ActionsForTest.RunAsync(id);   // the second is certainly a no-op

                    Assert.False(
                        string.IsNullOrWhiteSpace(window.StatusLabelTextForTest),
                        $"{id} ran with nothing to do and said nothing");
                }

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }
}
