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

    /// <summary>
    /// Runs an ASYNC body on the UI thread, and — the whole point — actually waits for it.
    ///
    /// <para><b>M39. Never write <c>await Session.Dispatch(async () =&gt; …)</c> again.</b> Avalonia
    /// offers <c>Dispatch(Action)</c>, <c>Dispatch&lt;T&gt;(Func&lt;T&gt;)</c> and
    /// <c>Dispatch&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt;)</c> — and no <c>Func&lt;Task&gt;</c> overload
    /// at all. So an <c>async</c> lambda binds to the middle one with <c>T = Task</c>, the call
    /// returns <c>Task&lt;Task&gt;</c>, and awaiting it waits only for the lambda to reach its first
    /// suspension point. The inner task — the body, and every assertion in it — is dropped on the
    /// floor, exceptions and all.</para>
    ///
    /// <para>It was found because a test written to FAIL passed: the assertion proving the new
    /// backup ring was empty threw on the UI thread and vanished, and the test only failed later,
    /// outside the lambda, for a different reason. Eleven lambdas across six files were in that
    /// state.</para>
    ///
    /// <para>Awaiting the returned <c>Task&lt;Task&gt;</c> a second time is NOT the fix. Avalonia
    /// tears the <c>Application</c> down when the task it is given completes, so under the
    /// <c>Func&lt;T&gt;</c> overload teardown begins at the body's first <c>await</c> — the rest of
    /// the body then runs against a dead session, which is 115 failures deep in the font manager.
    /// Returning a value puts the call on the <c>Func&lt;Task&lt;T&gt;&gt;</c> overload instead,
    /// which is the one that awaits the body before tearing anything down.</para>
    /// </summary>
    public static async Task DispatchAsync(Func<Task> body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await Instance.Dispatch(
            async () =>
            {
                await body();
                return true;
            },
            cancellationToken);
    }
}
