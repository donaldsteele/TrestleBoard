using System.Globalization;
using System.Text.Json;

namespace TrestleBoard.Roster;

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
    public RosterBook Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return RosterBook.Empty;
            }

            return (JsonSerializer.Deserialize(File.ReadAllBytes(Path), RosterJsonContext.Default.RosterBook)
                ?? RosterBook.Empty).Normalised();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException
            or NotSupportedException)
        {
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
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(
            book.Normalised(), RosterJsonContext.Default.RosterBook));
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
            DateTimeOffset now = _clock();
            string stamp = now.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            File.Copy(Path, System.IO.Path.Combine(BackupDirectory, BackupPrefix + stamp + BackupSuffix), overwrite: true);

            foreach (RosterBackup old in Backups().Skip(BackupsKept))
            {
                try
                {
                    File.Delete(old.Path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
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
