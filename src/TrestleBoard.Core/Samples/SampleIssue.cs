using System.Text.Json;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Samples;

/// <summary>
/// A whole five-page trestle board — the M8 acceptance fixture (docs/M8-spec.md §6). It recreates
/// the STRUCTURE of a real issue: a cover with an essay that runs onto the next page, an officers
/// table with text flowing beside it, a narrow birthday column, the district calendar, and an
/// announcement box.
///
/// Every name, number and date here is FICTIONAL (PLAN.md §0). Widget payloads are written as raw
/// JSON because that is exactly how a .tboard stores them, and because Core must not reach into the
/// widget layer to build a fixture.
/// </summary>
public static class SampleIssue
{
    public const string PhotoAssetName = "img-issue-photo.jpg";

    public const string CoverEssay =
        "Brethren, the summer months bring their own rhythm to the lodge. Attendance thins as "
        + "families travel, and yet the work continues much as it always has. The stewards keep "
        + "the kitchen going, the secretary keeps the minutes, and someone always remembers to "
        + "prop the side door open when the evening turns warm. "
        + "There is a lesson in that steadiness. A lodge is not built out of its busiest nights. "
        + "It is built out of the ordinary ones, when a handful of brothers turn up because they "
        + "said they would, and the work gets done because it is there to be done. "
        + "I am grateful to every brother who has taken a turn this year, and to those who have "
        + "quietly filled a gap without being asked. The committee welcomes news from anyone with "
        + "something to share; the deadline is the fifteenth of the month, and nothing is too small "
        + "to be worth printing. A note about a brother recovering at home matters more to the man "
        + "reading it than any essay I could write. "
        + "Our next stated communication falls on the first Tuesday, as always. Supper is at half "
        + "past six and the lodge opens at half past seven. Visitors from around the district are "
        + "always welcome, and a place is kept at the table for anyone travelling through. "
        + "A word about the building. The dining hall repairs are further along than they look "
        + "from the doorway, and the committee expects the plaster to be finished before the "
        + "autumn. The cost has run a little over what we hoped, which surprises nobody who has "
        + "owned an old building, and the charity fund has absorbed the difference for now. "
        + "We will bring a full accounting to the September communication so that every brother "
        + "can see where the money went and say his piece about it. "
        + "Finally, a reminder that the canned goods basket sits by the door through the summer. "
        + "The community pantry runs short in July and August when the schools are closed, and a "
        + "tin carried in on a Tuesday evening costs a brother very little and does a great deal.";

    public const string ClosingPiece =
        "The traveling gavel remains with our brethren to the north after their visit in the "
        + "spring, and the committee would dearly like it back. Any brother willing to make the "
        + "drive should speak to the Senior Warden, who has been keeping a list and is not shy "
        + "about using it. "
        + "Thanks are due to the stewards for their work in the kitchen through a warm month, and "
        + "to the brothers who travelled a long way to be with us.";

