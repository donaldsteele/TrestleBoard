using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Input;

namespace TrestleBoard.Layout.Documents;

/// <summary>Ties a layout frame back to its source block for rendering/hit-testing.</summary>
public sealed record FramePlacement(string PageId, string BlockId);

/// <summary>One story chain ready for the engine: the request plus where each frame lives.</summary>
public sealed record StoryLayoutPlan(string StoryId, LayoutRequest Request, IReadOnlyList<FramePlacement> Placements);

/// <summary>
/// Bridges the Core document model to layout input (PLAN.md §2/§3): follows TextBlock
/// linkNext chains into frame lists, turns higher-z wrap blocks into exclusions, and
/// resolves named styles into concrete run formats.
/// </summary>
public static class DocumentLayoutAdapter
{
    /// <summary>
    /// Builds one plan per story chain head, in page-then-z document order.
    /// <paramref name="rectOverrides"/> substitutes frame/exclusion rects without touching the
    /// document — the live-drag preview path (docs/M5-spec.md §4.1).
    /// </summary>
    public static IReadOnlyList<StoryLayoutPlan> BuildPlans(
        Document document,
        IReadOnlyDictionary<string, RectPt>? rectOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var linkedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Page page in document.Pages)
        {
            foreach (Block block in page.Blocks)
            {
                if (block is TextBlock { LinkNext: { } next })
                {
                    linkedIds.Add(next);
                }
            }
        }

        var plans = new List<StoryLayoutPlan>();
        foreach (Page page in document.Pages)
        {
            foreach (Block block in page.Blocks)
            {
                if (block is TextBlock head && !linkedIds.Contains(head.Id))
                {
                    plans.Add(BuildPlan(document, page, head, rectOverrides));
                }
            }
        }

