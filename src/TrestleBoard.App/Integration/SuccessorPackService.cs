using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TrestleBoard.App.Settings;
using TrestleBoard.Core.Container;
using TrestleBoard.Roster;
using TrestleBoard.Spelling;

namespace TrestleBoard.App.Integration;

/// <summary>
/// One part of a pack as a person needs to see it: what it is, how much of it there is, and what
/// taking it would do to what is already here.
/// </summary>
/// <param name="Id">One of <see cref="SuccessorPackParts"/>.</param>
/// <param name="Title">The short name, from Core so every surface says it the same way.</param>
/// <param name="Description">The sentence under it.</param>
/// <param name="Incoming">What the pack has — "84 people". Empty when the pack has none of this.</param>
/// <param name="AlreadyHere">What this computer has, or empty if nothing.</param>
/// <param name="WouldReplace">
/// True when this computer already has the store, so taking it overwrites something. The window
/// leaves these unticked: nothing is replaced unless somebody said so out loud.
/// </param>
internal sealed record PackPartChoice(
    string Id,
    string Title,
    string Description,
    string Incoming,
    string AlreadyHere,
    bool WouldReplace);

/// <summary>What a restore actually did, so the app can say so rather than going quiet.</summary>
/// <param name="Restored">Part ids that were written, in pack order.</param>
/// <param name="Failed">Part ids that could not be written, with the reason.</param>
internal sealed record RestoreOutcome(
    IReadOnlyList<string> Restored,
    IReadOnlyList<(string PartId, string Reason)> Failed);

/// <summary>
/// Packing up and unpacking a committee's whole working life (PLAN.md §11 M64).
///
/// <para><b>Why this exists.</b> Committee turnover is the existential risk for a volunteer lodge.
/// M57 lets one template be handed on; everything else — the address book, the phrase shelf, the
/// personal dictionary, the settings — has until now died with the old computer, along with the
/// several evenings of typing inside it.</para>
///
/// <para><b>One rule for restoring: the pack's files are written over the same paths, and nothing
/// the pack does not carry is ever deleted.</b> That single sentence is the whole policy, and it is
/// what makes the behaviour explainable to the person doing it. A restore that also tidied up —
/// removing templates the pack lacks, trimming the backup ring — would be a restore that quietly
/// destroyed the receiving committee's own work while claiming to be a copy.</para>
///
/// <para><b>Every path comes from <see cref="AppPaths"/>,</b> so the harness's temporary app-state
/// root covers this too (§0 rule 6). It matters more here than anywhere: this class's whole job is
/// to read every personal store in one pass and write it into a single file.</para>
/// </summary>
internal static class SuccessorPackService
{
    /// <summary>Where each store's files sit inside the pack. The first segment is the part id.</summary>
    private const string RosterEntry = SuccessorPackParts.Roster + "/roster.json";
    private const string RosterBackupsPrefix = SuccessorPackParts.Roster + "/roster-backups/";
    private const string TemplatesPrefix = SuccessorPackParts.Templates + "/";
    private const string PhrasesEntry = SuccessorPackParts.Phrases + "/phrases.json";
    private const string DictionaryEntry = SuccessorPackParts.Dictionary + "/personal-dictionary.txt";
    private const string SettingsEntry = SuccessorPackParts.Settings + "/settings.json";

    /// <summary>
    /// Reads every store on this computer into a package ready to be written out.
    ///
    /// <para>Never throws. A store that cannot be read is left out of the pack rather than taking
    /// the whole pack down with it — a successor with four of the five things is far better served
    /// than one with an error message, and the manifest says what is actually in there.</para>
    /// </summary>
    internal static SuccessorPackage Gather(DateTimeOffset now, string generatorVersion)
    {
        var package = new SuccessorPackage
        {
            Manifest = new SuccessorPackManifest
            {
                GeneratorVersion = generatorVersion,
                WrittenOn = now,
            },
        };

        AddPart(package, SuccessorPackParts.Roster, RosterFiles(), PeopleSummary);
        AddPart(package, SuccessorPackParts.Templates, TemplateFiles(), _ => TemplatesSummary());
        AddPart(package, SuccessorPackParts.Phrases, One(PhrasesEntry, AppPaths.PhraseShelfFile), _ => PhrasesSummary());
        AddPart(package, SuccessorPackParts.Dictionary, One(DictionaryEntry, AppPaths.PersonalDictionaryFile), _ => WordsSummary());
        AddPart(package, SuccessorPackParts.Settings, One(SettingsEntry, AppPaths.SettingsFile), _ => "your colours and text size");

        return package;
    }

