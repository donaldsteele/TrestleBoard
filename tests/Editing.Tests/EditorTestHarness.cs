using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Rendering;

namespace TrestleBoard.Editing.Tests;

/// <summary>Deterministic clock for coalescing-window tests (mirrors Core.Tests).</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan span) => _timestamp += (long)(span.TotalSeconds * TimestampFrequency);
}

internal sealed class FakeClipboard : ITextClipboard
{
    public string? Text { get; set; }

    public Task<string?> GetTextAsync() => Task.FromResult(Text);

    public Task SetTextAsync(string text)
    {
        Text = text;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Headless editing fixture (docs/M4-spec.md §8.3): a page with one text frame wrapping around
/// one exclusion rect, driven end-to-end through DocumentSession + DocumentRenderSource +
/// TextEditorController. All content fictional (PLAN.md §0).
/// </summary>
internal sealed class EditorTestHarness : IDisposable
{
    public const string StoryId = "story-1";
    public const string BlockId = "text-1";

    private static readonly Lazy<FontStore> Fonts = new(BundledFonts.CreateDefaultStore);

    public EditorTestHarness(
        string? initialText,
        int frameHeightPt = 420,
        bool withExclusion = true,
        Layout.Widgets.IWidgetLayoutProvider? widgets = null)
    {
        var doc = new Document();
        doc.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "body",
            FontFamily = BundledFonts.BodyFamily,
            SizePt = 12f,
        });
        doc.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "body-bold",
            FontFamily = BundledFonts.BodyFamily,
            Weight = FontWeightToken.Bold,
            SizePt = 12f,
        });
        doc.StyleSheet.ParagraphStyles.Add(new ParagraphStyleDef
        {
            Name = "body",
            CharacterStyleRef = "body",
            LineSpacing = 1.2f,
        });
        doc.StyleSheet.ParagraphStyles.Add(new ParagraphStyleDef
        {
            Name = "body-tight",
            CharacterStyleRef = "body",
            LineSpacing = 1.0f,
        });
        doc.PageMasters.Add(new PageMaster { Id = "master-1" });

        var story = new Story { Id = StoryId };
        story.Paragraphs.Add(new StoryParagraph
        {
            ParagraphStyleRef = "body",
            Runs = initialText is { Length: > 0 } ? [new StoryRun { Text = initialText }] : [],
        });
        doc.Stories.Add(story);

        var page = new Page { Id = "page-1", MasterRef = "master-1" };
        if (withExclusion)
        {
            page.Blocks.Add(new ImageFrame
            {
                Id = "img-1",
                AssetRef = "missing.png",
                FrameRect = new RectPt(260f, 120f, 140f, 110f),
                ZOrder = 10,
                WrapMode = WrapMode.Rectangle,
                WrapMarginPt = 8f,
                AltText = "Placeholder photo",
            });
        }

        page.Blocks.Add(new TextBlock
        {
            Id = BlockId,
            StoryRef = StoryId,
            FrameRect = new RectPt(54f, 54f, 400f, frameHeightPt),
            ZOrder = 1,
        });
        doc.Pages.Add(page);

        Clock = new ManualTimeProvider();
        Session = new DocumentSession(doc, Clock);
        Source = DocumentRenderSource.CreateEditable(
            doc, new Dictionary<string, byte[]>(), Fonts.Value, Session, options: null, widgets: widgets);
        Clipboard = new FakeClipboard();
        Controller = new TextEditorController(Session, Source, Clipboard);
        Frames = new FrameEditorController(Session, Source);
    }

    public DocumentSession Session { get; }

    public DocumentRenderSource Source { get; }

    public TextEditorController Controller { get; }

    /// <summary>M5 direct manipulation over the same document/session/render source.</summary>
    public FrameEditorController Frames { get; }

    public FakeClipboard Clipboard { get; }

    public ManualTimeProvider Clock { get; }

    public Story Story => Session.Document.GetStory(StoryId);

    public string ParagraphText(int index) =>
        Core.Text.StoryNavigator.GetParagraphText(Story.Paragraphs[index]);

    public string AllText() => string.Join(
        "\n", Story.Paragraphs.Select(Core.Text.StoryNavigator.GetParagraphText));

    /// <summary>Clicks inside the frame near its top-left text.</summary>
    public bool ClickIntoFrame(float xPt = 60f, float yPt = 60f) =>
        Controller.TryBeginAt(0, xPt, yPt);

    /// <summary>Types text one keystroke at a time with the clock advancing inside the
    /// coalescing window.</summary>
    public void TypeBurst(string text, TimeSpan? gap = null)
    {
        foreach (char c in text)
        {
            Clock.Advance(gap ?? TimeSpan.FromMilliseconds(80));
            Controller.InsertText(c.ToString());
        }
    }

    public void Dispose() => Source.Dispose();
}
