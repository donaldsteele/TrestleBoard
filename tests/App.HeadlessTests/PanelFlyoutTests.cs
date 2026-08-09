using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The action panel's "Paragraph style ▸" button opens its menu (reported 2026-08-09).
///
/// <para><b>The bug.</b> Every command ends with <c>ActionRunner</c> refreshing the shell, and the
/// refresh clears the action panel and builds it again from scratch. The paragraph-style handler
/// opened a flyout anchored to the button that had just been pressed — and that button was torn out
/// of the visual tree a moment later, taking the flyout with it. The user pressed the button and
/// nothing appeared. Nothing had gone wrong in any way a log would show: the menu had opened and
/// shut inside one turn of the loop.</para>
///
/// <para>The Format menu's copy of the same command never had the fault, because a menu item is not
/// in the panel and is not rebuilt — which is why this looked like it depended on what the user had
/// been doing beforehand rather than on which of the two buttons they pressed.</para>
/// </summary>
public sealed class PanelFlyoutTests
{
    [Fact]
    public async Task TheParagraphStyleButtonInThePanelOpensItsMenu()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            window.CanvasForTest.Focus();

            StartWriting(window);

            Button button = PanelButton(window, ActionId.ParagraphStyle);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // The flyout is opened on the next turn of the loop, on purpose: see the fix. Running
            // the queued jobs is what a real user's next frame does for them.
            Dispatcher.UIThread.RunJobs();

            MenuFlyout flyout = Assert.IsType<MenuFlyout>(window.ParagraphStyleFlyoutForTest);
            Assert.NotEmpty(flyout.Items);
            Assert.True(flyout.IsOpen, "the paragraph-style menu opened and shut without being seen");

            flyout.Hide();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// And it still opens when the paragraph has been emptied — the state the report was made from.
    /// </summary>
    [Fact]
    public async Task ItOpensWithTheCaretInAnEmptiedParagraph()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            window.CanvasForTest.Focus();

            StartWriting(window);
            window.EditorForTest!.SelectAll();
            window.ToggleList(Core.Model.ListKinds.Bullet);
            window.EditorForTest.SelectAll();
            window.EditorForTest.Backspace();
            window.RefreshActions();

            Button button = PanelButton(window, ActionId.ParagraphStyle);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.ParagraphStyleFlyoutForTest!.IsOpen);

            window.ParagraphStyleFlyoutForTest.Hide();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The other half of the report — "changing the font does not seem to work". The size steppers
    /// are panel buttons too, and they do not open a flyout, so they were never caught by the fault
    /// above. This says so rather than leaving it assumed.
    /// </summary>
    [Fact]
    public async Task TheSizeSteppersInThePanelDoChangeTheSize()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            window.CanvasForTest.Focus();

            StartWriting(window);
            window.EditorForTest!.SelectAll();
            window.ToggleList(Core.Model.ListKinds.Bullet);
            window.EditorForTest.SelectAll();
            window.EditorForTest.Backspace();
            window.RefreshActions();

            float before = window.EditorForTest.CurrentCharacterStyle!.SizePt;
            PanelButton(window, ActionId.BiggerText).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                window.EditorForTest.CurrentCharacterStyle!.SizePt > before,
                $"the size did not move: {before} then {window.EditorForTest.CurrentCharacterStyle!.SizePt}");

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    private static void StartWriting(MainWindow window)
    {
        string blockId = window.FramesForTest!.AddTextFrame(0);
        window.EditorForTest!.TryBeginAt(
            0,
            window.SourceForTest!.GetEffectiveRect(blockId).X + 2f,
            window.SourceForTest.GetEffectiveRect(blockId).Y + 2f);
        window.EditorForTest.InsertText("Bring a chair");
        window.RefreshActions();
    }

    private static Button PanelButton(MainWindow window, string actionId)
    {
        Button? button = window.PanelForTest
            .GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Tag as string == actionId);

        Assert.True(button is not null, $"the panel is not offering {actionId}");
        return button!;
    }
}
