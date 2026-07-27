using Avalonia;
using Avalonia.Headless;
using TrestleBoard.App;
using TrestleBoard.App.Settings;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>Headless AppBuilder: no compositor drawing (the snapshot suite proves pixels);
/// these tests exercise the shell's state machine.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>Avalonia can be initialized ONCE per process — every test class must share this
/// single session (two StartNew calls throw 'avares already registered').</summary>
public static class HeadlessSession
{
    private static readonly Lazy<HeadlessUnitTestSession> Lazy =
        new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static HeadlessUnitTestSession Instance => Lazy.Value;

    /// <summary>
    /// Every test in this suite runs against a temporary app-state root (M12). The point is not
    /// tidiness: from M12 <c>%AppData%/TrestleBoard</c> holds `roster.json`, which is real members'
    /// names, birthdays, telephone numbers and emails (PLAN.md §0 rule 5). A test suite that could
    /// read it could print it in an assertion message on a public CI log. Pointed at a temporary
    /// folder, it cannot — and the same settable root is what M15's screenshot harness will use.
    /// </summary>
    private static HeadlessUnitTestSession Start()
    {
        AppPaths.Root = Path.Combine(
            Path.GetTempPath(),
            "TrestleBoard-headless-tests",
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(AppPaths.Root);
        return HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
    }
}
