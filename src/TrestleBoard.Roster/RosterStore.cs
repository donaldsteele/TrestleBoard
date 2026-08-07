using System.Globalization;
using System.Text.Json;

namespace TrestleBoard.Roster;

/// <summary>
/// Why <see cref="RosterStore.Load(out RosterLoadState)"/> returned what it did.
///
/// The distinction is the whole point (M24): an empty book because there is no file yet is the
/// ordinary first-run state, and saving over nothing is correct. An empty book because the file
/// could not be READ — antivirus holding it, a sync client mid-write, a permissions change — looks
/// identical to the caller, and saving over that destroys a real address book. The old
/// <see cref="RosterStore.Load()"/> collapsed both into <see cref="RosterBook.Empty"/>, so a
/// transient lock at startup became silent, permanent data loss on the next edit.
/// </summary>
public enum RosterLoadState
{
    /// <summary>The file was read. The book is what it held.</summary>
    Loaded,

    /// <summary>There is no address book yet — first run. An empty book is the truth.</summary>
    NoFileYet,

    /// <summary>A file is there and could not be read. The empty book is a placeholder, NOT the data.</summary>
    CouldNotBeRead,
}

/// <summary>One kept copy of the address book, as the "Restore an earlier version…" list shows it.</summary>
/// <param name="Path">Where the copy is.</param>
/// <param name="SavedAt">When it was taken, in UTC.</param>
/// <param name="MemberCount">How many people were in it — the fact that tells a user which one they want.</param>
public sealed record RosterBackup(string Path, DateTimeOffset SavedAt, int MemberCount);

/// <summary>
/// The address book on disk (PLAN.md §11 M12): <c>&lt;AppData&gt;/TrestleBoard/roster.json</c> plus a
/// <c>roster-backups/</c> ring of ten.
///
/// This file holds data that exists nowhere else — no `.tboard` carries it, no command stack spans
/// sessions — so the ring is its only protection, and every write is temp-then-rename for the same
/// reason the recovery store's is: a truncated address book is worse than yesterday's address book.
/// Loading follows <c>AppSettings.Load</c>'s contract exactly — a
/// missing, unreadable or garbage file yields an empty book rather than an exception, because
/// refusing to start is never the better failure.
///
/// Velopack installs the app to <c>%LocalAppData%</c>, a different root, so an auto-update cannot
/// touch any of this.
/// </summary>
public sealed class RosterStore
{
    private const string BackupPrefix = "roster-";
    private const string BackupSuffix = ".roster.bak.json";
    private const string TempSuffix = ".tmp";

    /// <summary>Ten, matching the rotating `.bak` ring PLAN.md §4 gives the user's own files.</summary>
    public const int BackupsKept = 10;

    private readonly Func<DateTimeOffset> _clock;

    /// <param name="path">The roster file. The backups sit in <c>roster-backups/</c> beside it.</param>
    /// <param name="clock">Injected so the ring's rotation is testable without waiting a second.</param>
    public RosterStore(string path, Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        BackupDirectory = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(path) ?? ".", "roster-backups");
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Path { get; }

    public string BackupDirectory { get; }

    public bool Exists => File.Exists(Path);

    /// <summary>Never throws. See the class remarks — this is a deliberate contract, not laziness.</summary>
    public RosterBook Load() => Load(out _);

    /// <summary>
    /// Never throws, and says which of the three things happened (M24). Callers that are going to
    /// WRITE this file must use this overload: <see cref="RosterLoadState.CouldNotBeRead"/> means the
    /// empty book they were handed is a placeholder standing in front of real data.
    /// </summary>
    public RosterBook Load(out RosterLoadState state)
    {
        if (!File.Exists(Path))
        {
            state = RosterLoadState.NoFileYet;
            return RosterBook.Empty;
        }

        try
        {
            RosterBook book =
                (JsonSerializer.Deserialize(File.ReadAllBytes(Path), RosterJsonContext.Default.RosterBook)
                    ?? RosterBook.Empty).Normalised();
            state = RosterLoadState.Loaded;
            return book;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException
            or NotSupportedException)
        {
            state = RosterLoadState.CouldNotBeRead;
            return RosterBook.Empty;
        }
    }

    /// <summary>
    /// Writes the book, keeping the version it replaces in the ring first. Unlike loading, this
    /// throws: a save the user asked for and that silently did not happen is how an address book
    /// gets lost.
    /// </summary>
    public void Save(RosterBook book)
    {
        ArgumentNullException.ThrowIfNull(book);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? ".");
        Backup();

        string temp = Path + TempSuffix;

