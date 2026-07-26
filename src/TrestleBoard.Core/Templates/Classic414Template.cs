using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Templates;

/// <summary>
/// "Classic 414" (docs/M9-spec.md §2, PLAN.md §7): the shape the committee already knows — a cover
/// banner with an essay, a dedicated officers page, and a birthdays sidebar. Every field is a
/// PROMPT or EMPTY (PLAN.md §0): this is a shipped artefact, never a filled-in fixture like
/// <see cref="Samples.SampleIssue"/>.
/// </summary>
internal static class Classic414Template
{
    public const string TemplateId = "classic-414";

    public static readonly TemplateInfo Info = new(
        TemplateId,
        "Classic 414",
        "The look the committee already knows: a cover banner and message, an officers page, and a birthdays sidebar.",
        PageCount: 3);

    public const string CoverEssayPrompt = "Write the Worshipful Master's message here…";

    public static TboardPackage Build()
    {
        var document = new Document();
        TemplateHelpers.AddStandardStyles(document);
        document.PageMasters.Add(new PageMaster { Id = "master-letter" });
        document.Stories.Add(TemplateHelpers.PromptStory("story-essay", CoverEssayPrompt));

        // ---- page 1: cover banner + the message ---------------------------------------------------
        var page1 = new Page { Id = "page-1", MasterRef = "master-letter" };
        page1.Blocks.Add(TemplateHelpers.Widget(
            "w-cover", "coverBanner", new RectPt(54f, 54f, 504f, 130f), 1, TemplateHelpers.EmptyCoverBannerJson));
        page1.Blocks.Add(new TextBlock
        {
            Id = "frame-essay",
            StoryRef = "story-essay",
            FrameRect = new RectPt(54f, 210f, 504f, 528f),
            ZOrder = 2,
        });
        document.Pages.Add(page1);

        // ---- page 2: officers table ----------------------------------------------------------------
        var page2 = new Page { Id = "page-2", MasterRef = "master-letter" };
        page2.Blocks.Add(TemplateHelpers.Widget(
            "w-officers", "officersTable", new RectPt(54f, 54f, 504f, 320f), 1, TemplateHelpers.EmptyOfficersTableJson));
        document.Pages.Add(page2);

        // ---- page 3: birthdays, as a narrow sidebar column ------------------------------------------
        var page3 = new Page { Id = "page-3", MasterRef = "master-letter" };
        page3.Blocks.Add(TemplateHelpers.Widget(
            "w-birthdays", "birthdayList", new RectPt(54f, 54f, 150f, 300f), 1, TemplateHelpers.EmptyBirthdayListJson));
        document.Pages.Add(page3);

        return new TboardPackage { Document = document };
    }
}
