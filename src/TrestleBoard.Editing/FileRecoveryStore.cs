using System.Globalization;
using System.Text.Json;

namespace TrestleBoard.Editing;

/// <summary>
/// The on-disk recovery area (PLAN.md §4): <c>&lt;AppData&gt;/TrestleBoard/recovery/</c>. Every write is
/// temp-then-rename, because a truncated recovery file is worse than none (docs/M9-spec.md §1.2).
/// </summary>
public sealed class FileRecoveryStore : IRecoveryStore
{
    private const string DocumentExtension = ".tboard";
    private const string SidecarExtension = ".json";
    private const string TempExtension = ".tmp";

    private readonly string _directory;

    public FileRecoveryStore(string? directory = null)
    {
        _directory = directory ?? DefaultDirectory();
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Where recovery files live when the app is not told otherwise.</summary>
    public static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrestleBoard",
        "recovery");

    /// <summary>Where this store is writing; the restore dialog names it.</summary>
    public string Location => _directory;

    public void Write(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string document = Path.Combine(_directory, snapshot.Id + DocumentExtension);
        string sidecar = Path.Combine(_directory, snapshot.Id + SidecarExtension);

        // DOCUMENT first, sidecar second. Both writes are atomic, but a crash between them has to
        // leave the pair readable, and only one order gets that right.
        //
        // This used to be the other way round, with a comment reasoning it through backwards
        // (review §14.2). Sidecar-first pairs the NEW timestamp with the PREVIOUS document — so the
        // restore dialog says "saved 10 seconds ago" over bytes that are a minute old, and the
        // user, told their work is current, accepts it and loses the difference without ever being
        // shown that there was one. Overstating freshness is the direction that costs work.
        //
        // Document-first pairs new bytes with a stale timestamp: the dialog understates how fresh
        // the snapshot is. The user is offered MORE work than they were promised, which is a
        // harmless surprise. A missing sidecar entirely — the first-ever write, interrupted — is
        // already handled by ReadSidecar returning its default.
        WriteAtomic(document, snapshot.Bytes);
        WriteAtomic(sidecar, JsonSerializer.SerializeToUtf8Bytes(
            new Sidecar(snapshot.OriginalPath, snapshot.SavedAt)));
    }

    public void Delete(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Remove(Path.Combine(_directory, id + DocumentExtension));
        Remove(Path.Combine(_directory, id + SidecarExtension));
    }

    public IReadOnlyList<RecoverySnapshot> FindRecoverable()
    {
        var found = new List<RecoverySnapshot>();
        if (!System.IO.Directory.Exists(_directory))
        {
            return found;
        }

        foreach (string path in System.IO.Directory.GetFiles(_directory, "*" + DocumentExtension)
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            // A zero-length file is a write that never completed. It cannot help anyone, and offering
            // it would be worse than saying nothing.
            if (bytes.Length == 0)
            {
                continue;
            }

            Sidecar sidecar = ReadSidecar(Path.Combine(_directory, id + SidecarExtension));
            found.Add(new RecoverySnapshot(id, bytes, sidecar.OriginalPath, sidecar.SavedAt));
        }

        return found;
    }

    /// <summary>
    /// Rotates the previous contents of a user's file through <c>.bak1</c>…<c>.bak5</c> beside it
    /// before overwriting (PLAN.md §4). This protects against the user's own mistakes, which an
    /// autosave cannot: deleting a page deliberately and saving is not a crash.
    /// </summary>
    public static void RotateBackups(string documentPath, int keep = 5)
    {
        ArgumentNullException.ThrowIfNull(documentPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);
        if (!File.Exists(documentPath))
        {
            return;
        }

        string BackupPath(int index) => string.Create(
            CultureInfo.InvariantCulture, $"{documentPath}.bak{index}");

        // Oldest first, so nothing is overwritten before it has been shifted along. A locked
        // generation (an antivirus scan, an open handle) must not abandon the rotation half-done and
        // skip the copy that actually protects the user.
        Remove(BackupPath(keep));
        for (int i = keep - 1; i >= 1; i--)
        {
            try
            {
                if (File.Exists(BackupPath(i)))
                {
                    File.Move(BackupPath(i), BackupPath(i + 1), overwrite: true);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        try
        {
            File.Copy(documentPath, BackupPath(1), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        string temp = path + TempExtension;
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }

    private static void Remove(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file we cannot delete is not worth failing a save over.
        }
    }

    private static Sidecar ReadSidecar(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Sidecar>(File.ReadAllBytes(path))
                ?? new Sidecar(null, DateTimeOffset.MinValue);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // M35: UnauthorizedAccessException joins the other two here, and at the three sites
            // above. A read-only file, an ACL change or an antivirus hold used to escape — and from
            // FindRecoverable that aborted the whole startup scan, hiding every OTHER recoverable
            // snapshot, while from Remove it escaped RecoveryService.Complete() on the clean-close
            // path (review §14.2). Neither is worth failing over.
            return new Sidecar(null, DateTimeOffset.MinValue);
        }
    }

    private sealed record Sidecar(string? OriginalPath, DateTimeOffset SavedAt);
}
