namespace TrestleBoard.Editing;

/// <summary>
/// One generation of the rotating <c>.bak</c> ring that sits beside the user's own newsletter file
/// (PLAN.md §4). Written by <see cref="FileRecoveryStore.RotateBackups"/> and listed back by
/// <see cref="FileRecoveryStore.FindBackups"/>.
///
/// <para>This is a different kind of protection from the recovery snapshot. The snapshot answers
/// "the power went off"; the ring answers "I deleted the page on purpose, saved, and now I want it
/// back" — which no autosave can help with, because nothing went wrong.</para>
/// </summary>
/// <param name="Path">Where the copy is, so the shell can read it back.</param>
/// <param name="Generation">1 is the most recent; 5 is the oldest kept.</param>
/// <param name="SavedAt">
/// When the copy was made — the file's own last-write time. That is the moment the user pressed
/// Save on the version this copy holds, because a generation is written by the save that replaces it.
/// </param>
/// <param name="Bytes">Its size on disk, which is the one other fact that distinguishes two saves
/// made minutes apart.</param>
public sealed record DocumentBackup(string Path, int Generation, DateTimeOffset SavedAt, long Bytes);