        return plans;
    }

    /// <summary>
    /// Lays a story out against its real chain PLUS frames that do not exist yet — how auto-flow
    /// asks "would one more frame be enough?" without touching the document (docs/M8-spec.md §2).
    /// A planned frame on a page that does not exist yet simply has no exclusions.
    /// </summary>
    /// <param name="rectOverrides">
    /// The same substitutions the REAL layout is using, and passing them is the whole point.
    ///
    /// <para>This used to pass null while the live layout passed the render source's
    /// <c>_layoutRects</c> — which hold the caption-extended rect of every captioned photo. So the
    /// speculative layout wrapped text around a smaller obstacle than the real one, fitted more
    /// text than reality would, and could answer "yes, one more frame is enough" for an article
    /// that is still overset after the frame is committed (review §14.2). The answer has to be
    /// computed against the same page the user is looking at.</para>
    /// </param>
    public static LayoutRequest BuildSpeculativeRequest(
        Document document,
        TextBlock head,
        IReadOnlyList<(string PageId, TextBlock Frame)> plannedFrames,
        IReadOnlyDictionary<string, RectPt>? rectOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(plannedFrames);

        StoryLayoutPlan real = BuildPlan(document, document.FindBlock(head.Id).Page, head, rectOverrides);
        var frames = new List<LayoutFrame>(real.Request.Frames);
        foreach ((string pageId, TextBlock frame) in plannedFrames)
        {
            Page? page = document.Pages.Find(p => p.Id == pageId);
            frames.Add(new LayoutFrame(
                ToFrameRect(EffectiveRect(frame, rectOverrides)),
                page is null ? [] : BuildExclusions(page, frame, rectOverrides),
                frame.ColumnCount));
        }

        return new LayoutRequest(real.Request.Story, frames);
    }

    private static StoryLayoutPlan BuildPlan(
        Document document,
        Page headPage,
        TextBlock head,
        IReadOnlyDictionary<string, RectPt>? rectOverrides)
    {
        var frames = new List<LayoutFrame>();
        var placements = new List<FramePlacement>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        Page page = headPage;
        TextBlock? current = head;
        while (current is not null)
        {
            if (!visited.Add(current.Id))
            {
                throw new InvalidOperationException($"TextBlock chain contains a cycle at {current.Id}.");
            }

            frames.Add(new LayoutFrame(
                ToFrameRect(EffectiveRect(current, rectOverrides)),
                BuildExclusions(page, current, rectOverrides),
                current.ColumnCount));
            placements.Add(new FramePlacement(page.Id, current.Id));

            if (current.LinkNext is not { } nextId)
            {
                break;
            }

            // A link whose target is gone TERMINATES the chain (docs/M8-spec.md §3.1). Deleting the
            // page a continuation frame sat on must cost the user that continuation — reported as
            // overset — not the whole document to an exception thrown mid-paint.
            if (!document.TryFindBlock(nextId, out Page? nextPage, out Block? nextBlock))
            {
                break;
            }

            if (nextBlock is not TextBlock nextText)
            {
                throw new InvalidOperationException($"linkNext target {nextId} is not a text block.");
            }

            page = nextPage;
            current = nextText;
        }

        LayoutStory story = BuildStory(document, document.GetStory(head.StoryRef));
        return new StoryLayoutPlan(head.StoryRef, new LayoutRequest(story, frames), placements);
    }

    /// <summary>Blocks with wrapMode=Rectangle and higher z than the text frame push text aside (PLAN.md §2).</summary>
    private static List<ExclusionRect> BuildExclusions(
        Page page,
        TextBlock textBlock,
        IReadOnlyDictionary<string, RectPt>? rectOverrides)
    {
        var exclusions = new List<ExclusionRect>();
        foreach (Block block in page.Blocks)
        {
            if (!ReferenceEquals(block, textBlock)
                && block.WrapMode == WrapMode.Rectangle
                && block.ZOrder > textBlock.ZOrder)
            {
                exclusions.Add(new ExclusionRect(
                    ToFrameRect(EffectiveRect(block, rectOverrides)),
                    block.WrapMarginPt,
                    block.ZOrder));
            }
        }

        return exclusions;
    }

    /// <summary>The block's rect, or the drag preview standing in for it.</summary>
    public static RectPt EffectiveRect(Block block, IReadOnlyDictionary<string, RectPt>? rectOverrides)
    {
        ArgumentNullException.ThrowIfNull(block);
        return rectOverrides is not null && rectOverrides.TryGetValue(block.Id, out RectPt preview)
            ? preview
            : block.FrameRect;
    }

    private static LayoutStory BuildStory(Document document, Story story)
    {
        var paragraphs = new List<LayoutParagraph>(story.Paragraphs.Count);
        foreach (StoryParagraph para in story.Paragraphs)
        {
            ParagraphStyleDef paraStyle = document.StyleSheet.GetParagraphStyle(para.ParagraphStyleRef);
            CharacterStyle defaultRun = MapCharacter(document.StyleSheet.GetCharacterStyle(paraStyle.CharacterStyleRef));
            var runs = new List<LayoutRun>(para.Runs.Count);
            foreach (StoryRun run in para.Runs)
            {
                CharacterStyle style = run.CharacterStyleRef is { } styleRef
                    ? MapCharacter(document.StyleSheet.GetCharacterStyle(styleRef))
                    : defaultRun;
                runs.Add(new LayoutRun(run.Text, style));
            }

            paragraphs.Add(new LayoutParagraph(
                new ParagraphStyle(
                    paraStyle.LineSpacing,
                    paraStyle.SpaceBeforePt,
                    paraStyle.SpaceAfterPt,
                    paraStyle.FirstLineIndentPt,
                    MapAlign(paraStyle.Align),
                    defaultRun),
                runs));
        }

        return new LayoutStory(story.Id, paragraphs);
    }

    /// <summary>Internal from M18 so <see cref="CaptionLayout"/> sets a caption in the same way.</summary>
    internal static CharacterStyle MapCharacter(CharacterStyleDef def) => new(
        def.FontFamily,
        def.Weight == FontWeightToken.Bold ? FontWeight.Bold : FontWeight.Regular,
        def.Slant == FontSlantToken.Italic ? FontStyleSlant.Italic : FontStyleSlant.Normal,
        def.SizePt,
        def.ColorArgb);

    internal static TextAlign MapAlign(TextAlignment align) => align switch
    {
        TextAlignment.Center => TextAlign.Center,
        TextAlignment.Right => TextAlign.Right,
        _ => TextAlign.Left,
    };

    private static FrameRect ToFrameRect(RectPt rect) =>
        new(rect.X, rect.Y, rect.Right, rect.Bottom);
}