    /// <summary>Builds the issue. Pass JPEG bytes for the cover photo, or null for the placeholder path.</summary>
    public static TboardPackage CreatePackage(byte[]? photoJpeg = null)
    {
        var document = new Document
        {
            Metadata = new DocumentMetadata
            {
                LodgeName = "Placeholder Lodge No. 000",
                IssueMonth = 7,
                IssueYear = 2026,
                Title = "Placeholder Lodge Trestle Board — July 2026",
                MeetingRule = "1st Tuesday",
            },
        };

        AddStyles(document);
        document.PageMasters.Add(new PageMaster { Id = "master-letter" });

        var essay = new Story { Id = "story-essay" };
        foreach (string paragraph in Split(CoverEssay))
        {
            essay.Paragraphs.Add(Para("body", paragraph));
        }

        document.Stories.Add(essay);

        var closing = new Story { Id = "story-closing" };
        foreach (string paragraph in Split(ClosingPiece))
        {
            closing.Paragraphs.Add(Para("body", paragraph));
        }

        document.Stories.Add(closing);

        // ---- page 1: cover heading, the essay, a photo it flows around ---------------------------
        var page1 = new Page { Id = "page-1", MasterRef = "master-letter" };
        page1.Blocks.Add(Widget("w-cover", "coverBanner", new RectPt(54f, 54f, 504f, 130f), 1, CoverBannerData));
        page1.Blocks.Add(new TextBlock
        {
            Id = "frame-essay-1",
            StoryRef = "story-essay",
            FrameRect = new RectPt(54f, 200f, 504f, 400f),
            ZOrder = 2,
            LinkNext = "frame-essay-2",
        });
        page1.Blocks.Add(new ImageFrame
        {
            Id = "img-cover",
            AssetRef = PhotoAssetName,
            FrameRect = new RectPt(300f, 300f, 258f, 190f),
            ZOrder = 5,
            WrapMode = WrapMode.Rectangle,
            WrapMarginPt = 8f,
            AltText = "Brothers gathered outside the lodge on a summer evening",
            Caption = "A warm evening at the lodge.",
        });
        document.Pages.Add(page1);

        // ---- page 2: the essay continues beside the officers table -------------------------------
        var page2 = new Page { Id = "page-2", MasterRef = "master-letter" };
        page2.Blocks.Add(new TextBlock
        {
            Id = "frame-essay-2",
            StoryRef = "story-essay",
            FrameRect = new RectPt(54f, 54f, 504f, 684f),
            ZOrder = 1,
        });
        page2.Blocks.Add(Widget("w-officers", "officersTable", new RectPt(310f, 54f, 248f, 260f), 5, OfficersData));
        document.Pages.Add(page2);

        // ---- page 3: birthdays as a narrow column, committees below ------------------------------
        var page3 = new Page { Id = "page-3", MasterRef = "master-letter" };
        page3.Blocks.Add(Widget("w-birthdays", "birthdayList", new RectPt(54f, 54f, 150f, 200f), 5, BirthdaysData));
        page3.Blocks.Add(Widget("w-committees", "committeeList", new RectPt(54f, 300f, 504f, 150f), 4, CommitteesData));
        document.Pages.Add(page3);

        // ---- page 4: the district calendar and an announcement -----------------------------------
        var page4 = new Page { Id = "page-4", MasterRef = "master-letter" };
        page4.Blocks.Add(Widget("w-district", "districtCalendar", new RectPt(54f, 54f, 504f, 320f), 1, DistrictData));
        page4.Blocks.Add(Widget("w-event", "eventCard", new RectPt(54f, 420f, 504f, 140f), 2, EventData));
        document.Pages.Add(page4);

        // ---- page 5: a closing piece and the submissions box --------------------------------------
        var page5 = new Page { Id = "page-5", MasterRef = "master-letter" };
        page5.Blocks.Add(new TextBlock
        {
            Id = "frame-closing",
            StoryRef = "story-closing",
            FrameRect = new RectPt(54f, 54f, 504f, 420f),
            ZOrder = 1,
        });
        page5.Blocks.Add(Widget("w-submissions", "eventCard", new RectPt(54f, 520f, 504f, 130f), 2, SubmissionsData));
        document.Pages.Add(page5);

        var package = new TboardPackage { Document = document };
        if (photoJpeg is not null)
        {
            package.Assets[PhotoAssetName] = photoJpeg;
        }

        return package;
    }

    // ---- widget payloads (raw JSON, exactly as the container stores them) -------------------------

    private const string CoverBannerData = """
        {
          "lodgeName": "Placeholder Lodge No. 000",
          "headingText": "STATED COMMUNICATION",
          "meetingRule": "1st Tuesday",
          "meetingDateText": "July 7th",
          "dinnerTimeText": "6:30",
          "workTimeText": "7:30"
        }
        """;

    private const string OfficersData = """
        {
          "heading": "2026 Lodge Officers",
          "vacantText": "(vacant)",
          "officers": [
            { "position": "Worshipful Master", "name": "A. Placeholder", "phone": "555-0100" },
            { "position": "Senior Warden", "name": "B. Sample", "phone": "555-0101" },
            { "position": "Junior Warden", "name": "C. Example", "phone": "555-0102" },
            { "position": "Senior Deacon", "name": "D. Fictional", "phone": "555-0103" },
            { "position": "Junior Deacon", "name": "E. Stand-in", "phone": "555-0104" },
            { "position": "Senior Steward", "name": "" },
            { "position": "Junior Steward", "name": "" },
            { "position": "Treasurer", "name": "H. Sample", "phone": "555-0107" },
            { "position": "Secretary", "name": "I. Example", "phone": "555-0108" },
            { "position": "Chaplain", "name": "J. Fictional" },
            { "position": "Tiler", "name": "K. Stand-in", "phone": "555-0110" },
            { "position": "Marshall", "name": "L. Nobody", "phone": "555-0111" }
          ]
        }
        """;

