using System;
using System.Threading.Tasks;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// PLAN.md §11 M73(c): what the app says when a widget wizard is cancelled.
///
/// <para><b>Why.</b> <c>RunWizardAsync</c> is shared by insert and re-edit. On insert, cancelling
/// leaves an empty box on the page and "Press Ctrl+Z to take it back off the page" is true and
/// useful. On re-edit nothing was put on the page at all, so the same sentence sends the user to
/// undo whatever they last did — M71 multiplied the routes to re-edit, so this is now reached by
/// menu, by shortcut and by the "Fill in the meeting date on the cover" suggestion.</para>
///
/// <para>The grid branch of the same <c>if</c> said nothing at all, which reads exactly like a
/// window that did something and did not mention it.</para>
/// </summary>
public sealed class WizardCancelTests
{
    /// <summary>
    /// The two paths cannot say the same thing, because only one of them put something on the page.
    /// </summary>
    [Fact]
    public async Task CancellingAReEditDoesNotTellTheUserToUndo()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.OpenSample();
            window.CancelTheWizardForTest = true;

            try
            {
                // Insert: the box IS on the page, so Ctrl+Z is the right instruction.
                await window.InsertWidgetAsync("eventCard");
                string afterInsert = window.StatusLabelTextForTest ?? string.Empty;
                Assert.Contains("Ctrl+Z", afterInsert, StringComparison.Ordinal);

                // Re-edit of that same box: nothing was inserted, so Ctrl+Z would take back the
                // user's last unrelated edit.
                window.Announce(string.Empty);
                await window.EditWidgetAsync(grid: false);
                string afterReEdit = window.StatusLabelTextForTest ?? string.Empty;

                Assert.False(
                    string.IsNullOrWhiteSpace(afterReEdit),
                    "cancelling a re-edit said nothing at all");
                Assert.DoesNotContain("Ctrl+Z", afterReEdit, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>The grid editor is a re-edit too, and it used to be silent on cancel.</summary>
    [Fact]
    public async Task CancellingTheGridEditorSaysSomething()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.OpenSample();

            try
            {
                string blockId = window.WidgetsForTest!.InsertWidget(0, "officersTable");
                window.FramesForTest!.Select(blockId);
                window.Announce(string.Empty);
                window.CancelTheWizardForTest = true;

                await window.EditWidgetAsync(grid: true);
                string said = window.StatusLabelTextForTest ?? string.Empty;

                Assert.False(
                    string.IsNullOrWhiteSpace(said),
                    "cancelling the grid editor said nothing at all");
                Assert.DoesNotContain("Ctrl+Z", said, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
