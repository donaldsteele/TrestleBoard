using Avalonia;
using Avalonia.Headless;
using TrestleBoard.App;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>Headless AppBuilder: no compositor drawing (the snapshot suite proves pixels);
/// these tests exercise the viewer's open/navigate/zoom state machine.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

public sealed class ViewerSmokeTests
{
    private static HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

    [Fact]
    public async Task OpenSampleShowsPageOneOfTwo()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            Assert.Equal("Page 1 of 2", window.PageLabelTextForTest);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PageNavigationUpdatesLabelAndStops()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            window.GoToNextPageForTest();
            Assert.Equal("Page 2 of 2", window.PageLabelTextForTest);
            window.GoToNextPageForTest(); // past the end: must clamp, not throw
            Assert.Equal("Page 2 of 2", window.PageLabelTextForTest);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OpenSampleComputesFitZoom()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            Assert.NotNull(window.ZoomLabelTextForTest);
            Assert.EndsWith("%", window.ZoomLabelTextForTest, StringComparison.Ordinal);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
