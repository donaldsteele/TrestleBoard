using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using SkiaSharp;
using TrestleBoard.App.Canvas;
using TrestleBoard.Rendering;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// What the canvas is allowed to hand the compositor.
///
/// <para><b>The crash this is about.</b> A drag on the page died with "Operations that change
/// non-concurrent collections must have exclusive access. A concurrent update was performed on this
/// collection and corrupted its state." — thrown on the UI thread, from a plain
/// <c>Dictionary.TryInsert</c> inside <c>DocumentRenderSource.EnsureLayout</c>, reached through
/// <c>FrameEditorController.DragTo</c> → <c>RefreshActions</c> → <c>BuildContext</c>.</para>
///
/// <para>The thread that corrupted the dictionary was not that one. <c>PageDrawOperation.Render</c>
/// runs on the COMPOSITOR thread, and it was handed the render source itself: it called
/// <c>GetPageSize</c> and <c>RenderPage</c>, both of which lay out lazily, and laying out writes to
/// half a dozen dictionaries. A drag writes to those same dictionaries on the UI thread on every
/// single pointer move. Two threads, one <c>Dictionary</c>, no lock — and because a corrupted
/// dictionary does not fail where it was corrupted, the report named the innocent thread.</para>
///
/// <para>The fix is the rule the control had already been following for the caret, the selection,
/// the frame chrome and the font marks, applied to the last thing that was not: <b>snapshot on the
/// UI thread, hand the compositor something self-contained.</b> A recorded <c>SKPicture</c> is that
/// for a page.</para>
/// </summary>
public sealed class CompositorHandoffTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    private static Type DrawOperation =>
        typeof(PageCanvasControl).GetNestedType("PageDrawOperation", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PageDrawOperation is gone or has been renamed.");

    /// <summary>
    /// <b>Nothing the compositor is given may reach the render source.</b> Stated over the type
    /// rather than over one call, because the defect was not that some line rendered wrongly — it
    /// was that an object owned by the UI thread was reachable at all from a thread that runs
    /// beside it. A constructor parameter is how it got there, and a parameter is what this refuses.
    /// </summary>
    [Fact]
    public void TheDrawOperationIsHandedNoRenderSource()
    {
        ConstructorInfo ctor = Assert.Single(DrawOperation.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

        Assert.DoesNotContain(
            ctor.GetParameters(),
            p => typeof(DocumentRenderSource).IsAssignableFrom(p.ParameterType));

        Assert.DoesNotContain(
            DrawOperation.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            f => typeof(DocumentRenderSource).IsAssignableFrom(f.FieldType));

        // What it is handed instead: a recorded page, which is self-contained by construction.
        Assert.Contains(ctor.GetParameters(), p => p.ParameterType == typeof(SKPicture));
    }

    /// <summary>
    /// And the thing it IS handed carries the page: a picture the size of the sheet, recorded
    /// before the operation left the UI thread. A snapshot that came out empty would satisfy the
    /// test above and draw a blank page.
    /// </summary>
    [Fact]
    public async Task ThePageIsRecordedOnTheUiThreadAtTheSizeOfTheSheet()
    {
        await Session.Dispatch(
            () =>
            {
                var window = new MainWindow();
                window.Show();
                window.OpenSample();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                DocumentRenderSource source = window.SourceForTest!;
                Core.Model.SizePt size = source.GetPageSize(0);

                MethodInfo record = typeof(PageCanvasControl).GetMethod(
                    "RecordPage",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                using var picture = (SKPicture)record.Invoke(null, [source, 0, size])!;

                Assert.Equal(size.Width, picture.CullRect.Width, 3);
                Assert.Equal(size.Height, picture.CullRect.Height, 3);

                // A recorded page replays without the source: that is the whole point of handing one
                // to the compositor rather than the thing it came from.
                using var surface = SKSurface.Create(new SKImageInfo(64, 64));
                surface.Canvas.Clear(SKColors.Magenta);
                surface.Canvas.DrawPicture(picture);
                using SKImage drawn = surface.Snapshot();
                Assert.NotEqual(SKColors.Magenta, drawn.PeekPixels().GetPixelColor(32, 32));

                window.Close();
                return true;
            },
            TestContext.Current.CancellationToken);
    }

}