    /// <summary>
    /// The pack's parts set beside what this computer already has, in the order to ask about them.
    /// Parts the pack does not carry are left out — offering to restore nothing is a way of making
    /// somebody read a line for no reason.
    /// </summary>
    internal static IReadOnlyList<PackPartChoice> Choices(SuccessorPackage pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var choices = new List<PackPartChoice>();
        foreach (string id in SuccessorPackParts.InOrder)
        {
            if (!pack.FilesOf(id).Any())
            {
                continue;
            }

            string here = LocalSummary(id);
            choices.Add(new PackPartChoice(
                id,
                SuccessorPackParts.TitleOf(id),
                SuccessorPackParts.DescriptionOf(id),
                pack.PartOf(id)?.Summary ?? "",
                here,
                here.Length > 0));
        }

        return choices;
    }

    /// <summary>
    /// Writes the chosen parts onto this computer. A part that fails takes only itself down: the
    /// address book cannot be left half restored because the templates could not be written.
    ///
    /// <para><b>Each file lands whole or not at all</b> — written to a temp name beside the target
    /// and renamed over it, the discipline every other store in this app already uses. What is
    /// deliberately <i>not</i> promised is that a part is all-or-nothing: if the fourth of six
    /// templates fails, the three before it stay. Undoing them would mean deleting files, and the
    /// one restore rule is that this code never deletes anything. The part is named in
    /// <see cref="RestoreOutcome.Failed"/> instead, and the user is told which one to look at.</para>
    ///
    /// <para>The existing roster is copied into its own backup ring first, whenever the roster is
    /// among the chosen parts. Somebody who restores a predecessor's address book onto a computer
    /// that already had one has almost certainly done what they meant to — but "almost" is not good
    /// enough for the only copy of the lodge's membership, and the ring is already the app's answer
    /// for exactly this.</para>
    /// </summary>
    internal static RestoreOutcome Restore(SuccessorPackage pack, IReadOnlyCollection<string> partIds)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(partIds);

        var restored = new List<string>();
        var failed = new List<(string, string)>();

        foreach (string id in SuccessorPackParts.InOrder.Where(partIds.Contains))
        {
            try
            {
                if (id == SuccessorPackParts.Roster)
                {
                    KeepWhatIsHere();
                }

                foreach ((string entry, byte[] bytes) in pack.FilesOf(id))
                {
                    if (LocalPathFor(entry) is not { } path)
                    {
                        // An entry this build has no home for — a part from a newer TrestleBoard, or
                        // a name that tried to climb out of the folder. Skipped, never guessed at.
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    WriteWholeOrNotAtAll(path, bytes);
                }

                restored.Add(id);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed.Add((id, e.Message));
            }
        }

        return new RestoreOutcome(restored, failed);
    }

    /// <summary>
    /// Temp beside the target, flushed to the device, then renamed over — <c>TboardContainer</c>'s
    /// and <c>RosterStore</c>'s discipline, for the same reason. A disk that fills up half way
    /// through writing a restored <c>roster.json</c> would otherwise replace a good address book
    /// with a truncated one, which is the worst thing this milestone could possibly do.
    /// </summary>
    private static void WriteWholeOrNotAtAll(string path, byte[] bytes)
    {
        string temp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
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
                // The original failure is the one worth reporting.
            }

