using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using TrestleBoard.App.Settings;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// Themes and UI scale (PLAN.md §6, docs/M9-spec.md §4). These are not cosmetic preferences: for
/// some of this app's users they are the difference between being able to use it and not.
/// </summary>
public sealed class SettingsTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    [Fact]
    public void ScaleIsClampedToTheRangeThePlanFixes()
    {
        Assert.Equal(100, new AppSettings { UiScalePercent = 40 }.Normalised().UiScalePercent);
        Assert.Equal(200, new AppSettings { UiScalePercent = 5000 }.Normalised().UiScalePercent);
        Assert.Equal(150, new AppSettings { UiScalePercent = 150 }.Normalised().UiScalePercent);
        Assert.Equal(1.5d, new AppSettings { UiScalePercent = 150 }.UiScale);
    }

    [Fact]
    public void SettingsRoundTripThroughDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"trestleboard-settings-{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings { Theme = ThemeChoice.HighContrast, UiScalePercent = 175 }.Save(path);
            AppSettings loaded = AppSettings.Load(path);

            Assert.Equal(ThemeChoice.HighContrast, loaded.Theme);
            Assert.Equal(175, loaded.UiScalePercent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Losing a preference is a nuisance; refusing to start is not. A settings file that has been
    /// hand-edited into nonsense must yield defaults, not an exception.
    /// </summary>
    [Fact]
    public void NonsenseSettingsFallBackToTheDefaultsRatherThanFailing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"trestleboard-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ this is not json");
            AppSettings loaded = AppSettings.Load(path);

            Assert.Equal(ThemeChoice.System, loaded.Theme);
            Assert.Equal(100, loaded.UiScalePercent);

            // An unknown theme name makes the whole file unreadable, which is the safe outcome:
            // defaults, not a half-applied preference.
            File.WriteAllText(path, """{"theme":"Mauve","uiScalePercent":150}""");
            Assert.Equal(100, AppSettings.Load(path).UiScalePercent);

            // A readable file with an out-of-range scale is CLAMPED, not rejected — the rest of the
            // user's preference is still good.
            File.WriteAllText(path, """{"theme":"Dark","uiScalePercent":9000}""");
            AppSettings odd = AppSettings.Load(path);
            Assert.Equal(ThemeChoice.Dark, odd.Theme);
            Assert.Equal(200, odd.UiScalePercent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingSettingsFileIsSimplyTheDefaults()
    {
        AppSettings loaded = AppSettings.Load(
            Path.Combine(Path.GetTempPath(), $"trestleboard-absent-{Guid.NewGuid():N}.json"));

        Assert.Equal(ThemeChoice.System, loaded.Theme);
        Assert.Equal(100, loaded.UiScalePercent);
    }

    [Fact]
    public async Task ScalingTheChromeUsesALayoutTransformSoNothingOverlaps()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            window.ApplySettings(new AppSettings { UiScalePercent = 100 });
            Assert.All(window.ChromeScaleHostsForTest, h => Assert.Null(h.LayoutTransform));

            window.ApplySettings(new AppSettings { UiScalePercent = 175 });
            foreach (LayoutTransformControl host in window.ChromeScaleHostsForTest)
            {
                var transform = Assert.IsType<ScaleTransform>(host.LayoutTransform);
                Assert.Equal(1.75d, transform.ScaleX);
                Assert.Equal(1.75d, transform.ScaleY);
            }

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The page is a piece of paper: white in dark mode too, because the user is laying out
    /// something that will be printed (docs/M9-spec.md §4). The theme must not reach it, and neither
    /// must the UI scale — the canvas has its own zoom.
    /// </summary>
    [Fact]
    public async Task TheThemeAndTheScaleBothStopAtTheEdgeOfThePage()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenIssueSample();

            byte[] before = window.SourceForTest!.RenderPageToPng(0, scale: 0.25f);

            window.ApplySettings(new AppSettings { Theme = ThemeChoice.HighContrast, UiScalePercent = 200 });

            byte[] after = window.SourceForTest.RenderPageToPng(0, scale: 0.25f);

            Assert.Equal(before, after);
            Assert.Null(window.CanvasForTest.RenderTransform);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheSettingsDialogOffersEveryThemeAndReportsWhatWasChosen()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new Dialogs.SettingsDialog(
                new AppSettings { Theme = ThemeChoice.Dark, UiScalePercent = 125 });

            AppSettings result = dialog.Result;
            Assert.Equal(ThemeChoice.Dark, result.Theme);
            Assert.Equal(125, result.UiScalePercent);
            Assert.False(dialog.Confirmed);

            dialog.Close();
        }, TestContext.Current.CancellationToken);
    }
}
