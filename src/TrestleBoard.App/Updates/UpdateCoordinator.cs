namespace TrestleBoard.App.Updates;

/// <summary>What a check found (docs/M10-spec.md §2).</summary>
public enum UpdateState
{
    /// <summary>Running from a build output or a copy that was never installed — nothing to update.</summary>
    NotInstalled,
    UpToDate,

    /// <summary>Downloaded and waiting; it is applied when the user closes the app.</summary>
    Ready,
    Failed,
}

/// <param name="Announce">
/// True when the user should be told. A background check that finds nothing says nothing — the
/// status bar is where the app talks to the user, and filling it with "no update" noise trains
/// them to ignore it.
/// </param>
public sealed record UpdateOutcome(UpdateState State, string Message, bool Announce);

/// <summary>The update feed, behind an interface so the shell's wiring is testable without a network.</summary>
public interface IUpdateChannel
{
    /// <summary>False when the app is not running from a Velopack install (dev runs, tests, CI).</summary>
    bool IsInstalled { get; }

    /// <summary>Returns the newer version's number, or null when there is nothing newer.</summary>
    Task<string?> CheckAsync(CancellationToken cancellationToken);

    Task DownloadAsync(CancellationToken cancellationToken);

    /// <summary>Stages the downloaded update to be applied once this process exits.</summary>
    void ApplyOnExit();
}

/// <summary>
/// Auto-update, arranged so it can never interrupt the work (PLAN.md §11-M10, docs/M10-spec.md §2):
/// the check and the download happen quietly in the background, and the update is only *applied*
/// when the user closes the app. Nothing restarts under the user's hands mid-sentence.
/// </summary>
public sealed class UpdateCoordinator(IUpdateChannel channel)
{
    private readonly IUpdateChannel _channel = channel;

    /// <summary>True once an update is downloaded and waiting for the app to close.</summary>
    public bool UpdateReady { get; private set; }

    /// <summary>The version that is waiting, for the message the user sees.</summary>
    public string? ReadyVersion { get; private set; }

    /// <param name="userAsked">
    /// True for Help → "Check for an update", which must always answer — a button that appears to
    /// do nothing is worse than no button.
    /// </param>
    public async Task<UpdateOutcome> CheckAsync(bool userAsked, CancellationToken cancellationToken = default)
    {
        if (UpdateReady)
        {
            return new UpdateOutcome(UpdateState.Ready, ReadyMessage(ReadyVersion), Announce: true);
        }

        if (!_channel.IsInstalled)
        {
            return new UpdateOutcome(
                UpdateState.NotInstalled,
                "This copy of TrestleBoard was not installed by the TrestleBoard installer, so it "
                    + "cannot update itself.",
                userAsked);
        }

        try
        {
            string? version = await _channel.CheckAsync(cancellationToken).ConfigureAwait(true);
            if (version is null)
            {
                return new UpdateOutcome(
                    UpdateState.UpToDate, "TrestleBoard is up to date.", userAsked);
            }

            await _channel.DownloadAsync(cancellationToken).ConfigureAwait(true);
            UpdateReady = true;
            ReadyVersion = version;
            return new UpdateOutcome(UpdateState.Ready, ReadyMessage(version), Announce: true);
        }
        catch (OperationCanceledException)
        {
            return new UpdateOutcome(UpdateState.Failed, FailureMessage, Announce: false);
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // Any failure here — no network, GitHub down, a half-written download — is a nuisance,
            // never a reason to interrupt someone laying out a newsletter.
            return new UpdateOutcome(UpdateState.Failed, FailureMessage, userAsked);
        }
    }

    /// <summary>Called from the window's Closed handler; a no-op unless something is waiting.</summary>
    public void ApplyIfReady()
    {
        if (!UpdateReady)
        {
            return;
        }

        try
        {
            _channel.ApplyOnExit();
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // The app is closing. A failed update install here is picked up by the next check.
        }
    }

    private const string FailureMessage =
        "TrestleBoard could not check for an update just now. It will try again next time. "
        + "You can keep working.";

    private static string ReadyMessage(string? version) =>
        version is null
            ? "An update to TrestleBoard is ready. It will finish installing when you close the app."
            : $"TrestleBoard {version} is ready. It will finish installing when you close the app.";
}