    private const string BirthdaysData = """
        {
          "heading": "Happy Birthday, Brothers",
          "closingNote": "Miss anyone? Let us know.",
          "entries": [
            { "name": "A. Placeholder", "month": 7, "day": 4 },
            { "name": "B. Sample", "month": 7, "day": 9 },
            { "name": "C. Example", "month": 7, "day": 11 },
            { "name": "D. Fictional", "month": 7, "day": 16 },
            { "name": "E. Stand-in", "month": 7, "day": 21 },
            { "name": "F. Nobody", "month": 7, "day": 28 }
          ]
        }
        """;

    private const string CommitteesData = """
        {
          "heading": "2026 Committees",
          "noticeText": "Please notify the Worshipful Master to be added or removed.",
          "committees": [
            { "name": "Security", "members": ["A. Placeholder"], "note": "and team" },
            { "name": "Building", "members": ["B. Sample", "C. Example", "D. Fictional"] },
            { "name": "Ritual", "members": ["E. Stand-in", "F. Nobody"] },
            { "name": "Education", "members": ["G. Placeholder", "H. Sample"] }
          ]
        }
        """;

    private const string DistrictData = """
        {
          "heading": "22nd District",
          "mode": "both",
          "lodgesHeading": "Meeting days",
          "eventsHeading": "Coming up",
          "lodges": [
            { "lodgeName": "Sample Lodge 000", "meetingRule": "1st Monday" },
            { "lodgeName": "Placeholder Lodge No. 000", "meetingRule": "1st Tuesday" },
            { "lodgeName": "Example Lodge 111", "meetingRule": "2nd Monday" },
            { "lodgeName": "Fictional Lodge 222", "meetingRule": "2nd Tuesday" },
            { "lodgeName": "Stand-in Lodge 333", "meetingRule": "3rd Thursday" }
          ],
          "events": [
            { "dateText": "July 12", "hostText": "Sample Lodge 000", "description": "Masters and Wardens meeting", "timesText": "Meal 6:30, work 7:30" },
            { "dateText": "July 19", "hostText": "Example Lodge 111", "description": "Third degree, outdoor" },
            { "dateText": "July 26", "description": "Wardens workshop" }
          ]
        }
        """;

    private const string EventData = """
        {
          "title": "Summer Picnic",
          "whenText": "Saturday the eighteenth, from noon",
          "whereText": "The lodge grounds",
          "bodyText": "Bring a dish to share and a chair. The stewards will look after everything else. Families are very welcome.",
          "showBorder": true
        }
        """;

    private const string SubmissionsData = """
        {
          "title": "Something for next month?",
          "whenText": "Deadline: the fifteenth",
          "bodyText": "Send news, notices and photographs to the trestle board committee. Nothing is too small to be worth printing.",
          "showBorder": true
        }
        """;

    // ---- helpers ---------------------------------------------------------------------------------

    private static WidgetBlock Widget(string id, string type, RectPt rect, int z, string json) => new()
    {
        Id = id,
        WidgetType = type,
        DataVersion = 1,
        Data = JsonSerializer.Deserialize<JsonElement>(json),
        FrameRect = rect,
        ZOrder = z,
        WrapMode = WrapMode.Rectangle,
        WrapMarginPt = 8f,
    };

    private static StoryParagraph Para(string styleRef, string text) => new()
    {
        ParagraphStyleRef = styleRef,
        Runs = [new StoryRun { Text = text }],
    };

    /// <summary>Splits the essays into paragraphs on the doubled space they were written with.</summary>
    private static string[] Split(string text) =>
        text.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void AddStyles(Document document)
    {
        document.StyleSheet.CharacterStyles.AddRange(
        [
            new CharacterStyleDef { Name = "body", FontFamily = "Source Serif 4", SizePt = 11f },
            new CharacterStyleDef { Name = "body-bold", FontFamily = "Source Serif 4", Weight = FontWeightToken.Bold, SizePt = 11f },
            new CharacterStyleDef { Name = "table", FontFamily = "Source Sans 3", SizePt = 10f },
            new CharacterStyleDef { Name = "table-header", FontFamily = "Source Sans 3", Weight = FontWeightToken.Bold, SizePt = 12f },
        ]);
        document.StyleSheet.ParagraphStyles.AddRange(
        [
            new ParagraphStyleDef { Name = "body", CharacterStyleRef = "body", LineSpacing = 1.3f, SpaceAfterPt = 7f, FirstLineIndentPt = 14f },
        ]);
        document.StyleSheet.TableStyles.Add(new TableStyleDef
        {
            Name = "lodge-table",
            HeaderCharacterStyleRef = "table-header",
            BodyCharacterStyleRef = "table",
            RuleArgb = 0xFF8A8A8A,
            RuleWidthPt = 0.5f,
        });
    }
}
