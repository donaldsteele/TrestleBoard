using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;

namespace TrestleBoard.App.Integration;

/// <summary>Why last year's issue could not be opened, in words the user can act on (M59).</summary>
internal enum PastIssueProblem
{
    /// <summary>It opened.</summary>
    None,

    /// <summary>Nowhere has been named yet as the place old issues live.</summary>
    NoFolderYet,

    /// <summary>The folder is there; nothing in it is that month of that year.</summary>
    NotFound,

    /// <summary>A file was found and could not be read at all.</summary>
    CouldNotBeRead,

    /// <summary>A file was found and is older than this TrestleBoard can migrate.</summary>
    TooOldToOpen,
}

/// <summary>One article from an old issue, ready to be copied across (M59).</summary>
/// <param name="Heading">The first few words, so the user can tell one from another.</param>
/// <param name="Text">The whole story, paragraphs joined by newlines.</param>
internal sealed record PastArticle(string Heading, string Text);

/// <summary>What was found when last year's issue was looked for (M59).</summary>
internal sealed record PastIssue(
    PastIssueProblem Problem,
    string? Path,
    TboardPackage? Package,
    IReadOnlyList<PastArticle> Articles,
    string? Message)
{
    internal bool Opened => Problem == PastIssueProblem.None && Package is not null;
}

/// <summary>
/// One newsletter offered back on the start screen (M76 (h)).
/// </summary>
/// <param name="Path">The file to open, which is opened by the ordinary open path.</param>
/// <param name="Name">The file's own plain name, without the <c>.tboard</c> on the end — which for
/// anything this application saved is "Trestle Board 2026-07", so it says which issue it is.</param>
/// <param name="Detail">When it was last saved, so two issues of the same month can be told
/// apart. <b>Last saved, not last opened</b>: see <see cref="PastIssues.Recent"/> for why that is
/// the honest word.</param>
internal sealed record RecentIssue(string Path, string Name, string Detail);

