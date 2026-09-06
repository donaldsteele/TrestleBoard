using System.Text.Json;
using System.Text.Json.Serialization;
using TrestleBoard.Core.Phrases;

namespace TrestleBoard.App.Settings;

/// <summary>
/// Which look the app wears (PLAN.md §6). <see cref="System"/> follows the OS hint, which is what a
/// first run does — someone who has already set their machine to a dark or high-contrast theme has
/// told us what they need.
/// </summary>
public enum ThemeChoice
{
    System,
    Light,
    Dark,
    HighContrast,
}

/// <summary>
/// The user's chrome preferences (PLAN.md §6). Deliberately tiny and deliberately persisted: an
/// elderly user who has found a scale that works must not have to find it again next week.
/// </summary>
public sealed record AppSettings
{
    /// <summary>PLAN.md §6 pins the range at 100–200%.</summary>
    public const int MinScalePercent = 100;

    public const int MaxScalePercent = 200;

    public ThemeChoice Theme { get; init; } = ThemeChoice.System;

    public int UiScalePercent { get; init; } = MinScalePercent;

    /// <summary>
    /// Whether the panel of things-you-can-do is docked open (M11). On by default: it is the
    /// primary way actions are discovered now, and someone who has never seen it cannot decide
    /// they want it.
    /// </summary>
    public bool ShowActionPanel { get; init; } = true;

    /// <summary>
    /// Whether the row of miniature pages is docked open down the left (M76 (g)). On by default, for
    /// the reason the panel is: it is how a page other than the next one is reached now, and
    /// somebody who has never seen it cannot decide they do not want it.
    /// </summary>
    public bool ShowPageRail { get; init; } = true;

    /// <summary>
    /// Whether "Make the PDF" offers to look the newsletter over first (M51). On by default,
    /// because somebody who has never been offered the review cannot decide they do not want it.
    ///
    /// <para>It lives here, in the user's settings, rather than in the newsletter — M51's
    /// acceptance is that the checklist stays read-only over the document, and a "do not ask me
    /// again" written into the `.tboard` would mark the newsletter as edited on the way to
    /// exporting it. This is a preference about how the person likes to work, and it should
    /// outlive any one issue.</para>
    /// </summary>
    public bool OfferTheReviewBeforeExport { get; init; } = true;

    /// <summary>
    /// Whether words the checker does not know get a dotted line under them (M52). On by default,
    /// unlike the two diagnostic overlays: those help somebody hunt down a setting, and this one is
    /// about the reader's newsletter. A misspelling nobody is shown is one that prints.
    /// </summary>
    public bool ShowSpelling { get; init; } = true;

    /// <summary>
    /// M87: make the email-sized PDF every time, without asking.
    ///
    /// <para><b>Off by default, because two files is one more thing to explain.</b> A committee
    /// whose four-page issue is under a megabyte should never meet the question at all — the offer
    /// only appears when the PDF is actually big enough for a mail server to refuse it. This is for
    /// the lodge that has learned it always needs the smaller one.</para>
    /// </summary>
    public bool AlwaysMakeEmailCopy { get; init; }

    /// <summary>
    /// Where the committee keeps its old newsletters (M59), or null until they have been asked.
    ///
    /// <para>Asked once and remembered, because "where do you keep them?" is a question worth
    /// answering once a decade and not once a month.</para>
    /// </summary>
    public string? OldIssuesFolder { get; init; }

    /// <summary>
    /// Whether the five-screen tour has been shown (M63). False until it has, so a brand-new
    /// installation gets it once and nobody else ever does.
    ///
    /// <para><b>Defaults to false, which means an existing committee sees the tour once after
    /// updating.</b> That is deliberate and is the safer of the two wrong answers: defaulting to
    /// true would hide it from everybody who already has TrestleBoard, including the successor who
    /// inherited the laptop and has never seen the app before — which is the person the tour is
    /// most for. One skippable window once is the cost.</para>
    ///
    /// <para>Not to be confused with Velopack's <c>OnFirstRun</c> in <c>Program.cs</c>: that is an
    /// installer hook which runs and exits before Avalonia is up, so a tour hung off it would run
    /// with no window and never be seen again.</para>
    /// </summary>
    public bool HasSeenTheTour { get; init; }

