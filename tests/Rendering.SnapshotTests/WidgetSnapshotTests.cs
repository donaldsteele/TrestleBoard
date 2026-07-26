using System.Text.Json;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets;
using TrestleBoard.Widgets.Builtins.BirthdayList;
using TrestleBoard.Widgets.Builtins.CommitteeList;
using TrestleBoard.Widgets.Builtins.CoverBanner;
using TrestleBoard.Widgets.Builtins.DistrictCalendar;
using TrestleBoard.Widgets.Builtins.EventCard;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// M7 on the page (docs/M7-spec.md §10.1): all six widgets painted with fictional data, the
/// acceptance page with body text wrapping around the officers table, and the guard that says a
/// document rendered WITHOUT a widget provider still paints the pre-M7 placeholder.
/// All content fictional (PLAN.md §0). Per-OS baselines, strict gate.
/// </summary>
public sealed class WidgetSnapshotTests
{
    private static readonly WidgetLayoutProvider Widgets = WidgetLayoutProvider.CreateDefault();

    private const string Prose =
        "The Placeholder Lodge gathers on the appointed evening and the brothers arrive early for "
        + "supper. The Worshipful Master reminds everyone that the committee welcomes news from any "
        + "brother who has something to share, and that the deadline falls on the fifteenth of the "
        + "month. Visitors from the district are always welcome at the door, and a place is kept at "
        + "the table for anyone travelling through. The evening closes in the usual way, with thanks "
        + "to the stewards for their work in the kitchen and to the brothers who travelled far.";

    [Fact]
    public void WidgetGalleryMatchesBaseline() =>
        AssertMatchesBaseline("widgets-gallery-page1", GalleryDocument());

    [Fact]
    public void OfficersTableWrapMatchesBaseline() =>
        AssertMatchesBaseline("widget-officers-wrap", WrapDocument());

    /// <summary>
    /// The invariance guard on "the M3 baselines must not move": with no provider injected, a
    /// document full of widgets paints exactly the neutral placeholder it painted before M7.
    /// </summary>
    [Fact]
    public void WithNoProviderWidgetsStillPaintTheNeutralPlaceholder()
    {
        Document document = GalleryDocument();
        using DocumentRenderSource without = DocumentRenderSource.Create(
            document, new Dictionary<string, byte[]>(), SnapshotInfra.Store.Value);
        byte[] placeholders = DocumentSnapshotTests.RenderPagePng(without, 0);

        // Same document, same page, but every widget type replaced by one this build cannot know:
        // the unknown-type path must be pixel-identical to the no-provider path (§2.1).
        Document unknown = GalleryDocument();
        foreach (Block block in unknown.Pages[0].Blocks)
        {
            if (block is WidgetBlock widget)
            {
                widget.WidgetType = "memorialPanel";
            }
        }

        using DocumentRenderSource withProvider = DocumentRenderSource.Create(
            unknown, new Dictionary<string, byte[]>(), SnapshotInfra.Store.Value, options: null, widgets: Widgets);
        byte[] unknownTypes = DocumentSnapshotTests.RenderPagePng(withProvider, 0);

        Assert.Equal(placeholders, unknownTypes);
    }

    [Fact]
    public void EmptyWidgetsPromptOnScreenAndNeverInPrint()
    {
        Document document = EmptyWidgetDocument();
        using DocumentRenderSource source = DocumentRenderSource.Create(
            document, new Dictionary<string, byte[]>(), SnapshotInfra.Store.Value, options: null, widgets: Widgets);

        byte[] onScreen = DocumentSnapshotTests.RenderPagePng(source, 0);
        source.ShowEmptyPrompts = false;
        byte[] printed = DocumentSnapshotTests.RenderPagePng(source, 0);

        SnapshotInfra.ComparisonResult diff = SnapshotInfra.ComparePixels(onScreen, printed);
        Assert.True(diff.DiffPixelCount > 0, "the on-screen prompt should be visible");
    }

    [Fact]
    public void RenderingIsStableAcrossRepeatedPaints()
    {
        Document document = GalleryDocument();
        using DocumentRenderSource source = DocumentRenderSource.Create(
            document, new Dictionary<string, byte[]>(), SnapshotInfra.Store.Value, options: null, widgets: Widgets);

        Assert.Equal(
            DocumentSnapshotTests.RenderPagePng(source, 0),
            DocumentSnapshotTests.RenderPagePng(source, 0));
    }

