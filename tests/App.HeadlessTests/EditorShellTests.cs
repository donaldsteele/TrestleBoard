using Avalonia.Headless;
using Avalonia.Input;
using TrestleBoard.App;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>M4 shell smoke (docs/M4-spec.md §8.4): click → type → undo through the real
/// window with the headless Avalonia platform.</summary>
public sealed class EditorShellTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    [Fact]
    public async Task ClickTypeUndoRoundTrip()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            // Click into the cover body text frame (page 1, block-body-1 region).
            Assert.True(window.EditorForTest!.TryBeginAt(0, 100f, 200f));
            window.EditorForTest.InsertText("Brethren, ");

            Assert.True(window.SessionForTest!.CanUndo);
            Assert.Equal("Typing", window.SessionForTest.UndoDescription);

            window.SessionForTest.Undo();
            Assert.False(window.SessionForTest.CanUndo);
            Assert.True(window.SessionForTest.CanRedo);
            window.SessionForTest.Redo();
            Assert.True(window.SessionForTest.CanUndo);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BarePageDownMovesCaretWhileEditingCtrlChangesPage()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            Assert.Equal("Page 1 of 2", window.PageLabelTextForTest);

            Assert.True(window.EditorForTest!.TryBeginAt(0, 100f, 200f));
            int paragraphBefore = window.EditorForTest.Selection.Caret.ParagraphIndex;
            int offsetBefore = window.EditorForTest.Selection.Caret.Offset;

            // Bare PageDown = caret motion (may cross to the linked frame), not page navigation.
            window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
            Assert.True(window.EditorForTest.IsActive);

            // Ctrl+PageDown = page navigation.
            window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.Control);
            Assert.Equal("Page 2 of 2", window.PageLabelTextForTest);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EscEndsSessionAndClickOutsideNeverStartsOne()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            Assert.True(window.EditorForTest!.TryBeginAt(0, 100f, 200f));
            Assert.True(window.EditorForTest.IsActive);
            window.EditorForTest.End();
            Assert.False(window.EditorForTest.IsActive);

            // A point outside every text frame (page margin) never starts a session.
            Assert.False(window.EditorForTest.TryBeginAt(0, 10f, 780f));
            Assert.False(window.EditorForTest.IsActive);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BoldToggleUpdatesDocumentThroughShellEditor()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            Assert.True(window.EditorForTest!.TryBeginAt(0, 100f, 200f));
            window.EditorForTest.SelectAll();
            window.EditorForTest.ToggleBold();
            Assert.True(window.EditorForTest.IsBoldActive);
            Assert.True(window.SessionForTest!.CanUndo);
            window.SessionForTest.Undo();
            Assert.False(window.EditorForTest.IsBoldActive);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
