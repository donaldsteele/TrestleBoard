using System;
using System.IO;

namespace TrestleBoard.App;

/// <summary>
/// Temp-then-rename for a file the shell writes itself — the discipline
/// <c>TboardContainer.SaveToFile</c>, <c>SuccessorPackContainer</c>, <c>RosterStore.Save</c> and
/// <c>FileRecoveryStore.WriteAtomic</c> all share, made available to the one write path that never
/// had it: "Make the PDF" (PLAN.md §11 M74 (a)). The file already at <c>path</c> is untouched until
/// the rename, so a failure anywhere — disk full, an antivirus lock, a crash mid-write — leaves
/// last month's good PDF exactly as it was instead of a truncated file with the right name.
/// </summary>
internal static class AtomicFileWrite
{
    /// <summary>
    /// Writes via <paramref name="write"/> into a temp beside <paramref name="path"/>, flushes it
    /// to the device, then renames it over the target. On failure the temp is removed and the
    /// original exception is the one thrown — a temp that could not be deleted never replaces it.
    /// </summary>
    public static void Write(string path, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(write);

        string temp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(fs);

                // Flushed to the DEVICE, not just the OS cache, before the rename — otherwise the
                // rename can reach the disk while the bytes are still in flight, and a power cut
                // leaves a present-but-empty PDF (the M24 reasoning, applied here).
                fs.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The original failure is the one worth reporting; a temp file we could not remove
                // must not replace it with a second, less useful, exception.
            }

            throw;
        }
    }
}
