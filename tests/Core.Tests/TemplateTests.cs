using System.Text.Json;
using System.Text.RegularExpressions;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Templates;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// docs/M9-spec.md §2: three shipped templates. PLAN.md §0 is stricter here than anywhere else — a
/// template is SHIPPED, so <see cref="NoTemplateContainsAPhoneOrAnUnexplainedName"/> is the gate
/// that protects it: a real name or phone number pasted into a template later must fail this test.
/// </summary>
public sealed partial class TemplateTests
{
    [Fact]
    public void EveryTemplateHasItsAdvertisedPageCountAndRoundTripsUnchanged()
    {
        foreach (TemplateInfo info in TemplateLibrary.All)
        {
            TboardPackage package = TemplateLibrary.Create(info.Id);
            Assert.Equal(info.PageCount, package.Document.Pages.Count);

            using var ms = new MemoryStream();
            TboardContainer.Save(package, ms);
            ms.Position = 0;
            TboardPackage reloaded = TboardContainer.Load(ms);

            Assert.Equal(Fixtures.Snapshot(package.Document), Fixtures.Snapshot(reloaded.Document));
        }
    }

    [Fact]
    public void AllListsThreeDistinctTemplates()
    {
        Assert.Equal(3, TemplateLibrary.All.Count);
        Assert.Equal(TemplateLibrary.All.Count, TemplateLibrary.All.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (TemplateInfo info in TemplateLibrary.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(info.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(info.Description));
            Assert.True(info.PageCount > 0);
        }
    }

    [Fact]
    public void UnknownTemplateIdThrowsKeyNotFoundException()
    {
        Assert.Throws<KeyNotFoundException>(() => TemplateLibrary.Create("does-not-exist"));
    }

    [Fact]
    public void EveryTextFrameStoryIsAPromptNotProse()
    {
        foreach (TemplateInfo info in TemplateLibrary.All)
        {
            Document document = TemplateLibrary.Create(info.Id).Document;
            var referencedStoryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Page page in document.Pages)
            {
                foreach (Block block in page.Blocks)
                {
                    if (block is TextBlock text)
                    {
                        referencedStoryIds.Add(text.StoryRef);
                    }
                }
            }

            Assert.NotEmpty(referencedStoryIds);
            foreach (string storyId in referencedStoryIds)
            {
                Story story = document.GetStory(storyId);
                StoryParagraph paragraph = Assert.Single(story.Paragraphs);
                StoryRun run = Assert.Single(paragraph.Runs);

                Assert.StartsWith("Write", run.Text, StringComparison.Ordinal);
                Assert.True(run.Text.Length > "Write".Length, $"{info.Id}/{storyId}: prompt reads as empty.");
            }
        }
    }

