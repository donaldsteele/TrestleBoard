using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using TrestleBoard.App.Integration;
using TrestleBoard.App.Startup;
using TrestleBoard.App.Updates;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Templates;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M10 — the install-side behaviour (docs/M10-spec.md). None of this is visible until someone runs
/// an installer, which is exactly why it is pinned down here: the file association, the
/// double-click path into the app, and an auto-update that can never interrupt the work.
/// </summary>
public sealed class PackagingTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    // ---- Command line (docs/M10-spec.md §3) --------------------------------------------------

    [Fact]
    public void ADoubleClickedNewsletterArrivesAsAnArgument()
    {
        StartupOptions options = StartupOptions.Parse([@"C:\Users\Someone\July 2026.tboard"]);

        Assert.Equal(@"C:\Users\Someone\July 2026.tboard", options.DocumentPath);
        Assert.False(options.SkipUpdateCheck);
    }

    /// <summary>
    /// Velopack passes its own switches through the same argv, and a shell can pass anything at
    /// all. An argument the app does not understand must never stop it from starting.
    /// </summary>
    [Fact]
    public void UnknownArgumentsAreIgnoredRatherThanFatal()
    {
        StartupOptions options = StartupOptions.Parse(
            ["--veloapp-install", "1.2.3", "", "--squirrel-firstrun", "/tmp/august.tboard", "extra.pdf"]);

        Assert.Equal("/tmp/august.tboard", options.DocumentPath);

        Assert.Null(StartupOptions.Parse(["--veloapp-updated", "notes.txt"]).DocumentPath);
        Assert.Null(StartupOptions.Parse([]).DocumentPath);
        Assert.Null(StartupOptions.Parse(null).DocumentPath);
    }

    [Fact]
    public void TheFirstNewsletterWinsAndTheUpdateSwitchIsUnderstood()
    {
        StartupOptions options = StartupOptions.Parse(["--no-update-check", "one.TBOARD", "two.tboard"]);

        Assert.Equal("one.TBOARD", options.DocumentPath);
        Assert.True(options.SkipUpdateCheck);
    }

    // ---- File association (docs/M10-spec.md §3) ----------------------------------------------

    /// <summary>
    /// Lodge machines keep documents under "My Documents" and the app under "Program Files".
    /// An unquoted command line loses everything after the first space in either path, which shows
    /// up as "TrestleBoard cannot find C:\Users\Don\My" — a bug report nobody can act on.
    /// </summary>
    [Fact]
    public void TheWindowsOpenCommandQuotesBothThePathAndTheFile()
    {
        string command = FileAssociations.WindowsOpenCommand(@"C:\Program Files\TrestleBoard\TrestleBoard.App.exe");

        Assert.Equal(@"""C:\Program Files\TrestleBoard\TrestleBoard.App.exe"" ""%1""", command);
    }

    [Fact]
    public void TheLinuxAssociationDeclaresTheTypeAndPassesOneLocalFile()
    {
        string desktop = FileAssociations.DesktopEntry("/opt/trestleboard/TrestleBoard.App");
        string mime = FileAssociations.MimePackageXml();

        Assert.Contains("Exec=\"/opt/trestleboard/TrestleBoard.App\" %f", desktop, StringComparison.Ordinal);
        Assert.Contains($"MimeType={FileAssociations.MimeType};", desktop, StringComparison.Ordinal);
        Assert.Contains($"<mime-type type=\"{FileAssociations.MimeType}\">", mime, StringComparison.Ordinal);
        Assert.Contains("<glob pattern=\"*.tboard\"/>", mime, StringComparison.Ordinal);

        // %F (many files) or %u (a URL) would hand the app something it cannot open.
        Assert.DoesNotContain("%F", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("%u", desktop, StringComparison.Ordinal);
    }

    // ---- Auto-update (docs/M10-spec.md §2) ---------------------------------------------------

    private sealed class FakeChannel : IUpdateChannel
    {
        public bool IsInstalled { get; init; } = true;

        public string? Available { get; init; }

        public Exception? Throws { get; init; }

        public int Downloads { get; private set; }

        public int Applies { get; private set; }

        public Task<string?> CheckAsync(CancellationToken cancellationToken) =>
            Throws is null ? Task.FromResult(Available) : Task.FromException<string?>(Throws);

        public Task DownloadAsync(CancellationToken cancellationToken)
        {
            Downloads++;
            return Task.CompletedTask;
        }

        public void ApplyOnExit() => Applies++;
    }

    [Fact]
    public async Task AnUpdateIsDownloadedQuietlyAndOnlyInstalledOnTheWayOut()
    {
        var channel = new FakeChannel { Available = "1.4.0" };
        var coordinator = new UpdateCoordinator(channel);

        UpdateOutcome outcome = await coordinator.CheckAsync(userAsked: false, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Ready, outcome.State);
        Assert.Contains("1.4.0", outcome.Message, StringComparison.Ordinal);
        Assert.Contains("close", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(outcome.Announce);
        Assert.Equal(1, channel.Downloads);

        // Nothing is installed while the user is still working.
        Assert.Equal(0, channel.Applies);
        coordinator.ApplyIfReady();
        Assert.Equal(1, channel.Applies);
    }

    /// <summary>
    /// A background check that finds nothing says nothing. Filling the status bar with "no update"
    /// noise teaches the user to stop reading the one line the app uses to talk to them.
    /// </summary>
    [Fact]
    public async Task AQuietCheckStaysQuietButTheMenuItemAlwaysAnswers()
    {
        var upToDate = new UpdateCoordinator(new FakeChannel());
        Assert.False((await upToDate.CheckAsync(userAsked: false, TestContext.Current.CancellationToken)).Announce);

        UpdateOutcome asked = await upToDate.CheckAsync(userAsked: true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.UpToDate, asked.State);
        Assert.True(asked.Announce);
    }

    [Fact]
    public async Task ABrokenNetworkNeverInterruptsTheWork()
    {
        var coordinator = new UpdateCoordinator(new FakeChannel { Throws = new HttpRequestException("no route to host") });

        UpdateOutcome quiet = await coordinator.CheckAsync(userAsked: false, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Failed, quiet.State);
        Assert.False(quiet.Announce);
        Assert.False(coordinator.UpdateReady);

        UpdateOutcome asked = await coordinator.CheckAsync(userAsked: true, TestContext.Current.CancellationToken);
        Assert.True(asked.Announce);
        Assert.Contains("keep working", asked.Message, StringComparison.OrdinalIgnoreCase);

        // Nothing was downloaded, so closing the app must not try to install anything.
        coordinator.ApplyIfReady();
    }

    [Fact]
    public async Task ACopyThatWasNeverInstalledSaysSoInsteadOfPretending()
    {
        var coordinator = new UpdateCoordinator(new FakeChannel { IsInstalled = false, Available = "9.9.9" });

        UpdateOutcome outcome = await coordinator.CheckAsync(userAsked: true, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.NotInstalled, outcome.State);
        Assert.True(outcome.Announce);
        Assert.False(coordinator.UpdateReady);
    }

    // ---- The shell (docs/M10-spec.md §2/§3) --------------------------------------------------

    [Fact]
    public async Task DoubleClickingANewsletterOpensItInsteadOfTheStartScreen()
    {
        string path = Path.Combine(Path.GetTempPath(), $"trestleboard-{Guid.NewGuid():N}.tboard");
        using (var file = File.Create(path))
        {
            TboardContainer.Save(TemplateLibrary.Create(TemplateLibrary.All[1].Id), file);
        }

        try
        {
            await Session.Dispatch(() =>
            {
                var window = new MainWindow { StartupOptions = new StartupOptions(path, SkipUpdateCheck: true) };

                Assert.True(window.OpenDocumentFromPath(path));
                Assert.NotNull(window.PackageForTest);
                Assert.True(window.SourceForTest!.PageCount > 0);

                window.Close();
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The association can outlive the file it points at — a newsletter on a USB stick that is no
    /// longer plugged in, say. The app says so and stays up; it does not fail to start.
    /// </summary>
    [Fact]
    public async Task AMissingOrUnreadableNewsletterIsAMessageNotACrash()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"trestleboard-gone-{Guid.NewGuid():N}.tboard");
        string junk = Path.Combine(Path.GetTempPath(), $"trestleboard-junk-{Guid.NewGuid():N}.tboard");
        File.WriteAllText(junk, "this is not a zip container");

        try
        {
            await Session.Dispatch(() =>
            {
                var window = new MainWindow();

                Assert.False(window.OpenDocumentFromPath(missing));
                Assert.Contains("could not open", window.StatusLabelTextForTest!, StringComparison.OrdinalIgnoreCase);

                Assert.False(window.OpenDocumentFromPath(junk));
                Assert.Contains("could not open", window.StatusLabelTextForTest!, StringComparison.OrdinalIgnoreCase);
                Assert.Null(window.PackageForTest);

                window.Close();
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(junk);
        }
    }

    /// <summary>
    /// "Am I on the newest one?" must have an answer that does not involve a web browser — and like
    /// every other command in this app it has to be reachable from the keyboard (PLAN.md §6).
    /// </summary>
    [Fact]
    public async Task HelpOffersAnUpdateCheckThatReportsBackToTheStatusBar()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow();
            window.OpenSample();

            MenuItem check = window.GetLogicalDescendants().OfType<MenuItem>()
                .Single(m => m.Name == "CheckForUpdatesMenuItem");
            Assert.True(check.IsEnabled);
            Assert.Equal(
                "Check for an update",
                Avalonia.Automation.AutomationProperties.GetName(check));

            window.UseUpdateChannelForTest(new FakeChannel { Available = "2.0.0" });
            await window.CheckForUpdatesForTest(userAsked: true);

            Assert.Contains("2.0.0", window.StatusLabelTextForTest!, StringComparison.Ordinal);
            Assert.True(window.UpdatesForTest!.UpdateReady);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
