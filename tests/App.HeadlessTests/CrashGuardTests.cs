using Avalonia.Headless;
using TrestleBoard.App.Diagnostics;
using TrestleBoard.App.Settings;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M77: the app keeps its footing. Until this milestone there was no unhandled-exception handler
/// anywhere in <c>src/</c>, no log and no way for a committee member to say what happened.
///
/// <para>The tests drive <see cref="CrashGuard.Handle"/> rather than throwing for real, on purpose:
/// a test that installs a process-wide handler outlives itself and changes how every other test in
/// the assembly fails. What is worth proving is the ORDER of the two steps, which a real throw
/// would tell us nothing extra about.</para>
/// </summary>
public sealed class CrashGuardTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>Words the sample newsletter is full of. None of them may reach a report.</summary>
    private static readonly string[] SampleWords = ["Worshipful", "Lodge 414", "Placeholder", "Brother"];

    /// <summary>
    /// <b>The order is the feature.</b> The recovery snapshot is written BEFORE the card is drawn,
    /// because everything about drawing a card can fail while the process is on its way out, and a
    /// handler that showed a beautiful dialog and then discovered it could not save would have made
    /// the user a promise it could not keep.
    /// </summary>
    [Fact]
    public void TheWorkIsKeptBeforeTheCardIsShown()
    {
        var order = new List<string>();
        var guard = new CrashGuard(
            () => order.Add("kept"),
            _ => order.Add("card"));

        guard.Handle(new CrashGuard.CrashReport(new InvalidOperationException("boom"), false));

        Assert.Equal(["kept", "card"], order);
    }

    /// <summary>
    /// A snapshot that cannot be written must not cost the user the card as well. The guard
    /// swallows what the first step throws — this is what proves the second step still runs.
    /// </summary>
    [Fact]
    public void TheCardIsStillShownWhenTheWorkCouldNotBeKept()
    {
        var shown = 0;
        var guard = new CrashGuard(
            () => throw new IOException("the disk is full"),
            _ => shown++);

        guard.Handle(new CrashGuard.CrashReport(new InvalidOperationException("boom"), false));

        Assert.Equal(1, shown);
    }

    /// <summary>
    /// Drawing the card happens while the app is already in trouble, so it might throw — and the
    /// dispatcher hands that second exception straight back to the guard. Without the re-entrancy
    /// gate the user's last sight of TrestleBoard is a stack of identical windows.
    /// </summary>
    [Fact]
    public void ASecondCrashInsideTheCardDoesNotLoop()
    {
        CrashGuard? guard = null;
        var shown = 0;
        guard = new CrashGuard(
            () => { },
            report =>
            {
                shown++;
                guard!.Handle(report);
            });

        guard.Handle(new CrashGuard.CrashReport(new InvalidOperationException("boom"), false));

        Assert.Equal(1, shown);
    }

    /// <summary>
    /// The shell's half: the snapshot is taken from the open newsletter and the card records what
    /// it was told, without a window being drawn in a headless session.
    /// </summary>
    [Fact]
    public async Task TheShellCatchesAndKeepsTheWork()
    {
        await Session.Dispatch(
            () =>
            {
                var window = new MainWindow { SwallowErrorsForTest = true };
                window.OpenSample();

                window.SimulateCrashForTest(new InvalidOperationException("something gave way"));

                Assert.NotNull(window.LastCrashForTest);
                Assert.True(window.LastCrashKeptTheWorkForTest);
            },
            TestContext.Current.CancellationToken);
    }

    // ---- The report ---------------------------------------------------------------------------

    /// <summary>
    /// PLAN.md §0, made structural. A report is composed from a session whose home directory and
    /// documents folder appear in the exception text, and neither may survive into the file.
    ///
    /// <para>This is the failure the milestone exists to prevent: a .NET exception message from a
    /// failed open carries the path it failed to open, and a path under Documents names a person, a
    /// computer and a newsletter all at once.</para>
    /// </summary>
    [Fact]
    public void TheReportCarriesNoPathUnderTheUsersOwnFolders()
    {
        // Only the folders this machine actually reports.
        //
        // The first version of this test named UserProfile and MyDocuments outright and asserted
        // the report contained neither. It passed on Windows, where the five special folders are
        // all distinct and non-empty, and FAILED THE RELEASE on the Linux runner, where
        // MyDocuments comes back empty — and Assert.DoesNotContain("") fails against every string
        // there is. The assertion was about the runner's environment rather than about the
        // scrubber. This one asks the machine what it has and holds the scrubber to that.
        string[] folders =
        [
            .. new[]
            {
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.DesktopDirectory,
            }
            .Select(Environment.GetFolderPath)
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase),
        ];

        Assert.NotEmpty(folders);

        // An exception message naming a file under each of them — which is what a failed open
        // really carries, and what names a person, a computer and a newsletter all at once.
        var error = new IOException(string.Join(
            " ",
            folders.Select(folder =>
                $"Could not open {Path.Combine(folder, "September Trestle Board.tboard")}.")));

        string report = ProblemReport.Compose(
            new ProblemReportFacts("1.2.3", "Windows", 150, "Dark", error, []),
            DateTimeOffset.UnixEpoch);

        foreach (string folder in folders)
        {
            Assert.DoesNotContain(folder, report, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(ProblemReport.Redacted, report, StringComparison.Ordinal);
    }

    /// <summary>
    /// The scrubber itself, over a path shape rather than over whatever this machine happens to
    /// have — so the rule is checked identically on all three operating systems.
    /// </summary>
    [Fact]
    public void TheScrubberRemovesTheHomeDirectoryWhereverItAppears()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrWhiteSpace(home), "every supported OS reports a home directory");

        string scrubbed = ProblemReport.Scrub(
            $"at TrestleBoard.App.MainWindow.SaveAsync() in {Path.Combine(home, "src", "MainWindow.cs")}:line 12");

        Assert.DoesNotContain(home, scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ProblemReport.Redacted, scrubbed, StringComparison.Ordinal);

        // And the part that is not a path survives, or the report says nothing useful either.
        Assert.Contains("SaveAsync", scrubbed, StringComparison.Ordinal);
        Assert.Contains("line 12", scrubbed, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the privacy rule, and the one a reader would check by eye: a report built
    /// during a session that has a newsletter open and an address book loaded says nothing about
    /// either. It cannot, because there is nowhere in <see cref="ProblemReportFacts"/> to put them
    /// — this test is what stops a later edit adding somewhere.
    /// </summary>
    [Fact]
    public async Task TheReportSaysNothingAboutTheNewsletterOrTheMembers()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow { SwallowErrorsForTest = true };
                window.OpenSample();

                // The picker is not answered in a headless session, so this returns false — but the
                // text has already been composed and recorded, which is what is under test.
                await window.SaveAProblemReportAsync(new InvalidOperationException("boom"));

                string report = Assert.IsType<string>(window.LastProblemReportForTest);

                // Words the sample newsletter is full of. None of them may reach the file.
                foreach (string word in SampleWords)
                {
                    Assert.DoesNotContain(word, report, StringComparison.OrdinalIgnoreCase);
                }
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// What the report is FOR: the last few commands, so a maintainer can follow what led up to the
    /// trouble. Ids only — never a file name, never a word that was typed.
    /// </summary>
    [Fact]
    public void TheReportListsTheCommandsThatLedUpToIt()
    {
        var trail = new ActionTrail();
        trail.Record("newsletter.open", DateTimeOffset.UnixEpoch);
        trail.Record("picture.replace", DateTimeOffset.UnixEpoch.AddSeconds(30));

        string report = ProblemReport.Compose(
            new ProblemReportFacts("1.2.3", "Linux", 100, "System", null, trail.Recent(20)),
            DateTimeOffset.UnixEpoch);

        Assert.Contains("newsletter.open", report, StringComparison.Ordinal);
        Assert.Contains("picture.replace", report, StringComparison.Ordinal);
        Assert.Contains("Nothing crashed", report, StringComparison.Ordinal);
    }

    /// <summary>The ring holds the newest entries and drops the oldest, oldest-first on the way out.</summary>
    [Fact]
    public void TheTrailKeepsTheNewestAndForgetsTheRest()
    {
        var trail = new ActionTrail();
        for (int i = 0; i < ActionTrail.Capacity + 25; i++)
        {
            trail.Record($"action.{i}", DateTimeOffset.UnixEpoch.AddSeconds(i));
        }

        Assert.Equal(ActionTrail.Capacity, trail.Count);

        IReadOnlyList<TrailEntry> last3 = trail.Recent(3);
        Assert.Equal(
            ["action.222", "action.223", "action.224"],
            last3.Select(entry => entry.ActionId));
    }

    // ---- Where the window opens ----------------------------------------------------------------

    /// <summary>
    /// The rule that cannot be checked by looking at the app, because checking it means unplugging
    /// a monitor: a position on a screen that no longer exists is thrown away, or the user is left
    /// with a window they can neither see nor drag back.
    /// </summary>
    [Fact]
    public void APositionOnAScreenThatIsGoneIsRefused()
    {
        var oneScreen = new List<PlacementRect> { new(0, 0, 1920, 1080) };

        Assert.False(
            WindowPlacement.CanRestore(new PlacementRect(2400, 100, 1280, 860), oneScreen),
            "a window wholly on the second monitor must not be restored when it is unplugged");

        Assert.False(
            WindowPlacement.CanRestore(new PlacementRect(1880, 100, 1280, 860), oneScreen),
            "a sliver of title bar is not enough to take hold of");

        Assert.True(
            WindowPlacement.CanRestore(new PlacementRect(-100, 40, 1280, 860), oneScreen),
            "a window nudged a little off the left edge is where the user put it");
    }

    /// <summary>A headless session reports no screens at all, and that means the default.</summary>
    [Fact]
    public void NoScreensMeansTheDefaultPlace() =>
        Assert.False(WindowPlacement.CanRestore(new PlacementRect(0, 0, 1280, 860), []));

    /// <summary>
    /// A remembered size below what the chrome needs is treated as damage, not as a preference —
    /// and never clamped to something in between, because a size the user has never seen is not a
    /// preference either.
    /// </summary>
    [Fact]
    public void ASizeTooSmallToUseIsReplacedRatherThanClamped()
    {
        Assert.Equal(
            (WindowPlacement.DefaultWidth, WindowPlacement.DefaultHeight),
            WindowPlacement.ChooseSize(320, 240));

        Assert.Equal(
            (WindowPlacement.DefaultWidth, WindowPlacement.DefaultHeight),
            WindowPlacement.ChooseSize(null, null));

        Assert.Equal((1600, 1000), WindowPlacement.ChooseSize(1600, 1000));
    }

    /// <summary>
    /// M76 fixed a toolbar that overflowed at 1280 because somebody at 200% had been maximising the
    /// window at every launch for months. This is the other half of that finding: the app now holds
    /// the size they chose.
    /// </summary>
    [Fact]
    public void TheWindowOpensAtTheSizeItWasLeftAt()
    {
        var settings = new AppSettings { WindowWidth = 1600, WindowHeight = 1000 };
        AppSettings roundTripped = RoundTrip(settings);

        Assert.Equal(1600, roundTripped.WindowWidth);
        Assert.Equal(1000, roundTripped.WindowHeight);
        Assert.Equal((1600, 1000), WindowPlacement.ChooseSize(roundTripped.WindowWidth, roundTripped.WindowHeight));
    }

    /// <summary>
    /// A maximised window keeps the size it had BEFORE it was maximised. Writing the whole screen
    /// there would mean that un-maximising next time landed on a window the size of the monitor,
    /// which is not a size the user ever chose.
    ///
    /// <para><b>This test exists in this shape because the first one was worthless.</b> It maximised
    /// a headless window and asserted on what the shell wrote — but a headless window does not
    /// change its reported width when maximised, so the assertion held against code with the rule
    /// backwards. Checking WHERE a test fails, not just that it does, is the M39 lesson; this one
    /// could not fail at all. The rule moved to a pure function so that the test can say "the window
    /// reports the whole screen" without owning a screen.</para>
    /// </summary>
    [Fact]
    public void MaximisingDoesNotOverwriteTheSizeTheUserChose()
    {
        var chosen = new AppSettings { WindowWidth = 1400, WindowHeight = 900, WindowLeft = 60, WindowTop = 40 };

        // Maximised, and the window now reports the whole 4K screen.
        AppSettings after = WindowPlacement.Remember(chosen, maximised: true, 3840, 2160, 0, 0);

        Assert.True(after.WindowMaximised);
        Assert.Equal(1400, after.WindowWidth);
        Assert.Equal(900, after.WindowHeight);
        Assert.Equal(60, after.WindowLeft);
        Assert.Equal(40, after.WindowTop);

        // Not maximised: what the window reports IS the preference.
        AppSettings resized = WindowPlacement.Remember(chosen, maximised: false, 1600, 1000, 120, 80);
        Assert.False(resized.WindowMaximised);
        Assert.Equal(1600, resized.WindowWidth);
        Assert.Equal(1000, resized.WindowHeight);
        Assert.Equal(120, resized.WindowLeft);
    }

    private static AppSettings RoundTrip(AppSettings settings)
    {
        string path = Path.Combine(Path.GetTempPath(), $"tb-m77-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(settings.Save(path));
            return AppSettings.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