    // ---- fixtures ----------------------------------------------------------------------------

    private static void AssertMatchesBaseline(string name, Document document)
    {
        using DocumentRenderSource source = DocumentRenderSource.Create(
            document, new Dictionary<string, byte[]>(), SnapshotInfra.Store.Value, options: null, widgets: Widgets);
        byte[] actual = DocumentSnapshotTests.RenderPagePng(source, 0);
        string baselinePath = Path.Combine(SnapshotInfra.BaselineDir, name + ".png");

        if (SnapshotInfra.UpdateBaselines)
        {
            Directory.CreateDirectory(SnapshotInfra.BaselineDir);
            File.WriteAllBytes(baselinePath, actual);
            return;
        }

        if (!File.Exists(baselinePath))
        {
            string candidate = SnapshotInfra.WriteActualArtifact(name, actual);
            Assert.Fail($"Baseline missing: {baselinePath}. Actual written to {candidate} " +
                        "(run with TRESTLEBOARD_UPDATE_BASELINES=1 or promote the CI artifact).");
        }

        byte[] expected = File.ReadAllBytes(baselinePath);
        SnapshotInfra.ComparisonResult diff = SnapshotInfra.ComparePixels(actual, expected);
        if (diff.DiffPixelCount > 0)
        {
            string artifact = SnapshotInfra.WriteActualArtifact(name, actual);
            Assert.Fail(
                $"Snapshot '{name}' differs from baseline: {diff.DiffPixelCount}/{diff.TotalPixels} pixels, "
                + $"max channel diff {diff.MaxChannelDiff}. Actual written to {artifact}");
        }
    }

