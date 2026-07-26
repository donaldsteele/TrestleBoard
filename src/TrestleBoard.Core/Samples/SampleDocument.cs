using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Samples;

/// <summary>
/// Code-generated fixture approximating a real trestle-board page (PLAN.md §11 M3): cover
/// heading, photo with text wrapped around it, an officers table-style box, and story spill
/// onto page 2. ALL data is fictional (PLAN.md §0) — placeholders only, never real people.
/// </summary>
public static class SampleDocument
{
    public const string PhotoAssetName = "img-sample-photo.png";

    public const string BodyProse =
        "The Placeholder Lodge convenes on the appointed evening of every month. "
        + "Brothers gather early for fellowship, share a simple meal together, and then "
        + "proceed to the reading of the minutes. Visitors are always welcome to attend "
        + "the open portions of the program and to enjoy the refreshments afterward. "
        + "The building committee reports steady progress on the dining hall repairs, "
        + "and the charity fund drive continues through the end of the month. "
        + "Members are reminded to bring canned goods for the community pantry basket.";

    /// <summary>Builds the sample package. Pass PNG bytes for the photo frame, or null to
    /// exercise the renderer's missing-asset placeholder path.</summary>
    public static TboardPackage CreatePackage(byte[]? photoPng = null)
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
        };

        doc.StyleSheet.CharacterStyles.AddRange(
        [
            new CharacterStyleDef { Name = "display", FontFamily = "Cinzel", SizePt = 26f },
            new CharacterStyleDef { Name = "subtitle", FontFamily = "Source Sans 3", SizePt = 14f },
            new CharacterStyleDef { Name = "body", FontFamily = "Source Serif 4", SizePt = 12f },
            new CharacterStyleDef { Name = "body-bold", FontFamily = "Source Serif 4", Weight = FontWeightToken.Bold, SizePt = 12f },
            new CharacterStyleDef { Name = "table", FontFamily = "Source Sans 3", SizePt = 12f },
            new CharacterStyleDef { Name = "table-header", FontFamily = "Source Sans 3", Weight = FontWeightToken.Bold, SizePt = 13f },
        ]);
        doc.StyleSheet.ParagraphStyles.AddRange(
        [
            new ParagraphStyleDef { Name = "display", CharacterStyleRef = "display", Align = TextAlignment.Center, LineSpacing = 1.1f, SpaceAfterPt = 4f },
            new ParagraphStyleDef { Name = "subtitle", CharacterStyleRef = "subtitle", Align = TextAlignment.Center, LineSpacing = 1.2f, SpaceAfterPt = 2f },
            new ParagraphStyleDef { Name = "body", CharacterStyleRef = "body", LineSpacing = 1.25f, SpaceAfterPt = 6f, FirstLineIndentPt = 14f },
            new ParagraphStyleDef { Name = "table-row", CharacterStyleRef = "table", LineSpacing = 1.35f },
            new ParagraphStyleDef { Name = "table-header", CharacterStyleRef = "table-header", LineSpacing = 1.35f, SpaceAfterPt = 3f },
        ]);

        doc.PageMasters.Add(new PageMaster { Id = "master-letter" });

        var heading = new Story { Id = "story-heading" };
        heading.Paragraphs.Add(Para("display", "Placeholder Lodge No. 000"));
        heading.Paragraphs.Add(Para("subtitle", "STATED COMMUNICATION — First Tuesday of Every Month"));
        heading.Paragraphs.Add(Para("subtitle", "Dinner 6:30 · Lodge Opens 7:30"));
        doc.Stories.Add(heading);

        var body = new Story { Id = "story-body" };
        body.Paragraphs.Add(new StoryParagraph
        {
            ParagraphStyleRef = "body",
            Runs =
            [
                new StoryRun { Text = "From the East. ", CharacterStyleRef = "body-bold" },
                new StoryRun { Text = BodyProse },
            ],
        });
        body.Paragraphs.Add(Para("body", BodyProse));
        body.Paragraphs.Add(Para("body", BodyProse));
        doc.Stories.Add(body);

        var officers = new Story { Id = "story-officers" };
        officers.Paragraphs.Add(Para("table-header", "Lodge Officers"));
        officers.Paragraphs.Add(Para("table-row", "Worshipful Master — A. Placeholder — 555-0100"));
        officers.Paragraphs.Add(Para("table-row", "Senior Warden — B. Sample — 555-0101"));
        officers.Paragraphs.Add(Para("table-row", "Junior Warden — C. Example — 555-0102"));
        officers.Paragraphs.Add(Para("table-row", "Secretary — D. Fictional — 555-0103"));
        doc.Stories.Add(officers);

        var page1 = new Page { Id = "page-1", MasterRef = "master-letter" };
        page1.Blocks.Add(new TextBlock
        {
            Id = "block-heading",
            StoryRef = "story-heading",
            FrameRect = new RectPt(54f, 40f, 504f, 110f),
            ZOrder = 1,
        });
        page1.Blocks.Add(new ShapeBlock
        {
            Id = "block-heading-rule",
            Kind = ShapeKind.Rule,
            FrameRect = new RectPt(54f, 152f, 504f, 1.5f),
            FillArgb = 0xFF1A1A1A,
        });
        page1.Blocks.Add(new ImageFrame
        {
            Id = "block-photo",
            AssetRef = PhotoAssetName,
            FrameRect = new RectPt(338f, 180f, 220f, 150f),
            ZOrder = 10,
            WrapMode = WrapMode.Rectangle,
            WrapMarginPt = 10f,
            AltText = "Placeholder photo of the lodge dining hall",
        });
        page1.Blocks.Add(new TextBlock
        {
            Id = "block-body-1",
            StoryRef = "story-body",
            FrameRect = new RectPt(54f, 170f, 504f, 320f),
            ZOrder = 1,
            LinkNext = "block-body-2",
        });
        page1.Blocks.Add(new ShapeBlock
        {
            Id = "block-officers-box",
            Kind = ShapeKind.Box,
            FrameRect = new RectPt(54f, 520f, 504f, 150f),
            FillArgb = 0xFFF4F1EA,
            StrokeArgb = 0xFF1A1A1A,
            StrokeWidthPt = 1f,
        });
        page1.Blocks.Add(new TextBlock
        {
            Id = "block-officers",
            StoryRef = "story-officers",
            FrameRect = new RectPt(66f, 530f, 480f, 132f),
            ZOrder = 2,
        });
        doc.Pages.Add(page1);

        var page2 = new Page { Id = "page-2", MasterRef = "master-letter" };
        page2.Blocks.Add(new TextBlock
        {
            Id = "block-body-2",
            StoryRef = "story-body",
            FrameRect = new RectPt(54f, 54f, 504f, 680f),
            ZOrder = 1,
        });
        doc.Pages.Add(page2);

        var package = new TboardPackage { Document = doc };
        if (photoPng is not null)
        {
            package.Assets[PhotoAssetName] = photoPng;
        }

        return package;
    }

    private static StoryParagraph Para(string styleRef, string text) => new()
    {
        ParagraphStyleRef = styleRef,
        Runs = [new StoryRun { Text = text }],
    };
}
