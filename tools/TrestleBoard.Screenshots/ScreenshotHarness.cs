using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TrestleBoard.App;
using TrestleBoard.App.Settings;

namespace TrestleBoard.Screenshots;

/// <summary>
/// Runs the real application windows offscreen and photographs them.
///
/// Two things make this honest rather than a mock-up. First, <c>UseHeadlessDrawing = false</c>
/// alongside <c>.UseSkia()</c>: Avalonia composites through Skia, so
/// <c>PageDrawOperation.Render</c> gets the <c>ISkiaSharpApiLeaseFeature</c> it needs and the page
/// area is drawn by the same engine that draws the PDF. Second, <c>.WithInterFont()</c>: without it
/// the chrome falls back to whatever font the machine happens to offer, and every shot silently
/// becomes machine-dependent.
///
/// <b>The privacy guard is structural, not a rule (PLAN.md §0 rule 6).</b>
/// <see cref="AppPaths.Root"/> is pointed at a temporary folder before Avalonia starts, so this
/// process cannot read <c>%AppData%/TrestleBoard/roster.json</c> — the real lodge address book —
/// even though it runs on the maintainer's own machine. The People window shot is taken against a
/// fictional roster written into that temporary folder.
/// </summary>
internal sealed class ScreenshotHarness : IDisposable
{
    private readonly HeadlessUnitTestSession _session;
    private readonly string _stateRoot;
    private bool _disposed;

    private ScreenshotHarness(HeadlessUnitTestSession session, string stateRoot)
    {
        _session = session;
        _stateRoot = stateRoot;
    }

    public static ScreenshotHarness Start()
    {
        string stateRoot = Path.Combine(
            Path.GetTempPath(),
            "TrestleBoard-screenshots",
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(stateRoot);

        // Before Avalonia, before any window, before anything reads a path. This is the line that
        // makes a real-roster screenshot impossible rather than merely forbidden.
        AppPaths.Root = stateRoot;

        HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotAppBuilder));
        return new ScreenshotHarness(session, stateRoot);
    }

    /// <summary>Takes one shot on the UI thread and returns the PNG bytes.</summary>
    public byte[] Capture(Shot shot)
    {
        ArgumentNullException.ThrowIfNull(shot);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _session.Dispatch(
            async () =>
            {
                var stage = new Stage(_stateRoot);
                try
                {
                    return await shot.Take(stage).ConfigureAwait(true);
                }
                finally
                {
                    stage.Dispose();
                }
            },
            CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
        try
        {
            Directory.Delete(_stateRoot, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a documentation run over.
        }
    }
}

/// <summary>
/// The Avalonia app the harness runs. Skia rather than the headless software drawer, because the
/// page canvas leases the Skia surface directly and returns early without it.
/// </summary>
internal static class ScreenshotAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App.App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Everything a shot is allowed to do: build a window, settle it, photograph it. Windows opened
/// through here are closed when the shot finishes, so one shot cannot leak state into the next.
/// </summary>
internal sealed class Stage : IDisposable
{
    private readonly List<Window> _windows = [];

    public Stage(string stateRoot) => StateRoot = stateRoot;

    /// <summary>The temporary app-state folder. Fictional fixtures are written here.</summary>
    public string StateRoot { get; }

    /// <summary>
    /// Shows the real editor shell with the given chrome settings, already holding the five-page
    /// sample issue. Settings are INJECTED rather than loaded, so the maintainer's own theme and
    /// scale cannot change what the documentation looks like.
    /// </summary>
    public MainWindow OpenEditor(AppSettings? settings = null)
    {
        var window = new MainWindow();
        Track(window);
        window.ApplySettings(settings ?? DocumentationSettings);
        window.Show();
        Settle(window);
        window.OpenIssueSample();
        window.FitPage();
        Settle(window);
        return window;
    }

    /// <summary>
    /// Shows a dialog and settles it. The size is the dialog's own unless one is given — a shot
    /// that silently resized the window would document a layout the user never sees.
    /// </summary>
    public T OpenDialog<T>(T window, double? width = null, double? height = null)
        where T : Window
    {
        ArgumentNullException.ThrowIfNull(window);
        if (width is { } w)
        {
            window.Width = w;
        }

        if (height is { } h)
        {
            window.Height = h;
        }

        Track(window);
        window.Show();
        Settle(window);
        return window;
    }

    /// <summary>
    /// The chrome the documentation is shot in: Light, 100%, panel shown. Anything the reader
    /// might otherwise mistake for their own broken installation is pinned here.
    /// </summary>
    public static AppSettings DocumentationSettings => new()
    {
        Theme = ThemeChoice.Light,
        UiScalePercent = 100,
        ShowActionPanel = true,
    };

    /// <summary>
    /// Runs the layout, binding and render passes until the frame stops changing. Avalonia is
    /// asynchronous everywhere — without this a shot catches a half-drawn window.
    /// </summary>
    public static void Settle(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        for (int i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();

            // The headless render timer is what drives the layout and compositor passes; ticking
            // it repeatedly is how a window that measures, then reflows, then measures again
            // (which is every window here, because the canvas sizes itself from the page) reaches
            // a steady frame.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Photographs a window. Throws rather than writing a blank image if the compositor gave
    /// nothing back — a silently empty screenshot is worse than a failed run.
    /// </summary>
    public static byte[] Shoot(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Settle(window);

        WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException(
                "The compositor returned no frame. Check that UseSkia() and UseHeadlessDrawing=false "
                + "are both in the AppBuilder.");

        using (frame)
        {
            using var buffer = new MemoryStream();
            frame.Save(buffer);
            return Png.Sanitise(buffer.ToArray());
        }
    }

    private void Track(Window window) => _windows.Add(window);

    public void Dispose()
    {
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            try
            {
                _windows[i].Close();
            }
#pragma warning disable CA1031 // Closing a window during teardown must never fail the run.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Nothing useful to do; the process is about to move on to the next shot.
            }
        }

        _windows.Clear();
        Dispatcher.UIThread.RunJobs();
    }
}
