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
