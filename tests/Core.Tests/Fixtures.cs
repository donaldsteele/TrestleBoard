using System.Text.Json;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Serialization;

namespace TrestleBoard.Core.Tests;

/// <summary>Shared fixture document. All data fictional (PLAN.md §0) — no real names/numbers.</summary>
internal static class Fixtures
{
    public const string BodyProse =
        "The Placeholder Lodge convenes on the appointed evening of every month. "
        + "Brothers gather early for fellowship, share a simple meal together, and then "
        + "proceed to the reading of the minutes.";

    public static Document BuildDocument()
    {
        var doc = new Document
        {
            Metadata = new DocumentMetadata
            {
                LodgeName = "Placeholder Lodge No. 000",
                IssueMonth = 7,
                IssueYear = 2026,
                Title = "Sample Trestle Board",
                MeetingRule = "1st Tuesday",
            },
            Theme = new Theme
            {
                ColorTokens = { ["ink"] = 0xFF000000, ["accent"] = 0xFF123456 },
                FontTokens = { ["body"] = "Source Serif 4", ["display"] = "Cinzel" },
                SpacingScale = [4f, 8f, 16f],
            },
        };

        doc.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "body",
            FontFamily = "Source Serif 4",
            SizePt = 12f,
        });
        doc.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "body-bold",
            FontFamily = "Source Serif 4",
            Weight = FontWeightToken.Bold,
            SizePt = 12f,
        });
        doc.StyleSheet.ParagraphStyles.Add(new ParagraphStyleDef
        {
            Name = "body",
            CharacterStyleRef = "body",
            LineSpacing = 1.2f,
        });
        doc.StyleSheet.FrameStyles.Add(new FrameStyleDef { Name = "plain", StrokeArgb = 0xFF000000, StrokeWidthPt = 0.5f });
        doc.StyleSheet.TableStyles.Add(new TableStyleDef { Name = "roster" });

        doc.PageMasters.Add(new PageMaster { Id = "master-1" });

        var story = new Story { Id = "story-1" };
        story.Paragraphs.Add(new StoryParagraph
        {
            ParagraphStyleRef = "body",
            Runs =
            [
                new StoryRun { Text = "A. Placeholder, Worshipful Master. ", CharacterStyleRef = "body-bold" },
                new StoryRun { Text = BodyProse },
            ],
        });
        story.Paragraphs.Add(new StoryParagraph
        {
            ParagraphStyleRef = "body",
            Runs = [new StoryRun { Text = "Dinner at 6:30, work at 7:30. Call 555-0100 with questions." }],
        });
        doc.Stories.Add(story);

        var page1 = new Page { Id = "page-1", MasterRef = "master-1" };
        page1.Blocks.Add(new ImageFrame
        {
            Id = "img-1",
            AssetRef = "img-fixture.png",
            FrameRect = new RectPt(306f, 100f, 180f, 120f),
            ZOrder = 10,
            WrapMode = WrapMode.Rectangle,
            WrapMarginPt = 6f,
            AltText = "Sample lodge photo placeholder",
            Recipe = new ImageRecipe { RotationSteps = 1, AutoLevels = true },
        });
        page1.Blocks.Add(new TextBlock
        {
            Id = "text-1",
            StoryRef = "story-1",
            FrameRect = new RectPt(54f, 90f, 504f, 300f),
            ZOrder = 1,
            LinkNext = "text-2",
        });
        page1.Blocks.Add(new ShapeBlock
        {
            Id = "rule-1",
            Kind = ShapeKind.Rule,
            FrameRect = new RectPt(54f, 400f, 504f, 1f),
            StrokeArgb = 0xFF000000,
            StrokeWidthPt = 1f,
        });
        doc.Pages.Add(page1);

        var page2 = new Page { Id = "page-2", MasterRef = "master-1" };
        page2.Blocks.Add(new TextBlock
        {
            Id = "text-2",
            StoryRef = "story-1",
            FrameRect = new RectPt(54f, 54f, 504f, 200f),
            ZOrder = 1,
        });
        page2.Blocks.Add(new WidgetBlock
        {
            Id = "widget-1",
            WidgetType = "officersTable",
            FrameRect = new RectPt(54f, 300f, 504f, 200f),
            ZOrder = 5,
            WrapMode = WrapMode.Rectangle,
            Data = JsonSerializer.Deserialize<JsonElement>(
                """{"officers":[{"position":"Worshipful Master","name":"A. Placeholder","phone":"555-0100"}]}"""),
        });
        doc.Pages.Add(page2);

        return doc;
    }

    public static TboardPackage BuildPackage()
    {
        var package = new TboardPackage { Document = BuildDocument() };
        package.Assets["img-fixture.png"] = [0x89, 0x50, 0x4E, 0x47, 0x01, 0x02, 0x03, 0x04];
        package.Thumbnails["page-1.png"] = [0x89, 0x50, 0x4E, 0x47, 0x05, 0x06];
        return package;
    }

    /// <summary>Canonical JSON of the full document (body + styles) for identity assertions.</summary>
    public static string Snapshot(Document doc)
    {
        var body = new DocumentBodyFile
        {
            Metadata = doc.Metadata,
            PageMasters = doc.PageMasters,
            Pages = doc.Pages,
            Stories = doc.Stories,
            ExtraProperties = doc.ExtraProperties,
        };
        var styles = new StylesFile { Theme = doc.Theme, StyleSheet = doc.StyleSheet };
        return JsonSerializer.Serialize(body, TboardJsonContext.Default.DocumentBodyFile)
            + "\n---\n"
            + JsonSerializer.Serialize(styles, TboardJsonContext.Default.StylesFile);
    }
}
