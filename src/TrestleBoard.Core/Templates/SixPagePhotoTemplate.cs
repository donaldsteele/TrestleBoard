using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Templates;

/// <summary>
/// "6-page with photos" (docs/M9-spec.md §2, PLAN.md §7): Classic 414's cover, officers page and
/// birthdays sidebar, plus two photo-led pages and a closing page.
///
/// The spec names the shape ("Classic plus two photo-led spreads") but does not lay out page 4-6
/// block by block, so pages 4-5 are the photo-led additions and page 6 closes the issue the way
/// <see cref="Samples.SampleIssue"/>'s page 5 does (a closing note plus a boxed announcement) — a
/// judgment call, flagged in the M9 report rather than left implicit.
///
/// The photo frames carry no asset bytes: a template ships before the user has a photo to put in
/// it, and an <see cref="ImageFrame.AssetRef"/> with no matching entry in
/// <see cref="TboardPackage.Assets"/> is already a supported state — <see cref="Samples.SampleDocument.CreatePackage"/>
/// documents the same "missing asset placeholder" rendering path for exactly this reason.
/// </summary>
internal static class SixPagePhotoTemplate
{
    public const string TemplateId = "six-page-photos";

    public static readonly TemplateInfo Info = new(
        TemplateId,
        "6-page with photos",
        "Classic 414 plus two photo-led pages, for an issue with a lot to show.",
        PageCount: 6);

    public const string CoverEssayPrompt = "Write the Worshipful Master's message here…";
    public const string PhotoPrompt = "Write about this photo here…";
    public const string ClosingPrompt = "Write a closing note here…";
    public const string PhotoAltTextPrompt = "Write a description of this photo here…";

    public static TboardPackage Build()
    {
        var document = new Document();
        TemplateHelpers.AddStandardStyles(document);
        document.PageMasters.Add(new PageMaster { Id = "master-letter" });
        document.Stories.Add(TemplateHelpers.PromptStory("story-cover", CoverEssayPrompt));
        document.Stories.Add(TemplateHelpers.PromptStory("story-photo-a", PhotoPrompt));
        document.Stories.Add(TemplateHelpers.PromptStory("story-photo-b", PhotoPrompt));
        document.Stories.Add(TemplateHelpers.PromptStory("story-closing", ClosingPrompt));

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

        // ---- page 2: officers table --------------------------------------------------------------
        var page2 = new Page { Id = "page-2", MasterRef = "master-letter" };
        page2.Blocks.Add(TemplateHelpers.Widget(
            "w-officers", "officersTable", new RectPt(54f, 54f, 504f, 320f), 1, TemplateHelpers.EmptyOfficersTableJson));
        document.Pages.Add(page2);

        // ---- page 3: birthdays sidebar ------------------------------------------------------------
        var page3 = new Page { Id = "page-3", MasterRef = "master-letter" };
        page3.Blocks.Add(TemplateHelpers.Widget(
            "w-birthdays", "birthdayList", new RectPt(54f, 54f, 150f, 300f), 1, TemplateHelpers.EmptyBirthdayListJson));
        document.Pages.Add(page3);

        // ---- page 4: photo-led page A ---------------------------------------------------------------
        var page4 = new Page { Id = "page-4", MasterRef = "master-letter" };
        page4.Blocks.Add(new ImageFrame
        {
            Id = "img-4",
            AssetRef = "photo-placeholder-1.jpg",
            FrameRect = new RectPt(54f, 54f, 504f, 300f),
            ZOrder = 5,
            WrapMode = WrapMode.Rectangle,
            WrapMarginPt = 8f,
            AltText = PhotoAltTextPrompt,
        });
        page4.Blocks.Add(new TextBlock
        {
            Id = "frame-photo-a",
            StoryRef = "story-photo-a",
            FrameRect = new RectPt(54f, 370f, 504f, 368f),
            ZOrder = 1,
        });
        document.Pages.Add(page4);

        // ---- page 5: photo-led page B ---------------------------------------------------------------
        var page5 = new Page { Id = "page-5", MasterRef = "master-letter" };
        page5.Blocks.Add(new ImageFrame
        {
            Id = "img-5",
            AssetRef = "photo-placeholder-2.jpg",
            FrameRect = new RectPt(54f, 54f, 504f, 300f),
            ZOrder = 5,
            WrapMode = WrapMode.Rectangle,
            WrapMarginPt = 8f,
            AltText = PhotoAltTextPrompt,
        });
        page5.Blocks.Add(new TextBlock
        {
            Id = "frame-photo-b",
            StoryRef = "story-photo-b",
            FrameRect = new RectPt(54f, 370f, 504f, 368f),
            ZOrder = 1,
        });
        document.Pages.Add(page5);

        // ---- page 6: closing note + a boxed announcement -----------------------------------------
        var page6 = new Page { Id = "page-6", MasterRef = "master-letter" };
        page6.Blocks.Add(new TextBlock
        {
            Id = "frame-closing",
            StoryRef = "story-closing",
            FrameRect = new RectPt(54f, 54f, 504f, 420f),
            ZOrder = 1,
        });
        page6.Blocks.Add(TemplateHelpers.Widget(
            "w-announcement", "eventCard", new RectPt(54f, 500f, 504f, 150f), 2, TemplateHelpers.EmptyEventCardJson));
        document.Pages.Add(page6);

        return new TboardPackage { Document = document };
    }
}
