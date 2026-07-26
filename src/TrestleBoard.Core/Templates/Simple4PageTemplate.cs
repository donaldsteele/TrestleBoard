using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Templates;

/// <summary>
/// "Simple 4-page" (docs/M9-spec.md §2, PLAN.md §7): the fewest moving parts — a cover, two plain
/// text pages, and a back page with one announcement box.
/// </summary>
internal static class Simple4PageTemplate
{
    public const string TemplateId = "simple-4-page";

    public static readonly TemplateInfo Info = new(
        TemplateId,
        "Simple 4-page",
        "The fewest moving parts: a cover, two plain text pages, and a back page with an announcement.",
        PageCount: 4);

    public const string CoverEssayPrompt = "Write the Worshipful Master's message here…";
    public const string Page2Prompt = "Write this month's news here…";
    public const string Page3Prompt = "Write more news here…";

    public static TboardPackage Build()
    {
        var document = new Document();
        TemplateHelpers.AddStandardStyles(document);
        document.PageMasters.Add(new PageMaster { Id = "master-letter" });
        document.Stories.Add(TemplateHelpers.PromptStory("story-cover", CoverEssayPrompt));
        document.Stories.Add(TemplateHelpers.PromptStory("story-page2", Page2Prompt));
        document.Stories.Add(TemplateHelpers.PromptStory("story-page3", Page3Prompt));

        // ---- page 1: cover ---------------------------------------------------------------------
        var page1 = new Page { Id = "page-1", MasterRef = "master-letter" };
        page1.Blocks.Add(TemplateHelpers.Widget(
            "w-cover", "coverBanner", new RectPt(54f, 54f, 504f, 130f), 1, TemplateHelpers.EmptyCoverBannerJson));
        page1.Blocks.Add(new TextBlock
        {
            Id = "frame-cover-essay",
            StoryRef = "story-cover",
            FrameRect = new RectPt(54f, 210f, 504f, 528f),
            ZOrder = 2,
        });
        document.Pages.Add(page1);

        // ---- pages 2-3: plain text pages ---------------------------------------------------------
        var page2 = new Page { Id = "page-2", MasterRef = "master-letter" };
        page2.Blocks.Add(new TextBlock
        {
            Id = "frame-page2",
            StoryRef = "story-page2",
            FrameRect = new RectPt(54f, 54f, 504f, 684f),
            ZOrder = 1,
        });
        document.Pages.Add(page2);

        var page3 = new Page { Id = "page-3", MasterRef = "master-letter" };
        page3.Blocks.Add(new TextBlock
        {
            Id = "frame-page3",
            StoryRef = "story-page3",
            FrameRect = new RectPt(54f, 54f, 504f, 684f),
            ZOrder = 1,
        });
        document.Pages.Add(page3);

        // ---- page 4: back page, an announcement box only -------------------------------------------
        var page4 = new Page { Id = "page-4", MasterRef = "master-letter" };
        page4.Blocks.Add(TemplateHelpers.Widget(
            "w-announcement", "eventCard", new RectPt(54f, 54f, 504f, 200f), 1, TemplateHelpers.EmptyEventCardJson));
        document.Pages.Add(page4);

        return new TboardPackage { Document = document };
    }
}