    /// <summary>
    /// Which officer members are told to speak to about sickness and distress (M54, the owner's
    /// ruling of 2026-08-09 — docs/M54-spec.md §3).
    ///
    /// <para><b>Free text, not a list of offices.</b> A list is safer to render, and wrong: a lodge
    /// that routes this through its Chaplain, its Junior Warden, a Sunshine Committee or a named
    /// visiting officer would find its own answer missing from a list somebody in another state
    /// wrote. The blank is one short phrase that lands in one sentence, and the committee can read
    /// the sentence back before it goes in — the two ways free text usually goes wrong.</para>
    ///
    /// <para>Empty is the one thing it may not be, so <see cref="Normalised"/> puts the default
    /// back. "Please speak to the , who is keeping in touch" is not a sentence to print.</para>
    /// </summary>
    public string SicknessContactOffice { get; init; } = PhraseLibrary.DefaultOffice;

    /// <summary>
    /// Pictures used before, newest first (M80). At most <see cref="RecentPicturesKept"/>.
    ///
    /// <para><b>Paths, never copies.</b> The lodge front and the Master's portrait go into most
    /// issues, and the committee should not have to find the same file every month. What is stored
    /// is where it was, so nothing is duplicated and nothing grows — and a path that no longer
    /// leads anywhere is dropped silently rather than offered.</para>
    ///
    /// <para>These are the user's own file paths and therefore §0 material: they are shown by FILE
    /// NAME only, never in full, and they never reach a problem report — <c>ProblemReportFacts</c>
    /// has nowhere to put them.</para>
    /// </summary>
    public IReadOnlyList<string> RecentPictures { get; init; } = [];

    /// <summary>How many are remembered. Ten is about a year of a committee's habits.</summary>
    public const int RecentPicturesKept = 10;

    /// <summary>
    /// This list with <paramref name="path"/> at the front, no duplicates, capped.
    ///
    /// <para>A pure function on the settings record so the rule — most recent first, each path
    /// once — is testable without a window and without a disk.</para>
    /// </summary>
    public AppSettings WithPictureUsed(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return this;
        }

        List<string> kept = [path];
        kept.AddRange((RecentPictures ?? [])
            .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
            .Take(RecentPicturesKept - 1));