        // Flushed to the DEVICE, not just to the OS cache, before the rename. Without this the
        // rename can reach the disk while the bytes are still in flight, and a power cut leaves a
        // present-but-empty roster.json — the exact corruption temp-then-rename exists to prevent
        // (M24). The cost is one fsync on a file measured in kilobytes.
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(JsonSerializer.SerializeToUtf8Bytes(
                book.Normalised(), RosterJsonContext.Default.RosterBook));
            fs.Flush(flushToDisk: true);
        }

        File.Move(temp, Path, overwrite: true);
    }

    /// <summary>The kept copies, newest first.</summary>
    public IReadOnlyList<RosterBackup> Backups()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return [];
        }

        var found = new List<RosterBackup>();
        foreach (string file in Directory.GetFiles(BackupDirectory, BackupPrefix + "*" + BackupSuffix))
        {
            if (!TryReadStamp(file, out DateTimeOffset savedAt))
            {
                continue;
            }

            int count;
            try
            {
                count = (JsonSerializer.Deserialize(
                    File.ReadAllBytes(file), RosterJsonContext.Default.RosterBook) ?? RosterBook.Empty).Count;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                continue;
            }

            found.Add(new RosterBackup(file, savedAt, count));
        }

        return found.OrderByDescending(b => b.SavedAt).ThenBy(b => b.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Reads one kept copy. Restoring it is an ordinary <see cref="Save"/> by the caller, so the
    /// version being replaced goes into the ring too — restoring the wrong one is itself undoable.
    /// </summary>
    public static RosterBook ReadBackup(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            return (JsonSerializer.Deserialize(File.ReadAllBytes(path), RosterJsonContext.Default.RosterBook)
                ?? RosterBook.Empty).Normalised();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return RosterBook.Empty;
        }
    }

    /// <summary>
    /// Copies the current file into the ring and trims it to <see cref="BackupsKept"/>. Best-effort:
    /// a ring that cannot be written must not stop the save it was meant to protect.
    /// </summary>
    private void Backup()
    {
        if (!File.Exists(Path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(BackupDirectory);
            File.Copy(Path, NextFreeBackupPath(_clock()), overwrite: false);

            foreach (RosterBackup old in Backups().Skip(BackupsKept))
            {
                TryDelete(old.Path);
            }

            // Backups() skips any file it cannot parse, so an unreadable one was never counted
            // against BackupsKept and never deleted — it simply accumulated, for ever, in a folder
            // the user never opens (review §14.2). They are removed here instead: a kept copy that
            // cannot be read is not a kept copy.
            foreach (string file in Directory.GetFiles(BackupDirectory, BackupPrefix + "*" + BackupSuffix))
            {
                if (!Backups().Any(b => string.Equals(b.Path, file, StringComparison.Ordinal)))
                {
                    TryDelete(file);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A backup path nothing is using yet, stepping the stamp forward a millisecond at a time.
    ///
    /// <para>The stamp names the file to the millisecond, and two saves inside one millisecond are
    /// perfectly possible — a fast machine, or a test. Until this existed the second copy was
    /// written with <c>overwrite: true</c> over the first, so <b>one of the ten kept versions
    /// silently vanished</b>: the ring reported ten entries and held nine distinct ones. It showed
    /// up as a Linux CI flake in `RestoringAnEarlierVersionIsItselfUndoable`, where three quick
    /// saves left two backups and the oldest one was not the version the test had kept.</para>
    ///
    /// <para>Stepping the stamp rather than adding a suffix keeps the file name in the one format
    /// <see cref="TryReadStamp"/> parses, and keeps the ring's ordering monotone. The cost is that a
    /// stamp means "when this was kept, to the nearest free millisecond", which is a truthful thing
    /// for it to mean.</para>
    /// </summary>
    private string NextFreeBackupPath(DateTimeOffset now)
    {
        // A whole second of collisions is far past anything real; giving up and overwriting after
        // that is better than looping in a method whose job is to be best-effort.
        for (int i = 0; i < 1000; i++)
        {
            string candidate = System.IO.Path.Combine(
                BackupDirectory,
                BackupPrefix
                    + now.AddMilliseconds(i).UtcDateTime.ToString(
                        "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
                    + BackupSuffix);
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return System.IO.Path.Combine(
            BackupDirectory,
            BackupPrefix + now.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
                + BackupSuffix);
    }

    /// <summary>The time is in the file name, so the ring survives a copy that loses file times.</summary>
    private static bool TryReadStamp(string path, out DateTimeOffset savedAt)
    {
        savedAt = default;
        string name = System.IO.Path.GetFileName(path);
        if (!name.StartsWith(BackupPrefix, StringComparison.Ordinal)
            || !name.EndsWith(BackupSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        string stamp = name[BackupPrefix.Length..^BackupSuffix.Length];
        if (!DateTime.TryParseExact(
                stamp,
                "yyyyMMdd-HHmmss-fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime parsed))
        {
            return false;
        }

        savedAt = new DateTimeOffset(parsed, TimeSpan.Zero);
        return true;
    }
}
