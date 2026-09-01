using Avalonia.Threading;

namespace TrestleBoard.App.Diagnostics;

/// <summary>
/// The last thing standing between an unhandled exception and a window that vanishes (PLAN.md §11
/// M77).
///
/// <para><b>The order is the feature.</b> The work is kept FIRST, before a card is drawn, before a
/// file is written, before anything at all is shown. Everything else in this class can fail without
/// costing the committee their evening; that one step cannot, so it goes first and it goes inside
/// its own guard. A crash handler that drew a beautiful dialog and then discovered it could not
/// save would be worse than no crash handler, because the user would have read a promise.</para>
///
/// <para><b>Three doors, one room.</b> An exception can reach the top three ways in an Avalonia
/// app: off the dispatcher (a click handler, a timer tick), off a task nobody awaited, or off a
/// plain background thread. They are wired separately and all land in <see cref="Handle"/>, so
/// there is one behaviour to get right and one behaviour to test.</para>
///
/// <para><b>What it does not do is pretend.</b> A dispatcher exception is caught and the app keeps
/// running, because Avalonia lets it. An <see cref="AppDomain.UnhandledException"/> is the runtime
/// on its way out and nothing here can stop that — the work is written and the card is shown, and
/// then the process ends the way it was going to. The card says which of the two happened, in
/// words, rather than claiming the app is fine when it is about to disappear.</para>
/// </summary>
internal sealed class CrashGuard
{
    private readonly Action _keepTheWork;
    private readonly Action<CrashReport> _showTheCard;
    private readonly Lock _gate = new();
    private bool _busy;

    /// <summary>
    /// What went wrong, and whether the app can carry on afterwards.
    /// </summary>
    /// <param name="Error">The exception, for the report.</param>
    /// <param name="AppWillClose">
    /// True when the runtime is on its way out regardless — the card must not offer to carry on.
    /// </param>
    internal sealed record CrashReport(Exception Error, bool AppWillClose);

    /// <param name="keepTheWork">
    /// Writes the recovery snapshot. Called first, always, and its failure is swallowed: the card
    /// is what tells the user, and a second exception in here would be the handler crashing.
    /// </param>
    /// <param name="showTheCard">Puts the plain card in front of the user.</param>
    internal CrashGuard(Action keepTheWork, Action<CrashReport> showTheCard)
    {
        _keepTheWork = keepTheWork ?? throw new ArgumentNullException(nameof(keepTheWork));
        _showTheCard = showTheCard ?? throw new ArgumentNullException(nameof(showTheCard));
    }

    /// <summary>How many times the guard has run. The tests assert on this rather than on a log.</summary>
    internal int TimesCaught { get; private set; }

    /// <summary>
    /// Wires the three doors. Called once, from the shell, at startup.
    ///
    /// <para>Not called by the headless tests: they drive <see cref="Handle"/> directly, because a
    /// test that installs a process-wide exception handler affects every other test in the
    /// assembly, and because what is worth proving here is the ORDER of the two steps rather than
    /// whether the .NET runtime raises its own events.</para>
    /// </summary>
    internal void Install()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            // Handled = true keeps the app alive. Avalonia's default is to let it tear down, which
            // for this audience means the window disappearing mid-sentence with nothing said.
            Handle(new CrashReport(e.Exception, AppWillClose: false));
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Handle(new CrashReport(e.Exception, AppWillClose: false));
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            // Nothing here can stop this one. The work still gets written, which is the whole
            // reason to be in this handler at all.
            Exception error = e.ExceptionObject as Exception
                ?? new InvalidOperationException("TrestleBoard stopped for a reason it could not describe.");
            Handle(new CrashReport(error, AppWillClose: true));
        };
    }

    /// <summary>
    /// Keep the work, then say so. In that order, and once at a time.
    ///
    /// <para><b>Re-entrancy is refused rather than queued.</b> If drawing the card throws — and the
    /// card is drawn while the app is already in trouble, so it might — the dispatcher hands that
    /// second exception straight back here. Without the gate that is an infinite loop of cards, and
    /// the user's last sight of TrestleBoard is a stack of identical windows.</para>
    /// </summary>
    internal void Handle(CrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        lock (_gate)
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
        }

        try
        {
            TimesCaught++;

            try
            {
                _keepTheWork();
            }
            catch (Exception)
            {
                // Deliberately swallowed and deliberately broad. Every narrower catch here is a
                // guess about what can go wrong while the process is already failing, and being
                // wrong about that guess costs the user the card as well as the snapshot.
            }

            try
            {
                _showTheCard(report);
            }
            catch (Exception)
            {
                // Same reasoning. There is nowhere left to report a failure to report a failure.
            }
        }
        finally
        {
            lock (_gate)
            {
                _busy = false;
            }
        }
    }
}
