using Velopack;
using Velopack.Sources;

namespace TrestleBoard.App.Updates;

/// <summary>
/// The real feed: GitHub Releases of the project repository (PLAN.md §1 — "Velopack … feeds from
/// GitHub Releases"). Releases are public, so no token is needed; pre-releases are ignored, because
/// the committee should only ever be offered something that was deliberately published.
/// </summary>
public sealed class VelopackUpdateChannel : IUpdateChannel
{
    public const string RepositoryUrl = "https://github.com/donaldsteele/TrestleBoard";

    private readonly UpdateManager _manager =
        new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

    private UpdateInfo? _pending;

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        _pending = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return _pending?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(CancellationToken cancellationToken) =>
        _pending is null
            ? Task.CompletedTask
            : _manager.DownloadUpdatesAsync(_pending, cancelToken: cancellationToken);

    /// <summary>
    /// Stages the update rather than applying it now: <c>restart: false</c> means the installer runs
    /// after this process exits and the user's next launch is the new version. Nobody's window
    /// disappears mid-edit.
    /// </summary>
    public void ApplyOnExit()
    {
        if (_pending is not null)
        {
            _manager.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: true, restart: false);
        }
    }
}