/// <summary>
/// Finding the same month of last year (PLAN.md §11 M59).
///
/// <para>The monthly cycle has an annual rhythm the product ignores: the picnic announcement, the
/// awards night, the installation notice. Committee members keep old PDFs open in another window
/// and retype from them.</para>
///
/// <para><b>Reading only.</b> Nothing in this file constructs a <c>DocumentSession</c>, and that is
/// the whole of M59's safety story — see <c>LastYearWindow</c>. What comes back is a package and
/// some strings.</para>
/// </summary>
internal static class PastIssues
{
    /// <summary>
    /// Looks in <paramref name="folder"/> for the issue of <paramref name="month"/> in the year
    /// before <paramref name="year"/>, and reads it.
    ///
    /// <para>Every failure is a sentence rather than an exception: this is a convenience, and a
    /// convenience that throws is worse than one that says "not this time".</para>
    /// </summary>
    internal static PastIssue Find(string? folder, int month, int year)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return Failed(
                PastIssueProblem.NoFolderYet,
                "TrestleBoard does not know where you keep your old newsletters yet. Show it the "
                + "folder once and it will remember.");
        }

        if (!Directory.Exists(folder))
        {
            return Failed(
                PastIssueProblem.NoFolderYet,
                $"The folder TrestleBoard was told to look in is not there any more ({folder}). "
                + "Show it the folder again.");
        }

        int wanted = year - 1;
        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(folder, "*.tboard", SearchOption.AllDirectories);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failed(
                PastIssueProblem.CouldNotBeRead,
                $"TrestleBoard could not look inside that folder. ({e.Message})");
        }

        PastIssueProblem worst = PastIssueProblem.NotFound;
        string? worstMessage = null;

        foreach (string path in candidates.OrderBy(p => p, StringComparer.Ordinal))
        {
            TboardPackage package;
            try
            {
                package = TboardContainer.LoadFromFile(path);
            }
            catch (Exception e) when (e is Core.Migrations.UnsupportedFormatException
                or InvalidDataException
                or NotSupportedException)
            {
                // The M25 standard: a file too old, or too broken, says so rather than crashing.
                // Kept as the answer only if nothing better turns up.
                worst = PastIssueProblem.TooOldToOpen;
                worstMessage =
                    $"{System.IO.Path.GetFileName(path)} was made by a version of TrestleBoard this "
                    + $"one cannot read. ({e.Message})";
                continue;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                worst = PastIssueProblem.CouldNotBeRead;
                worstMessage = $"{System.IO.Path.GetFileName(path)} could not be read. ({e.Message})";
                continue;
            }

            DocumentMetadata meta = package.Document.Metadata;
            if (meta.IssueMonth == month && meta.IssueYear == wanted)
            {
                return new PastIssue(PastIssueProblem.None, path, package, ArticlesIn(package.Document), null);
            }
        }

        return worst == PastIssueProblem.NotFound
            ? Failed(
                PastIssueProblem.NotFound,
                $"TrestleBoard looked in that folder and did not find a newsletter for "
                + $"{MonthName(month)} {wanted}.")
            : Failed(worst, worstMessage!);
    }

    /// <summary>
    /// The old issue's writing, one entry per story, in reading order. Stories that still hold a
    /// template prompt are left out: an empty frame from last September is not a thing anybody
    /// wants to copy across.
    /// </summary>
    internal static IReadOnlyList<PastArticle> ArticlesIn(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var articles = new List<PastArticle>();
        foreach (string storyId in StoryFinder.StoryOrder(document))
        {
            if (!document.TryGetStory(storyId, out Story? story))
            {
                continue;
            }

            string text = string.Join(
                "\n",
                story.Paragraphs.Select(StoryNavigator.GetParagraphText).Where(p => p.Trim().Length > 0));

            if (text.Trim().Length == 0 || Core.Templates.PlaceholderPrompts.LurksIn(text))
            {
                continue;
            }

            articles.Add(new PastArticle(Heading(text), text));
        }

        return articles;
    }

    /// <summary>
    /// The newsletters this committee touched most recently, newest first (M76 (h), spec §7).
    ///
    /// <para><b>Why this lives here.</b> M59 already answered "where do the old issues live?" once
    /// and remembered the answer in <c>AppSettings.OldIssuesFolder</c>. A recent-files list is the
    /// same question asked a different way, so it is answered from the same folder rather than from
    /// a second list kept somewhere else — two stores of "which newsletters matter" is two stores
    /// to disagree with each other, and the one nobody maintains is the one that lies.</para>
    ///
    /// <para><b>It is recency of saving, not of opening, and it says so.</b> Nothing in the
    /// application records when a file was last opened, and inventing a settings file to record it
    /// was not in this deliverable's remit. The file system knows when each newsletter was last
    /// written, which for this audience — who open a file, edit it and save it, for a week — is
    /// very nearly the same list in very nearly the same order. The label the user reads says "Last
    /// saved", because that is the fact we actually have.</para>
    ///
    /// <para><b>Nothing is loaded.</b> The name comes from the file name and the date from the
    /// directory entry, so this stays instant on a folder holding ten years of issues. That is also
    /// why an unreadable or too-old newsletter still appears: it is offered, and the ordinary open
    /// path says the M25 sentence about it if it will not open. Deciding here would mean loading
    /// every candidate to find out.</para>
    ///
    /// <para>Every failure is an empty list. A start screen that will not come up because a folder
    /// went missing is a far worse thing than a start screen with no shortcuts on it.</para>
    /// </summary>
    /// <param name="folder">Where old issues live — <c>AppSettings.OldIssuesFolder</c>, which is
    /// null until the committee has been asked.</param>
    /// <param name="most">How many to offer. Short on purpose: this is a shortcut past the file
    /// dialog, and a list long enough to need reading is not a shortcut.</param>
    internal static IReadOnlyList<RecentIssue> Recent(string? folder, int most = 5)
    {
        if (most <= 0 || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(folder, "*.tboard", SearchOption.AllDirectories);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var dated = new List<(string Path, DateTime When)>(candidates.Length);
        foreach (string path in candidates)
        {
            try
            {
                dated.Add((path, File.GetLastWriteTime(path)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // One file the disk will not talk about does not cost the user the other four.
            }
        }

        return dated
            .OrderByDescending(f => f.When)
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .Take(most)
            .Select(f => new RecentIssue(
                f.Path,
                System.IO.Path.GetFileNameWithoutExtension(f.Path),
                $"Last saved on {f.When.ToString("d MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture)}"))
            .ToList();
    }

    /// <summary>The first few words, which is how a person tells one article from another.</summary>
    private static string Heading(string text)
    {
        string flat = string.Join(' ', text.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= 60 ? flat : flat[..60].TrimEnd() + "…";
    }

    private static PastIssue Failed(PastIssueProblem problem, string message) =>
        new(problem, null, null, [], message);

    private static string MonthName(int month) =>
        month is >= 1 and <= 12
            ? System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)
            : "that month";
}
