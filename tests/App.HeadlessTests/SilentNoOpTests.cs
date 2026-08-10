using System;
using System.Collections.Generic;
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
    /// **The rule behind (c), and since M73 (i) it really is one.** Invoking an AVAILABLE command
    /// must leave the user with something to read. A command the catalog refuses is a different case
    /// and is covered by <see cref="AnswerDeliveryTests"/>.
    ///
    /// <para><b>It used to iterate a hardcoded pair</b> — "Move it to the front" and "Move it to the
    /// back" — while calling itself the rule asserted once rather than command by command. Every
    /// single finding of M73 (e) was invisible to it, which is what a claim like that costs when it
    /// is not true. It now enumerates the catalog.</para>
    ///
    /// <para><b>What is enumerated, and why that set.</b> Every command in the groups that act on the
    /// page, minus those whose own title ends in "…" or "▸" — the app's own mark, used everywhere
    /// from the menu bar to the help window, for "this opens something and asks you". Both halves are
    /// read off the catalog, so a new command joins this test by existing. The groups left out are
    /// <see cref="ActionGroup.Newsletter"/>, <see cref="ActionGroup.Page"/>,
    /// <see cref="ActionGroup.People"/>, <see cref="ActionGroup.Everything"/> and
    /// <see cref="ActionGroup.Help"/>: each of those opens a window, a file picker or a confirmation
    /// — and a window IS an answer, so silence is not the failure mode there. That is a real gap and
    /// it is stated rather than hidden: this rule covers the commands that act on the page in place,
    /// which is where a command can finish having done nothing and leave the screen unchanged.</para>
    ///
    /// <para>Each command starts from the same state — a fresh frame of writing, selected — because
    /// running them in sequence would let one command's effect decide the next one's availability,
    /// and the second press is what makes the "nothing to do" case certain.</para>
    /// </summary>
    [Fact]
    public async Task AnAvailableCommandAlwaysLeavesSomethingToRead()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                ActionGroup[] actOnThePage =
                [
                    ActionGroup.Edit, ActionGroup.Text, ActionGroup.Insert, ActionGroup.Item,
                    ActionGroup.Picture, ActionGroup.TextFlow, ActionGroup.Arrange, ActionGroup.View,
                ];

                string[] candidates =
                [
                    .. ActionCatalog.All
                        .Where(a => actOnThePage.Contains(a.Group))
                        .Where(a => !a.Title.Contains('…', StringComparison.Ordinal)
                                    && !a.Title.Contains('▸', StringComparison.Ordinal))
                        .Select(a => a.Id),
                ];

                List<string> silent = [];
                int ran = 0;

                foreach (string id in candidates)
                {
                    // The same starting point every time, so one command cannot decide the next
                    // one's availability.
                    string blockId = window.FramesForTest!.AddTextFrame(0);
                    window.FramesForTest.Select(blockId);
                    window.RefreshActions();

                    if (!ActionCatalog.Evaluate(id, window.CurrentActionContext).IsAvailable)
                    {
                        continue;
                    }

                    ran++;
                    await window.ActionsForTest.RunAsync(id);
                    await window.ActionsForTest.RunAsync(id);   // the second is far likelier a no-op

                    if (string.IsNullOrWhiteSpace(window.StatusLabelTextForTest))
                    {
                        silent.Add(id);
                    }
                }

                // Anti-vacuity: the enumeration must not quietly stop finding commands. The old
                // version of this test checked two; anything near two means the filter has eaten it.
                Assert.True(ran >= 12, $"only {ran} available commands were run — the rule checked almost nothing");

                Assert.True(
                    silent.Count == 0,
                    "these commands ran with nothing to do and left the user nothing to read: "
                    + string.Join(", ", silent)
                    + " — PLAN.md §11 M70 (c).");

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }
}