        return this with { RecentPictures = kept };
    }

    /// <summary>
    /// The newsletters this person actually opened, most recent first (M106).
    ///
    /// <para><b>Why this exists beside <c>OldIssuesFolder</c>.</b> M76 (h) offered a recent list
    /// built by scanning the old-issues folder by last-write time, and said so on the record: no
    /// second store, and "last saved" is the fact we have. That list answers a different question.
    /// It is empty until somebody has nominated a folder, it cannot see a newsletter kept anywhere
    /// else, and it orders by when a file was WRITTEN — so a file touched by a backup tool climbs
    /// it, and a newsletter opened and read climbs nothing. <b>This one records opening</b>, which
    /// is the question "what was I working on" actually asks.</para>
    ///
    /// <para><b>Both are shown, and neither is thrown away.</b> The start screen keeps the folder
    /// scan, which is how a committee finds an issue from three years ago; this is what the File
    /// menu offers, which is how somebody gets back to what they had open on Tuesday.</para>
    ///
    /// <para>These are the user's own file paths and therefore §0 material, on exactly the terms
    /// <see cref="RecentPictures"/> is: shown by file name, never in full, and never in a problem
    /// report.</para>
    /// </summary>
    public IReadOnlyList<string> RecentNewsletters { get; init; } = [];

    /// <summary>How many are remembered. Eight is more than a committee has ever wanted at once.</summary>
    public const int RecentNewslettersKept = 8;

    /// <summary>
    /// This list with <paramref name="path"/> at the front, no duplicates, capped.
    ///
    /// <para>A pure function on the record, so the rule — most recent first, each path once,
    /// case-insensitively on the platforms where that is what a path means — is testable without a
    /// window and without a disk.</para>
    /// </summary>
    public AppSettings WithNewsletterOpened(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return this;
        }

        List<string> kept = [path];
        kept.AddRange((RecentNewsletters ?? [])
            .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
            .Take(RecentNewslettersKept - 1));

        return this with { RecentNewsletters = kept };
    }

    /// <summary>
    /// This list with <paramref name="path"/> taken out of it.
    ///
    /// <para>For a file that is no longer there. It is dropped when it is offered and found
    /// missing, rather than at load: a newsletter on a memory stick that is not plugged in today is
    /// not a newsletter that has stopped existing, and forgetting it on that basis would be the
    /// list quietly deciding something the user did not.</para>
    /// </summary>
    public AppSettings WithoutNewsletter(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? this
            : this with
            {
                RecentNewsletters = (RecentNewsletters ?? [])
                    .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
            };

    /// <summary>
    /// Where the window was when it last closed (M77), or null until it has closed once.
    ///
    /// <para>Four nullable fields rather than one rectangle, because a settings file that has been
    /// hand-edited or written by an older build may have some and not others, and four nulls
    /// degrade to "open where you always did" while a half-built rectangle would not.</para>
    ///
    /// <para><b>Remembering the size is not a nicety here.</b> M76 fixed the toolbar overflowing at
    /// the default 1280, and the reason it overflowed was that somebody at 200% scale had been
    /// maximising the window at every single launch for months. A preference this audience has to
    /// re-express daily is one the app is failing to hold.</para>
    /// </summary>
    public int? WindowWidth { get; init; }

    public int? WindowHeight { get; init; }

    public int? WindowLeft { get; init; }

    public int? WindowTop { get; init; }

    /// <summary>Whether it was maximised. Kept separately from the size, so un-maximising lands on
    /// the size it had before rather than on the whole screen.</summary>
    public bool WindowMaximised { get; init; }

    [JsonIgnore]
    public double UiScale => Math.Clamp(UiScalePercent, MinScalePercent, MaxScalePercent) / 100d;

    /// <summary>
    /// Clamped rather than rejected: a settings file edited by hand should not stop the app.
    ///
    /// <para><b>The two remembered lists are part of that promise.</b> Neither has ever been
    /// nullable in this record, so <c>= []</c> reads like a guarantee — but it is only the value the
    /// property is BORN with, and <c>"recentNewsletters": null</c> in the file overwrites it with
    /// null just as <c>[]</c> would overwrite it with empty. A real settings file on a real machine
    /// had exactly that, and because every opening funnels through the shell's <c>DocumentPath</c>,
    /// which remembers the newsletter, which walks that list, the whole application answered "Value
    /// cannot be null. (Parameter 'source')" to File → Open — for every newsletter, permanently.
    /// It also perpetuated itself: the null was read in and written straight back out on the next
    /// save. Coercing here mends both ends, since <see cref="Save"/> normalises on the way out too,
    /// so one run of a mended build cleans the file for good.</para>
    /// </summary>
    public AppSettings Normalised() => this with
    {
        UiScalePercent = Math.Clamp(UiScalePercent, MinScalePercent, MaxScalePercent),
        Theme = Enum.IsDefined(Theme) ? Theme : ThemeChoice.System,
        SicknessContactOffice = string.IsNullOrWhiteSpace(SicknessContactOffice)
            ? PhraseLibrary.DefaultOffice
            : SicknessContactOffice.Trim(),
        RecentPictures = RecentPictures ?? [],
        RecentNewsletters = RecentNewsletters ?? [],
    };

    /// <summary>
    /// From M12 this comes through <see cref="AppPaths"/> rather than building the AppData path
    /// itself, so a harness that redirects the root redirects this too.
    /// </summary>
    public static string DefaultPath() => AppPaths.SettingsFile;

    /// <summary>
    /// Never throws. A settings file that is missing, unreadable or garbage yields the defaults —
    /// losing a preference is a nuisance, refusing to start is not.
    /// </summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return (JsonSerializer.Deserialize(File.ReadAllBytes(path), SettingsJsonContext.Default.AppSettings)
                ?? new AppSettings()).Normalised();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Best-effort. A preference that cannot be written is not worth an error dialog — but it is
    /// worth saying, which is why this is no longer <c>void</c> (PLAN.md §11 M73(e)).
    /// </summary>
    /// <returns>False when the change did not reach the disk, so the caller can stop saying
    /// "Saved." over a setting that will be back to how it was next time.</returns>
    public bool Save(string? path = null)
    {
        path = path ?? DefaultPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(
                Normalised(), SettingsJsonContext.Default.AppSettings));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
