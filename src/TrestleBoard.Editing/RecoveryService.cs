using System.Globalization;
using TrestleBoard.Core.Commands;

namespace TrestleBoard.Editing;

/// <summary>What a recovery snapshot needs alongside the document bytes.</summary>
public sealed record RecoverySnapshot(
    string Id,
    byte[] Bytes,
    string? OriginalPath,
    DateTimeOffset SavedAt);

/// <summary>
/// Where recovery snapshots live. Abstracted so the timing rules can be tested without a disk, and
/// so the atomic-write rule has exactly one implementation to get right.
/// </summary>
public interface IRecoveryStore
{
    /// <summary>
    /// Writes a snapshot ATOMICALLY — temp file, flush, rename. Never writes the destination
    /// directly: a crash mid-write would otherwise leave a truncated archive, turning "your work is
    /// safe" into "your work is safe but the file is broken" (docs/M9-spec.md §1.2).
    /// </summary>
    void Write(RecoverySnapshot snapshot);

    /// <summary>Removes the snapshot; called on a clean close, when there is nothing to recover.</summary>
    void Delete(string id);

    /// <summary>Snapshots that survived a previous run — i.e. the app did not close cleanly.</summary>
    IReadOnlyList<RecoverySnapshot> FindRecoverable();
}

/// <summary>
/// Autosave and crash recovery (PLAN.md §4, docs/M9-spec.md §1). Headless and clock-injected, so
/// every timing rule is testable without sleeping and without a window.
/// </summary>
public sealed class RecoveryService : IDisposable
{
    /// <summary>Longest a continuous typist can go unprotected. The acceptance bound is 60s.</summary>
    public static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(60);

    /// <summary>Quiet for this long and the work is written immediately — the common case.</summary>
    public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(5);

    private readonly DocumentSession _session;
    private readonly IRecoveryStore _store;
    private readonly Func<RecoveryPayload> _payloadFactory;
    private readonly TimeProvider _time;
    private readonly string _id;

    private long? _lastEditTimestamp;
    private long? _dirtySinceTimestamp;
    private long? _lastWriteTimestamp;
    private bool _dirty;
    private bool _disposed;

    public RecoveryService(
        DocumentSession session,
        IRecoveryStore store,
        Func<RecoveryPayload> payloadFactory,
        string? id = null,
        TimeProvider? time = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _payloadFactory = payloadFactory ?? throw new ArgumentNullException(nameof(payloadFactory));
        _time = time ?? TimeProvider.System;
        _id = id ?? "session";
        _session.Changed += OnDocumentChanged;
    }

    /// <summary>The bytes to write and where the document came from, produced on demand.</summary>
    public sealed record RecoveryPayload(byte[] Bytes, string? OriginalPath);

    /// <summary>True when there are edits the recovery file does not yet hold.</summary>
    public bool HasUnsavedWork => _dirty;

    /// <summary>Snapshots actually written. The tests assert on this rather than on file mtimes.</summary>
    public int SaveCount { get; private set; }

    /// <summary>Raised after a snapshot is written, for the status line.</summary>
    public event EventHandler? Saved;

    /// <summary>
    /// Call on a tick. Writes only when a rule says it is time — idle for 5s, or 60s since the last
    /// write. An untouched document writes NOTHING, so a recovery file's timestamp keeps meaning
    /// "when the work was done".
    /// </summary>
    public bool Poll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_dirty)
        {
            return false;
        }

        long now = _time.GetTimestamp();
        bool idle = _lastEditTimestamp is { } lastEdit && Elapsed(lastEdit, now) >= IdleInterval;

        // The cap is measured from whichever came last — the previous write, or the edit that made
        // the document dirty. Measuring from "never written" would fire on the first poll and defeat
        // the idle rule entirely.
        long since = _lastWriteTimestamp ?? _dirtySinceTimestamp ?? now;
        bool overdue = Elapsed(since, now) >= MaxInterval;

        if (!idle && !overdue)
        {
            return false;
        }

        WriteSnapshot(now);
        return true;
    }

    /// <summary>Writes immediately regardless of the timers — used when the window is closing dirty.</summary>
    public bool SaveNow()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_dirty)
        {
            return false;
        }

        WriteSnapshot(_time.GetTimestamp());
        return true;
    }

    /// <summary>
    /// A clean close: the snapshot is deleted. A file surviving startup therefore MEANS the app did
    /// not close cleanly — no heuristics, no guessing at whether the document was modified.
    /// </summary>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _store.Delete(_id);
        _dirty = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Changed -= OnDocumentChanged;
    }

    /// <summary>Plain-language "when" for the restore dialog.</summary>
    public static string DescribeAge(DateTimeOffset savedAt, DateTimeOffset now)
    {
        TimeSpan age = now - savedAt;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "less than a minute ago";
        }

        if (age < TimeSpan.FromHours(1))
        {
            int minutes = (int)age.TotalMinutes;
            return string.Create(CultureInfo.InvariantCulture, $"{minutes} minute{(minutes == 1 ? "" : "s")} ago");
        }

        if (age < TimeSpan.FromDays(1))
        {
            int hours = (int)age.TotalHours;
            return string.Create(CultureInfo.InvariantCulture, $"{hours} hour{(hours == 1 ? "" : "s")} ago");
        }

        return savedAt.ToString("d MMMM, HH:mm", CultureInfo.InvariantCulture);
    }

    private void WriteSnapshot(long now)
    {
        RecoveryPayload payload = _payloadFactory();
        _store.Write(new RecoverySnapshot(
            _id,
            payload.Bytes,
            payload.OriginalPath,
            _time.GetUtcNow()));

        _lastWriteTimestamp = now;
        _dirtySinceTimestamp = null;
        _dirty = false;
        SaveCount++;
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        long now = _time.GetTimestamp();
        _dirtySinceTimestamp ??= now;
        _dirty = true;
        _lastEditTimestamp = now;
    }

    private TimeSpan Elapsed(long from, long to) =>
        TimeSpan.FromSeconds((to - from) / (double)_time.TimestampFrequency);
}
