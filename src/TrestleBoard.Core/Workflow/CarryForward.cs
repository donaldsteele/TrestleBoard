using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;

namespace TrestleBoard.Core.Workflow;

/// <summary>
/// Start-from-last-month (docs/M9-spec.md §3, PLAN.md §7): the single biggest monthly-effort win
/// for the committee, and the one place a silent mistake reaches the printed page under the wrong
/// date. <see cref="NextIssue"/> copies a whole issue, bumps the date, recomputes the meeting date
/// from the machine-readable rule, and — the safety-critical step — resets every article to a
/// prompt so last month's message cannot go out again under this month's date.
/// </summary>
public static class CarryForward
{
    public const string DefaultArticlePrompt = "Write this month's article here…";

    private const string CoverBannerWidgetType = "coverBanner";
    private const string MeetingDateTextField = "meetingDateText";
    private const string FallbackParagraphStyleRef = "body";

    /// <summary>
    /// Builds next month's issue from <paramref name="previous"/>. <paramref name="previous"/> is
    /// never mutated — the deep copy happens first, via the same Save/Load path a real file goes
    /// through, so "the source must be untouched" holds by construction rather than by convention.
    /// </summary>
    public static TboardPackage NextIssue(TboardPackage previous, string articlePrompt = DefaultArticlePrompt)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentException.ThrowIfNullOrEmpty(articlePrompt);

        TboardPackage next = DeepCopy(previous);

        BumpIssueDate(next.Document.Metadata);
        RecomputeMeetingDates(next.Document);
        ResetArticleProse(next.Document, articlePrompt);

        return next;
    }

    /// <summary>
    /// A true deep copy, built by round-tripping through <see cref="TboardContainer"/> in memory
    /// rather than hand-written Clone() methods. The model has Clone() helpers on a few leaf types
    /// (<see cref="ImageRecipe"/>, <see cref="StoryParagraph"/>, <see cref="StoryRun"/>) but none on
    /// <see cref="Document"/>, <see cref="Page"/> or the polymorphic <see cref="Block"/> hierarchy,
    /// and this file must not add any to those existing files. Save/Load already knows how to
    /// reconstruct every field of every type byte-for-byte (that is its entire job); reusing it here
    /// means a future model change cannot make this copy silently incomplete the way a hand-written
    /// field-by-field clone could.
    /// </summary>
    private static TboardPackage DeepCopy(TboardPackage source)
    {
        using var stream = new MemoryStream();
        TboardContainer.Save(source, stream);
        stream.Position = 0;
        return TboardContainer.Load(stream);
    }

    private static void BumpIssueDate(DocumentMetadata metadata)
    {
        int month = metadata.IssueMonth + 1;
        int year = metadata.IssueYear;
        if (month > 12)
        {
            month = 1;
            year++;
        }

        metadata.IssueMonth = month;
        metadata.IssueYear = year;
    }

    /// <summary>Rewrites every CoverBanner widget's <c>meetingDateText</c> from the document's own
    /// <see cref="DocumentMetadata.MeetingRule"/>, now that it has been bumped. The widget's own
    /// <c>meetingRule</c> field is deliberately left alone (docs/M9-spec.md §3 step 3).</summary>
    private static void RecomputeMeetingDates(Document document)
    {
        if (!MeetingRule.TryParse(document.Metadata.MeetingRule, out MeetingRule rule))
        {
            // No usable rule, so the printed date cannot be recomputed. Leaving LAST month's date
            // under THIS month's issue is the same failure the prose reset exists to prevent, so the
            // date is cleared instead and the user is left an empty field to fill in.
            ClearMeetingDates(document);
            return;
        }

        DateOnly date = rule.ResolveDate(document.Metadata.IssueYear, document.Metadata.IssueMonth);
        string dateText = FormatMeetingDate(date);

        foreach (Page page in document.Pages)
        {
            UpdateCoverBanners(page.Blocks, dateText);
        }

        foreach (PageMaster master in document.PageMasters)
        {
            UpdateCoverBanners(master.Blocks, dateText);
        }
    }

    private static void UpdateCoverBanners(List<Block> blocks, string dateText)
    {
        foreach (Block block in blocks)
        {
            if (block is not WidgetBlock { WidgetType: CoverBannerWidgetType } widget || widget.Data is not { } data)
            {
                continue;
            }

            if (JsonNode.Parse(data.GetRawText()) is not JsonObject payload)
            {
                continue;
            }

            payload[MeetingDateTextField] = JsonValue.Create(dateText);
            widget.Data = JsonSerializer.Deserialize<JsonElement>(payload.ToJsonString());
        }
    }

    /// <summary>"July 7th" — the month name spelled out, the day with its ordinal suffix, no year
    /// (docs/M9-spec.md §3 step 3, matching the printed issues).</summary>
    private static string FormatMeetingDate(DateOnly date)
    {
        string month = date.ToString("MMMM", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{month} {date.Day}{OrdinalSuffix(date.Day)}");
    }

    private static string OrdinalSuffix(int day)
    {
        if (day % 100 is >= 11 and <= 13)
        {
            return "th";
        }

        return (day % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
    }

    /// <summary>Every story a plain TextBlock displays is replaced with a single prompt paragraph.
    /// This is the safety-critical step (docs/M9-spec.md §3 step 5): the failure being designed
    /// against is last month's message going out under this month's date, and the only safe default
    /// is to clear it and make the user write. Widget data is untouched here — it is carried
    /// forward unchanged by step 4, handled simply by not being reset.
    ///
    /// Clears EVERY story, not only those a text frame still points at. An orphaned story — one
    /// whose frame was deleted at some point — is invisible in the editor but still sits in the
    /// saved container, and "carry-forward never silently keeps prose" has to mean never.</summary>
    /// <summary>
    /// Blanks every cover banner's printed date. An empty field asks to be filled in; a wrong date
    /// looks finished and goes out.
    /// </summary>
    private static void ClearMeetingDates(Document document)
    {
        foreach (Page page in document.Pages)
        {
            UpdateCoverBanners(page.Blocks, string.Empty);
        }

        foreach (PageMaster master in document.PageMasters)
        {
            UpdateCoverBanners(master.Blocks, string.Empty);
        }
    }

    private static void ResetArticleProse(Document document, string prompt)
    {
        foreach (Story story in document.Stories)
        {
            string styleRef = story.Paragraphs.Count > 0
                ? story.Paragraphs[0].ParagraphStyleRef
                : FallbackParagraphStyleRef;

            story.Paragraphs = [new StoryParagraph
            {
                ParagraphStyleRef = styleRef,
                Runs = [new StoryRun { Text = prompt }],
            }];
        }
    }

}
