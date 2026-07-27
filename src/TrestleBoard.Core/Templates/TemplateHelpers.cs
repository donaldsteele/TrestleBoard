using System.Text.Json;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Templates;

/// <summary>
/// Shared scaffolding for the three shipped templates (docs/M9-spec.md §2): the standard style
/// sheet (the same names <see cref="Samples.SampleIssue"/> uses, so a template looks like the
/// issues the committee already prints) and the small builders that keep the three template files
/// from repeating the same boilerplate.
///
/// <b>PLAN.md §0 is absolute here.</b> A template is shipped, so every default below is either a
/// PROMPT (for prose the reader will see, e.g. an essay) or EMPTY, structural-scaffolding-only data
/// (for widgets) — never a fictional person, phone number or date. The widget defaults mirror
/// docs/M7-spec.md §8.3's "data at insert" table exactly, because that table is already the
/// project's answer to "what does an empty widget look like".
/// </summary>
internal static class TemplateHelpers
{
    public const string DefaultParagraphStyleRef = "body";

    /// <summary>The standard style sheet at template dimensions. See <see cref="StandardStyles"/>
    /// — this used to be its own near-duplicate copy of the same table.</summary>
    public static void AddStandardStyles(Document document) => StandardStyles.Add(document);

    /// <summary>A story holding exactly one paragraph: the prompt. This is the whole of what a
    /// template ever puts in a story — see the M9 task brief's rule that a template ships prompts,
    /// never sample prose.</summary>
    public static Story PromptStory(string storyId, string prompt) => new()
    {
        Id = storyId,
        Paragraphs = [Para(DefaultParagraphStyleRef, prompt)],
    };

    public static StoryParagraph Para(string styleRef, string text) => new()
    {
        ParagraphStyleRef = styleRef,
        Runs = [new StoryRun { Text = text }],
    };

    public static WidgetBlock Widget(string id, string widgetType, RectPt rect, int zOrder, string json) => new()
    {
        Id = id,
        WidgetType = widgetType,
        DataVersion = 1,
        Data = JsonSerializer.Deserialize<JsonElement>(json),
        FrameRect = rect,
        ZOrder = zOrder,
        WrapMode = WrapMode.Rectangle,
        WrapMarginPt = 8f,
    };

    // ---- empty widget payloads: docs/M7-spec.md §8.3's insert-time defaults, verbatim -------------

    public const string EmptyCoverBannerJson = """
        {
          "lodgeName": "",
          "headingText": "STATED COMMUNICATION",
          "meetingRule": "",
          "meetingDateText": ""
        }
        """;

    public const string EmptyOfficersTableJson = """
        {
          "heading": "Lodge Officers",
          "vacantText": "(vacant)",
          "officers": [
            { "position": "Worshipful Master", "name": "" },
            { "position": "Senior Warden", "name": "" },
            { "position": "Junior Warden", "name": "" },
            { "position": "Senior Deacon", "name": "" },
            { "position": "Junior Deacon", "name": "" },
            { "position": "Senior Steward", "name": "" },
            { "position": "Junior Steward", "name": "" },
            { "position": "Treasurer", "name": "" },
            { "position": "Secretary", "name": "" },
            { "position": "Chaplain", "name": "" },
            { "position": "Tiler", "name": "" },
            { "position": "Marshall", "name": "" }
          ]
        }
        """;

    public const string EmptyBirthdayListJson = """
        {
          "heading": "Birthdays",
          "entries": []
        }
        """;

    public const string EmptyEventCardJson = """
        {
          "title": "",
          "bodyText": ""
        }
        """;
}
