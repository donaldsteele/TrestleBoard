using System.Text.Json;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Samples;
using TrestleBoard.Core.Text;
using TrestleBoard.Core.Workflow;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// docs/M9-spec.md §3: start-from-last-month. Built against <see cref="SampleIssue"/> (the M8
/// five-page fixture) because it already has a realistic shape — a CoverBanner with a meeting rule,
/// an essay that flows across two frames, a closing piece, and the other five widgets — so a single
/// shared fixture exercises "carry widget data forward" and "reset prose" together, the way a real
/// issue would.
/// </summary>
public sealed class CarryForwardTests
{
    [Fact]
    public void BumpsIssueMonthWithinTheSameYear()
    {
        TboardPackage previous = SampleIssue.CreatePackage(); // July 2026
        TboardPackage next = CarryForward.NextIssue(previous);

        Assert.Equal(8, next.Document.Metadata.IssueMonth);
        Assert.Equal(2026, next.Document.Metadata.IssueYear);
    }

    [Fact]
    public void DecemberRollsToJanuaryOfTheNextYear()
    {
        TboardPackage previous = SampleIssue.CreatePackage();
        previous.Document.Metadata.IssueMonth = 12;
        previous.Document.Metadata.IssueYear = 2026;

        TboardPackage next = CarryForward.NextIssue(previous);

        Assert.Equal(1, next.Document.Metadata.IssueMonth);
        Assert.Equal(2027, next.Document.Metadata.IssueYear);
    }

    [Theory]
    // day/ordinal chosen so ResolveDate lands exactly on `expectedDay` for THIS test's target month
    // (August 2026) regardless of calendar arithmetic done by hand — see the sanity assert below.
    [InlineData(1, WeekOrdinal.First, "1st")]
    [InlineData(2, WeekOrdinal.First, "2nd")]
    [InlineData(3, WeekOrdinal.First, "3rd")]
    [InlineData(11, WeekOrdinal.Second, "11th")]
    [InlineData(21, WeekOrdinal.Third, "21st")]
    public void RecomputesTheMeetingDateWithTheRightOrdinalSuffix(int expectedDay, WeekOrdinal ordinal, string suffix)
    {
        var targetDate = new DateOnly(2026, 8, expectedDay);
        var rule = new MeetingRule(ordinal, targetDate.DayOfWeek);
        Assert.Equal(targetDate, rule.ResolveDate(2026, 8)); // sanity: the chosen rule really lands here

        TboardPackage previous = SampleIssue.CreatePackage(); // July 2026 -> bumps to August 2026
        previous.Document.Metadata.MeetingRule = rule.ToString();

        TboardPackage next = CarryForward.NextIssue(previous);

        Assert.Equal($"August {suffix}", GetCoverBannerField(next.Document, "meetingDateText"));
    }

    [Fact]
    public void CoverBannerMeetingRuleIsUntouchedWhileOnlyTheDateTextChanges()
    {
        TboardPackage previous = SampleIssue.CreatePackage();
        TboardPackage next = CarryForward.NextIssue(previous);

        Assert.Equal("1st Tuesday", GetCoverBannerField(previous.Document, "meetingRule"));
        Assert.Equal("1st Tuesday", GetCoverBannerField(next.Document, "meetingRule"));

        Assert.Equal("July 7th", GetCoverBannerField(previous.Document, "meetingDateText"));
        Assert.NotEqual("July 7th", GetCoverBannerField(next.Document, "meetingDateText"));
        Assert.Equal("August 4th", GetCoverBannerField(next.Document, "meetingDateText"));

        Assert.Equal(
            GetCoverBannerField(previous.Document, "lodgeName"),
            GetCoverBannerField(next.Document, "lodgeName"));
        Assert.Equal(
            GetCoverBannerField(previous.Document, "headingText"),
            GetCoverBannerField(next.Document, "headingText"));
    }

    [Fact]
    public void NonCoverBannerWidgetDataSurvivesByteIdentically()
    {
        TboardPackage previous = SampleIssue.CreatePackage();
        TboardPackage next = CarryForward.NextIssue(previous);

        bool foundAny = false;
        foreach (Page page in previous.Document.Pages)
        {
            Page nextPage = next.Document.GetPage(page.Id);
            foreach (Block block in page.Blocks)
            {
                if (block is not WidgetBlock { WidgetType: not "coverBanner" } widget)
                {
                    continue;
                }

                foundAny = true;
                var nextWidget = (WidgetBlock)nextPage.GetBlock(widget.Id);
                Assert.Equal(widget.DataVersion, nextWidget.DataVersion);
                Assert.Equal(CanonicalJson(widget.Data), CanonicalJson(nextWidget.Data));
            }
        }

        Assert.True(foundAny, "Fixture did not exercise any non-coverBanner widget.");
    }

