using System.Diagnostics;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Editing;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Rendering;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M5 acceptance (PLAN.md §11-M5): "drag an image frame through a text column → live reflow at
/// 60fps on a 6-page doc". Two gates: a deterministic one (a page-local drag only re-lays-out
/// that page's stories — docs/M5-spec.md §4.2) and a wall-clock one (median drag step inside a
/// 16ms frame budget).
/// </summary>
public sealed class LiveReflowPerformanceTests : IDisposable
{
    private const int PageCount = 6;
    private const string DraggedImageId = "img-p0";

    private static readonly Lazy<FontStore> Fonts = new(BundledFonts.CreateDefaultStore);

    private readonly DocumentSession _session;
    private readonly DocumentRenderSource _source;
    private readonly FrameEditorController _frames;

    public LiveReflowPerformanceTests()
    {
        Document document = BuildSixPageDocument();
        _session = new DocumentSession(document);
        _source = DocumentRenderSource.CreateEditable(
            document, new Dictionary<string, byte[]>(), Fonts.Value, _session);
        _frames = new FrameEditorController(_session, _source);
    }

    /// <summary>Six pages, each a full text column with a wrapping photo over it. Fictional
    /// content only (PLAN.md §0).</summary>
    private static Document BuildSixPageDocument()
    {
        var doc = new Document();
        doc.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "body",
            FontFamily = BundledFonts.BodyFamily,
            SizePt = 11f,
        });
        doc.StyleSheet.ParagraphStyles.Add(new ParagraphStyleDef
        {
            Name = "body",
            CharacterStyleRef = "body",
            LineSpacing = 1.25f,
            SpaceAfterPt = 6f,
        });
        doc.PageMasters.Add(new PageMaster { Id = "master-1" });

        string prose = string.Concat(Enumerable.Repeat(
            "The Placeholder Lodge gathers on the appointed evening for fellowship and a simple "
            + "meal, after which the minutes are read and the committee reports are heard. ",
            18));

        for (int p = 0; p < PageCount; p++)
        {
            var story = new Story { Id = $"story-p{p}" };
            story.Paragraphs.Add(new StoryParagraph
            {
                ParagraphStyleRef = "body",
                Runs = [new StoryRun { Text = prose }],
            });
            doc.Stories.Add(story);

            var page = new Page { Id = $"page-{p}", MasterRef = "master-1" };
            page.Blocks.Add(new TextBlock
            {
                Id = $"text-p{p}",
                StoryRef = story.Id,
                FrameRect = new RectPt(54f, 54f, 504f, 684f),
                ZOrder = 1,
            });
            page.Blocks.Add(new ImageFrame
            {
                Id = $"img-p{p}",
                AssetRef = "missing.png",
                FrameRect = new RectPt(300f, 120f, 200f, 150f),
                ZOrder = 10,
                WrapMode = WrapMode.Rectangle,
                WrapMarginPt = 8f,
                AltText = "Placeholder photo",
            });
            doc.Pages.Add(page);
        }

        return doc;
    }

    /// <summary>Forces the lazy layout the way a paint would.</summary>
    private void Paint()
    {
        for (int p = 0; p < PageCount; p++)
        {
            Assert.True(_source.TryGetStoryGeometry($"story-p{p}", out _));
        }
    }

    [Fact]
    public void DraggingAPhotoOnlyRelaysOutItsOwnPage()
    {
        Paint();
        int baseline = _source.StoryLayoutCount;
        Assert.Equal(PageCount, baseline); // one layout per story on the first pass

        _frames.Select(DraggedImageId);
        _frames.BeginDrag(FrameHandle.Body, 400f, 200f);
        for (int step = 1; step <= 30; step++)
        {
            _frames.DragTo(400f, 200f + (step * 8f), snap: false);
            Paint();
        }

        _frames.EndDrag(commit: true);
        Paint();

        // 30 drag steps + the commit, each re-laying-out exactly the one story on page 1.
        Assert.Equal(baseline + 31, _source.StoryLayoutCount);
    }

    [Fact]
    public void SixtyDragStepsStayInsideTheFrameBudget()
    {
        Paint();
        _frames.Select(DraggedImageId);
        _frames.BeginDrag(FrameHandle.Body, 400f, 200f);

        // Warm up the shaper/font caches; the acceptance is about steady-state dragging.
        for (int i = 0; i < 5; i++)
        {
            _frames.DragTo(400f, 200f + i, snap: false);
            Paint();
        }

        var samples = new List<double>(60);
        var clock = new Stopwatch();
        for (int step = 0; step < 60; step++)
        {
            // Sweep the photo down through the column and back up again.
            float y = 120f + (step < 30 ? step * 16f : (60 - step) * 16f);
            clock.Restart();
            _frames.DragTo(400f, y, snap: true);
            Paint();
            clock.Stop();
            samples.Add(clock.Elapsed.TotalMilliseconds);
        }

        _frames.EndDrag(commit: false);

        samples.Sort();
        double median = samples[samples.Count / 2];
        Assert.True(
            median < 16d,
            $"median live-reflow step was {median:F2}ms (60fps budget is 16ms); "
            + $"min {samples[0]:F2}ms, max {samples[^1]:F2}ms");
    }

    public void Dispose() => _source.Dispose();
}