    [Fact]
    public void EveryWidgetPayloadIsEmptyOfPeople()
    {
        foreach (TemplateInfo info in TemplateLibrary.All)
        {
            Document document = TemplateLibrary.Create(info.Id).Document;
            foreach (Page page in document.Pages)
            {
                foreach (Block block in page.Blocks)
                {
                    if (block is WidgetBlock widget)
                    {
                        AssertWidgetEmptyOfPeople(info.Id, widget);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The privacy gate (docs/M9-spec.md §2, PLAN.md §0). Deliberately blunt, matching the
    /// idiom docs/M7-spec.md §10.2 already uses for widget fixtures: scan every human-facing string
    /// in the template for anything phone-shaped, and for anything name-shaped that is not one of
    /// this template set's known structural phrases (officer position titles, a heading) or an
    /// obvious prompt (this file's convention: every prompt starts with "Write"). A real name pasted
    /// into a template default later is neither, and fails here.
    /// </summary>
    [Fact]
    public void NoTemplateContainsAPhoneOrAnUnexplainedName()
    {
        foreach (TemplateInfo info in TemplateLibrary.All)
        {
            Document document = TemplateLibrary.Create(info.Id).Document;
            foreach (string text in EnumerateHumanStrings(document))
            {
                Assert.False(
                    PhoneShapedPattern().IsMatch(text),
                    $"{info.Id}: phone-shaped text in a shipped template: '{text}'");

                if (text.StartsWith("Write", StringComparison.Ordinal))
                {
                    continue; // an obvious prompt (this file's convention)
                }

                foreach (Match match in NameShapedPattern().Matches(text))
                {
                    Assert.True(
                        AllowedStructuralPhrases.Contains(match.Value),
                        $"{info.Id}: possible name-shaped literal '{match.Value}' in '{text}'");
                }
            }
        }
    }

    // ---- privacy-gate machinery ---------------------------------------------------------------

    /// <summary>Structural phrases that happen to look name-shaped (two capitalized words) but are
    /// Masonic office titles or a widget heading, never a person. Kept short deliberately: anything
    /// not on this exact list must be a prompt or fails the gate.</summary>
    private static readonly HashSet<string> AllowedStructuralPhrases = new(StringComparer.Ordinal)
    {
        "Worshipful Master",
        "Senior Warden",
        "Junior Warden",
        "Senior Deacon",
        "Junior Deacon",
        "Senior Steward",
        "Junior Steward",
        "Lodge Officers",
    };

    [GeneratedRegex(@"(\(\d{3}\)\s*|\d{3}[-.\s])?\d{3}[-.\s]\d{4}\b")]
    private static partial Regex PhoneShapedPattern();

    /// <summary>"John Smith" or "A. Placeholder" shaped — the two forms every fictional name in
    /// this repo's fixtures already uses (PLAN.md §0), so this is exactly the shape a real one
    /// pasted in later would take too.</summary>
    [GeneratedRegex(@"\b[A-Z][a-z]+\s[A-Z][a-z]+\b|\b[A-Z]\.\s?[A-Z][a-z]+\b")]
    private static partial Regex NameShapedPattern();

    private static IEnumerable<string> EnumerateHumanStrings(Document document)
    {
        if (document.Metadata.LodgeName.Length > 0)
        {
            yield return document.Metadata.LodgeName;
        }

        if (document.Metadata.Title.Length > 0)
        {
            yield return document.Metadata.Title;
        }

        if (document.Metadata.MeetingRule.Length > 0)
        {
            yield return document.Metadata.MeetingRule;
        }

        foreach (Story story in document.Stories)
        {
            foreach (StoryParagraph paragraph in story.Paragraphs)
            {
                foreach (StoryRun run in paragraph.Runs)
                {
                    if (run.Text.Length > 0)
                    {
                        yield return run.Text;
                    }
                }
            }
        }

        foreach (Page page in document.Pages)
        {
            foreach (string s in EnumerateBlockStrings(page.Blocks))
            {
                yield return s;
            }
        }

        foreach (PageMaster master in document.PageMasters)
        {
            foreach (string s in EnumerateBlockStrings(master.Blocks))
            {
                yield return s;
            }
        }
    }

    private static IEnumerable<string> EnumerateBlockStrings(List<Block> blocks)
    {
        foreach (Block block in blocks)
        {
            switch (block)
            {
                case ImageFrame image:
                    if (image.AltText.Length > 0)
                    {
                        yield return image.AltText;
                    }

                    if (!string.IsNullOrEmpty(image.Caption))
                    {
                        yield return image.Caption;
                    }

                    break;

                case WidgetBlock { Data: { } data }:
                    foreach (string s in EnumerateJsonStrings(data))
                    {
                        yield return s;
                    }

                    break;
            }
        }
    }

    private static IEnumerable<string> EnumerateJsonStrings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString() ?? "";
                break;

            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    foreach (string s in EnumerateJsonStrings(property.Value))
                    {
                        yield return s;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    foreach (string s in EnumerateJsonStrings(item))
                    {
                        yield return s;
                    }
                }

                break;
        }
    }

    private static void AssertWidgetEmptyOfPeople(string templateId, WidgetBlock widget)
    {
        Assert.True(widget.Data.HasValue, $"{templateId}: widget '{widget.Id}' has no data.");
        JsonElement data = widget.Data!.Value;

        switch (widget.WidgetType)
        {
            case "coverBanner":
                Assert.Equal("", data.GetProperty("lodgeName").GetString());
                Assert.Equal("", data.GetProperty("meetingRule").GetString());
                Assert.Equal("", data.GetProperty("meetingDateText").GetString());
                break;

            case "officersTable":
                foreach (JsonElement officer in data.GetProperty("officers").EnumerateArray())
                {
                    Assert.Equal("", officer.GetProperty("name").GetString());
                    Assert.False(officer.TryGetProperty("phone", out JsonElement phone) && phone.ValueKind == JsonValueKind.String);
                }

                break;

            case "birthdayList":
                Assert.Equal(0, data.GetProperty("entries").GetArrayLength());
                break;

            case "eventCard":
                Assert.Equal("", data.GetProperty("title").GetString());
                Assert.Equal("", data.GetProperty("bodyText").GetString());
                break;

            default:
                Assert.Fail($"{templateId}: unhandled widget type in the privacy test: {widget.WidgetType}");
                break;
        }
    }
}