    private static Document EmptyDocument()
    {
        var document = new Document();
        document.Metadata.LodgeName = "Placeholder Lodge No. 000";
        document.Metadata.IssueMonth = 7;
        document.Metadata.IssueYear = 2026;
        document.Metadata.MeetingRule = "1st Tuesday";
        document.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "body",
            FontFamily = Layout.Fonts.BundledFonts.BodyFamily,
            SizePt = 11f,
        });
        document.StyleSheet.ParagraphStyles.Add(new ParagraphStyleDef
        {
            Name = "body",
            CharacterStyleRef = "body",
            LineSpacing = 1.25f,
        });
        document.PageMasters.Add(new PageMaster { Id = "master-1" });
        return document;
    }

    /// <summary>One page carrying all six widgets — the styling regression net.</summary>
    private static Document GalleryDocument()
    {
        Document document = EmptyDocument();
        var page = new Page { Id = "page-1", MasterRef = "master-1" };

        page.Blocks.Add(Widget("w-cover", "coverBanner", new RectPt(54f, 54f, 504f, 120f), 1, Cover()));
        page.Blocks.Add(Widget("w-officers", "officersTable", new RectPt(54f, 190f, 250f, 250f), 2, Officers()));
        page.Blocks.Add(Widget("w-birthdays", "birthdayList", new RectPt(320f, 190f, 150f, 150f), 3, Birthdays()));
        page.Blocks.Add(Widget("w-district", "districtCalendar", new RectPt(320f, 355f, 238f, 130f), 4, District()));
        page.Blocks.Add(Widget("w-committees", "committeeList", new RectPt(54f, 460f, 250f, 110f), 5, Committees()));
        page.Blocks.Add(Widget("w-event", "eventCard", new RectPt(54f, 600f, 504f, 120f), 6, Event()));

        document.Pages.Add(page);
        return document;
    }

    /// <summary>The acceptance page: an officers table with body text flowing around it.</summary>
    private static Document WrapDocument()
    {
        Document document = EmptyDocument();
        var story = new Story { Id = "story-1" };
        story.Paragraphs.Add(new StoryParagraph
        {
            ParagraphStyleRef = "body",
            Runs = [new StoryRun { Text = Prose }],
        });
        document.Stories.Add(story);

        var page = new Page { Id = "page-1", MasterRef = "master-1" };
        page.Blocks.Add(new TextBlock
        {
            Id = "text-1",
            StoryRef = "story-1",
            FrameRect = new RectPt(54f, 54f, 504f, 500f),
            ZOrder = 1,
        });
        page.Blocks.Add(Widget(
            "w-officers", "officersTable", new RectPt(320f, 120f, 238f, 220f), 5, Officers()));
        document.Pages.Add(page);
        return document;
    }

    private static Document EmptyWidgetDocument()
    {
        Document document = EmptyDocument();
        var page = new Page { Id = "page-1", MasterRef = "master-1" };
        var seed = new WidgetSeed("", 7, 2026, "");
        foreach (IWidgetDefinition definition in BuiltInWidgets.All.Take(3))
        {
            page.Blocks.Add(Widget(
                $"w-{definition.TypeId}",
                definition.TypeId,
                new RectPt(54f, 54f + (page.Blocks.Count * 140f), 300f, 120f),
                page.Blocks.Count + 1,
                definition.WriteData(definition.CreateEmptyData(seed))));
        }

        document.Pages.Add(page);
        return document;
    }

    private static WidgetBlock Widget(string id, string type, RectPt rect, int z, JsonElement data) => new()
    {
        Id = id,
        WidgetType = type,
        DataVersion = 1,
        Data = data,
        FrameRect = rect,
        ZOrder = z,
        WrapMode = WrapMode.Rectangle,
        WrapMarginPt = 6f,
    };

    private static JsonElement Officers()
    {
        var definition = new OfficersTableDefinition();
        OfficersTableData data = definition.CreateEmpty(new WidgetSeed("", 1, 2026, ""));
        string[] names =
        [
            "A. Placeholder", "B. Sample", "C. Example", "D. Fictional", "E. Stand-in", "",
            "", "H. Sample", "I. Example", "J. Fictional", "K. Stand-in", "L. Nobody",
        ];
        for (int i = 0; i < data.Officers.Count; i++)
        {
            data.Officers[i].Name = names[i];
            if (names[i].Length > 0 && i % 3 != 2)
            {
                data.Officers[i].Phone = $"555-01{i:00}";
            }
        }

        return definition.WriteData(data);
    }

    private static JsonElement Birthdays()
    {
        var definition = new BirthdayListDefinition();
        var data = new BirthdayListData { ClosingNote = "Miss anyone? Let us know." };
        (string Name, int Month, int Day)[] rows =
        [
            ("A. Placeholder", 7, 4),
            ("B. Sample", 7, 11),
            ("C. Example", 7, 19),
            ("D. Fictional", 7, 23),
            ("E. Stand-in", 7, 30),
        ];
        foreach ((string name, int month, int day) in rows)
        {
            data.Entries.Add(new BirthdayEntry { Name = name, Month = month, Day = day });
        }

        return definition.WriteData(data);
    }

    private static JsonElement Committees()
    {
        var definition = new CommitteeListDefinition();
        var data = new CommitteeListData();
        data.Committees.Add(new CommitteeEntry
        {
            Name = "Security",
            Members = ["A. Placeholder", "B. Sample"],
            Note = "and team",
        });
        data.Committees.Add(new CommitteeEntry
        {
            Name = "Building",
            Members = ["C. Example", "D. Fictional", "E. Stand-in"],
        });
        return definition.WriteData(data);
    }

    private static JsonElement District()
    {
        var definition = new DistrictCalendarDefinition();
        var data = new DistrictCalendarData { Mode = DistrictCalendarMode.MeetingDays };
        data.Lodges.Add(new DistrictLodgeEntry { LodgeName = "Sample Lodge 000", MeetingRule = "2nd Monday" });
        data.Lodges.Add(new DistrictLodgeEntry { LodgeName = "Placeholder Lodge 111", MeetingRule = "1st Tuesday" });
        data.Lodges.Add(new DistrictLodgeEntry { LodgeName = "Example Lodge 222", MeetingRule = "3rd Thursday" });
        return definition.WriteData(data);
    }

    private static JsonElement Event()
    {
        var definition = new EventCardDefinition();
        return definition.WriteData(new EventCardData
        {
            Title = "Summer picnic",
            WhenText = "Saturday the eighteenth",
            WhereText = "The lodge grounds",
            BodyText = "Bring a dish to share. The stewards will look after everything else.",
        });
    }

    private static JsonElement Cover()
    {
        var definition = new CoverBannerDefinition();
        return definition.WriteData(new CoverBannerData
        {
            LodgeName = "Placeholder Lodge No. 000",
            HeadingText = "STATED COMMUNICATION",
            MeetingRule = "1st Tuesday",
            MeetingDateText = "July 7th",
            DinnerTimeText = "6:30",
            WorkTimeText = "7:30",
        });
    }
}