    [Fact]
    public void ResetsArticleProseToThePromptAndDropsTheOldText()
    {
        const string prompt = "Write this month's article here…";
        TboardPackage previous = SampleIssue.CreatePackage();

        TboardPackage next = CarryForward.NextIssue(previous, prompt);

        Story essay = next.Document.GetStory("story-essay");
        StoryParagraph essayParagraph = Assert.Single(essay.Paragraphs);
        StoryRun essayRun = Assert.Single(essayParagraph.Runs);
        Assert.Equal(prompt, essayRun.Text);

        Story closing = next.Document.GetStory("story-closing");
        StoryParagraph closingParagraph = Assert.Single(closing.Paragraphs);
        StoryRun closingRun = Assert.Single(closingParagraph.Runs);
        Assert.Equal(prompt, closingRun.Text);

        string snapshot = Fixtures.Snapshot(next.Document);
        Assert.DoesNotContain("summer months bring their own rhythm", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("traveling gavel remains", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain(SampleIssue.CoverEssay, snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain(SampleIssue.ClosingPiece, snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void UsesTheDefaultPromptWhenNoneIsSupplied()
    {
        TboardPackage previous = SampleIssue.CreatePackage();
        TboardPackage next = CarryForward.NextIssue(previous);

        Story essay = next.Document.GetStory("story-essay");
        Assert.Equal(CarryForward.DefaultArticlePrompt, Assert.Single(Assert.Single(essay.Paragraphs).Runs).Text);
    }

    [Fact]
    public void DoesNotMutateTheSourcePackage()
    {
        TboardPackage previous = SampleIssue.CreatePackage();
        string beforeDocument = Fixtures.Snapshot(previous.Document);
        int beforeAssetCount = previous.Assets.Count;

        _ = CarryForward.NextIssue(previous);

        Assert.Equal(beforeDocument, Fixtures.Snapshot(previous.Document));
        Assert.Equal(beforeAssetCount, previous.Assets.Count);
        Assert.Equal(7, previous.Document.Metadata.IssueMonth);
        Assert.Equal("1st Tuesday", GetCoverBannerField(previous.Document, "meetingRule"));
    }

    [Fact]
    public void CarriesAssetsForward()
    {
        byte[] photo = [0xFF, 0xD8, 0xFF, 0x01, 0x02, 0x03];
        TboardPackage previous = SampleIssue.CreatePackage(photo);

        TboardPackage next = CarryForward.NextIssue(previous);

        Assert.Equal(previous.Assets.Keys, next.Assets.Keys);
        Assert.Equal(photo, next.Assets[SampleIssue.PhotoAssetName]);
    }

    [Fact]
    public void ThrowsOnANullPackage()
    {
        Assert.Throws<ArgumentNullException>(() => CarryForward.NextIssue(null!));
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static string GetCoverBannerField(Document document, string field)
    {
        foreach (Page page in document.Pages)
        {
            foreach (Block block in page.Blocks)
            {
                if (block is WidgetBlock { WidgetType: "coverBanner", Data: { } data })
                {
                    return data.GetProperty(field).GetString() ?? "";
                }
            }
        }

        throw new InvalidOperationException("No coverBanner widget found in the fixture.");
    }

    private static string CanonicalJson(JsonElement? element) =>
        element is null ? "" : JsonSerializer.Serialize(element.Value);

    /// <summary>
    /// An orphaned story — one whose frame was deleted at some point — is invisible in the editor
    /// but still in the saved container. "Never silently keeps prose" has to mean never.
    /// </summary>
    [Fact]
    public void EvenAStoryNoFramePointsAtLosesLastMonthsProse()
    {
        TboardPackage previous = SampleIssue.CreatePackage();
        previous.Document.Stories.Add(new Story
        {
            Id = "story-orphan",
            Paragraphs =
            {
                new StoryParagraph
                {
                    ParagraphStyleRef = "body",
                    Runs = [new StoryRun { Text = "An article whose frame was deleted last month." }],
                },
            },
        });

        TboardPackage next = CarryForward.NextIssue(previous);

        Story orphan = next.Document.Stories.First(s => s.Id == "story-orphan");
        Assert.DoesNotContain(
            "whose frame was deleted",
            string.Concat(orphan.Paragraphs.SelectMany(p => p.Runs).Select(r => r.Text)),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// With no usable meeting rule the date cannot be recomputed. Leaving LAST month's date under
    /// THIS month's issue is the same failure the prose reset exists to prevent, so it is cleared.
    /// </summary>
    [Fact]
    public void AnUnreadableMeetingRuleClearsTheDateRatherThanKeepingAStaleOne()
    {
        TboardPackage previous = SampleIssue.CreatePackage();
        previous.Document.Metadata.MeetingRule = "whenever we can all make it";

        TboardPackage next = CarryForward.NextIssue(previous);

        JsonElement payload = next.Document.Pages
            .SelectMany(p => p.Blocks)
            .OfType<WidgetBlock>()
            .First(w => w.WidgetType == "coverBanner")
            .Data!.Value;

        Assert.Equal("", payload.GetProperty("meetingDateText").GetString());

        // And the rule itself is left alone, so the user can correct it rather than retype it.
        Assert.Equal("whenever we can all make it", next.Document.Metadata.MeetingRule);
    }
}