            throw;
        }
    }

    /// <summary>
    /// Where one pack entry belongs on this computer, or null if it belongs nowhere.
    ///
    /// <para><b>This is the security boundary of the milestone.</b> A pack arrives by email from
    /// outside, and a zip entry is a string somebody else chose: <c>../../.ssh/authorized_keys</c>
    /// is a valid one. Nothing is joined onto a path from the archive without being checked to have
    /// landed inside the folder it was supposed to, and the roster, phrases, dictionary and settings
    /// entries are matched by <b>exact name</b> so they cannot be redirected at all.</para>
    /// </summary>
    internal static string? LocalPathFor(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return null;
        }

        switch (entry)
        {
            case RosterEntry:
                return AppPaths.RosterFile;
            case PhrasesEntry:
                return AppPaths.PhraseShelfFile;
            case DictionaryEntry:
                return AppPaths.PersonalDictionaryFile;
            case SettingsEntry:
                return AppPaths.SettingsFile;
        }

        if (entry.StartsWith(RosterBackupsPrefix, StringComparison.Ordinal))
        {
            return Inside(BackupDirectory(), entry[RosterBackupsPrefix.Length..]);
        }

        if (entry.StartsWith(TemplatesPrefix, StringComparison.Ordinal))
        {
            return Inside(AppPaths.TemplatesDirectory, entry[TemplatesPrefix.Length..]);
        }

        return null;
    }

    /// <summary>
    /// A file directly inside <paramref name="folder"/>, or null. Anything with a separator in it,
    /// a drive letter, or a <c>..</c> is refused rather than sanitised: a pack has no reason to
    /// contain a nested path, so one that does is either damaged or hostile, and quietly flattening
    /// it would be inventing a file the sender did not send.
    /// </summary>
    private static string? Inside(string folder, string name)
    {
        if (name.Length == 0
            || name.Contains('/', StringComparison.Ordinal)
            || name.Contains('\\', StringComparison.Ordinal)
            || name is "." or ".."
            || Path.IsPathRooted(name)
            || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
        {
            return null;
        }

        string full = Path.GetFullPath(Path.Combine(folder, name));
        string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.Ordinal) ? full : null;
    }

    // ---- gathering ------------------------------------------------------------------------------

    private static void AddPart(
        SuccessorPackage package,
        string id,
        IEnumerable<(string Entry, string Path)> files,
        Func<int, string> summarise)
    {
        int taken = 0;
        foreach ((string entry, string path) in files)
        {
            if (ReadOrNull(path) is not { } bytes)
            {
                continue;
            }

            package.Files[entry] = bytes;
            taken++;
        }

        if (taken > 0)
        {
            package.Manifest.Parts.Add(new SuccessorPackPart(id, taken, summarise(taken)));
        }
    }

    private static IEnumerable<(string Entry, string Path)> One(string entry, string path) =>
        [(entry, path)];

    private static IEnumerable<(string Entry, string Path)> RosterFiles()
    {
        yield return (RosterEntry, AppPaths.RosterFile);

        foreach (string path in FilesIn(BackupDirectory()))
        {
            yield return (RosterBackupsPrefix + Path.GetFileName(path), path);
        }
    }

    /// <summary>
    /// Both files of every template: the <c>.tboard</c> and the sidecar that holds its name. Taking
    /// the document and leaving the sidecar would hand the successor a shelf of layouts named
    /// <c>stated-communication</c> instead of "Stated Communication".
    /// </summary>
    private static IEnumerable<(string Entry, string Path)> TemplateFiles() =>
        FilesIn(AppPaths.TemplatesDirectory)
            .Where(p => Path.GetExtension(p) is ".tboard" or ".json")
            .Select(p => (TemplatesPrefix + Path.GetFileName(p), p));

    private static IEnumerable<string> FilesIn(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? [.. Directory.GetFiles(directory).OrderBy(p => p, StringComparer.Ordinal)]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static byte[]? ReadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string BackupDirectory() => new RosterStore(AppPaths.RosterFile).BackupDirectory;

    // ---- summaries ------------------------------------------------------------------------------

    /// <summary>
    /// What is in a store, in counts and never in names (§0 rule 7 — the summary is written into
    /// the manifest, and a manifest is the part of the pack somebody is most likely to look at).
    /// </summary>
    private static string LocalSummary(string id) => id switch
    {
        SuccessorPackParts.Roster => File.Exists(AppPaths.RosterFile) ? PeopleSummary(0) : "",
        SuccessorPackParts.Templates => TemplatesSummary(),
        SuccessorPackParts.Phrases => File.Exists(AppPaths.PhraseShelfFile) ? PhrasesSummary() : "",
        SuccessorPackParts.Dictionary => File.Exists(AppPaths.PersonalDictionaryFile) ? WordsSummary() : "",
        SuccessorPackParts.Settings => File.Exists(AppPaths.SettingsFile) ? "your colours and text size" : "",
        _ => "",
    };

    private static string PeopleSummary(int _)
    {
        try
        {
            var store = new RosterStore(AppPaths.RosterFile);
            return store.Exists ? Count(store.Load().Members.Count, "person", "people") : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>
    /// Counts templates, not files. Each one is a <c>.tboard</c> and a sidecar, and "2 templates"
    /// for one saved layout is the kind of small lie that makes somebody distrust the whole window.
    /// </summary>
    private static string TemplatesSummary()
    {
        int n = TemplateFiles().Count(f => f.Entry.EndsWith(".tboard", StringComparison.Ordinal));
        return n == 0 ? "" : Count(n, "template");
    }

    private static string PhrasesSummary()
    {
        try
        {
            return Count(new PhraseShelf().All().Count, "saved wording");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static string WordsSummary()
    {
        try
        {
            return Count(new PersonalDictionary(AppPaths.PersonalDictionaryFile).Words.Count, "word");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static string Count(int n, string one, string? many = null) =>
        n == 1 ? $"1 {one}" : $"{n} {many ?? one + "s"}";

    /// <summary>
    /// Puts the roster that is here now into its own backup ring before it is replaced. Best effort:
    /// the restore the user asked for is not abandoned because the safety net could not be hung.
    /// </summary>
    private static void KeepWhatIsHere()
    {
        try
        {
            var store = new RosterStore(AppPaths.RosterFile);
            if (store.Exists)
            {
                store.Backup();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing to say: the pack is still about to be written, and the ring is a courtesy.
        }
    }
}
