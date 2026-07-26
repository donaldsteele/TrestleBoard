using Avalonia;
using Avalonia.Headless;
using TrestleBoard.App;

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
        new(() => HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder)), LazyThreadSafetyMode.ExecutionAndPublication);

    public static HeadlessUnitTestSession Instance => Lazy.Value;
}
