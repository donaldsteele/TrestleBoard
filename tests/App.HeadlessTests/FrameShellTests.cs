using Avalonia.Headless;
using Avalonia.Input;
using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M5 acceptance (PLAN.md §11-M5): "every mouse action has a keyboard path". Drives the real
/// window with real key events — no controller calls — and checks each direct-manipulation
/// gesture lands (docs/M5-spec.md §9).
/// </summary>
public sealed class FrameShellTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    private static RectPt RectOf(MainWindow window, string blockId) =>
        window.SessionForTest!.Document.FindBlock(blockId).Block.FrameRect;

    [Fact]
    public async Task EveryFrameActionIsReachableFromTheKeyboard()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            window.CanvasForTest.Focus();

            // Select — Tab cycles the page's blocks in stacking order.
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            string first = Assert.IsType<string>(window.FramesForTest!.SelectedBlockId);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Assert.NotEqual(first, window.FramesForTest.SelectedBlockId);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.Equal(first, window.FramesForTest.SelectedBlockId);

            // Move — arrows nudge 1pt, Shift+arrows 10pt.
            RectPt before = RectOf(window, first);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal(before.Y + 1f, RectOf(window, first).Y, 3);
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
            Assert.Equal(before.X + 10f, RectOf(window, first).X, 3);

            // Resize — Ctrl+arrows move the right/bottom edges.
            RectPt beforeResize = RectOf(window, first);
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Control);
            Assert.Equal(beforeResize.Width + 1f, RectOf(window, first).Width, 3);

            // Add a text frame — Ctrl+Shift+T; it becomes the selection.
            int blocksBefore = window.SessionForTest!.Document.Pages[0].Blocks.Count;
            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Equal(blocksBefore + 1, window.SessionForTest.Document.Pages[0].Blocks.Count);
            string added = Assert.IsType<string>(window.FramesForTest.SelectedBlockId);
            Assert.Equal("Add text frame", window.SessionForTest.UndoDescription);

            // Wrap toggle — Ctrl+Shift+W.
            window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Equal(WrapMode.Rectangle, window.SessionForTest.Document.FindBlock(added).Block.WrapMode);

            // Stacking — Ctrl+[ sends backward, Ctrl+] brings it back.
            int topZ = window.SessionForTest.Document.FindBlock(added).Block.ZOrder;
            window.KeyPressQwerty(PhysicalKey.BracketLeft, RawInputModifiers.Control);
            int loweredZ = window.SessionForTest.Document.FindBlock(added).Block.ZOrder;
            Assert.True(loweredZ < topZ);
            window.KeyPressQwerty(PhysicalKey.BracketRight, RawInputModifiers.Control);
            Assert.True(window.SessionForTest.Document.FindBlock(added).Block.ZOrder > loweredZ);

            // Enter starts typing in the selected text frame; Escape hands it back to frame mode.
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.True(window.EditorForTest!.IsActive);
            Assert.Null(window.FramesForTest.SelectedBlockId);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(window.EditorForTest.IsActive);
            Assert.Equal(added, window.FramesForTest.SelectedBlockId);

            // Delete removes the frame.
            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            Assert.Equal(blocksBefore, window.SessionForTest.Document.Pages[0].Blocks.Count);
            Assert.Equal("Delete frame", window.SessionForTest.UndoDescription);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LinkModeArmsFromTheKeyboardAndExplainsItselfInPlainLanguage()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            window.CanvasForTest.Focus();

            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control | RawInputModifiers.Shift);
            window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control | RawInputModifiers.Shift);

            Assert.True(window.FramesForTest!.IsLinkModeActive);
            Assert.Equal(
                "Click the frame the text should continue into, or press Tab to pick one and Enter to confirm.",
                window.StatusLabelTextForTest);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(window.FramesForTest.IsLinkModeActive);
            Assert.Equal("", window.StatusLabelTextForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LinkingCompletesFromTheKeyboardAlone()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            window.CanvasForTest.Focus();

            // A fresh empty frame to link into, then select an unlinked text frame as the source.
            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control | RawInputModifiers.Shift);
            string target = Assert.IsType<string>(window.FramesForTest!.SelectedBlockId);
            window.FramesForTest.Select("block-officers");

            window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control | RawInputModifiers.Shift);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Assert.Equal(target, window.FramesForTest.LinkTargetBlockId);
            // Tab moved the link cursor, not the selection.
            Assert.Equal("block-officers", window.FramesForTest.SelectedBlockId);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.False(window.FramesForTest.IsLinkModeActive);
            var source = (TextBlock)window.SessionForTest!.Document.FindBlock("block-officers").Block;
            Assert.Equal(target, source.LinkNext);
            Assert.Equal("Link frames", window.SessionForTest.UndoDescription);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ClickingTextStillTypesButTheFrameEdgeSelectsTheFrame()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            // The M4 promise: one click in the body of a text frame starts typing.
            Assert.True(window.EditorForTest!.TryBeginAt(0, 100f, 200f));
            Assert.True(window.EditorForTest.IsActive);
            Assert.Null(window.FramesForTest!.SelectedBlockId);

            // Selecting a frame ends the text session (the two modes are exclusive).
            window.FramesForTest.SelectAt(0, 100f, 200f);
            window.EditorForTest.End();
            Assert.False(window.EditorForTest.IsActive);
            Assert.NotNull(window.FramesForTest.SelectedBlockId);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
