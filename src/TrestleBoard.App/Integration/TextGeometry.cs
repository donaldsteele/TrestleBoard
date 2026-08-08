using System;
using System.Collections.Generic;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using TrestleBoard.Layout.Editing;
using TrestleBoard.Rendering;

namespace TrestleBoard.App.Integration;

/// <summary>
/// Where a stretch of writing sits on a page, in page points (M52, M58).
///
/// <para>Two features now need the same answer — the spelling underline and the read-aloud
/// highlight — and they must agree, because a squiggle under one word and a band behind a different
/// one would be the app pointing two ways at once. Extracted when the second caller arrived rather
/// than copied, which is the same call M52 made about <c>TextReplacement</c>.</para>
///
/// <para>Both callers are chrome. Nothing here draws; it answers a question about geometry, and the
/// canvas decides what to do with the answer.</para>
/// </summary>
internal static class TextGeometry
{
    /// <summary>
    /// The rectangles covering characters <paramref name="offset"/> to
    /// <paramref name="offset"/> + <paramref name="length"/> of a paragraph, restricted to one
    /// page. A story flowing through frames on two pages answers for both, and only this page's
    /// rectangles belong on this page.
    /// </summary>
    internal static IReadOnlyList<RectPt> RectsFor(
        Document document,
        DocumentRenderSource source,
        int pageIndex,
        string storyId,
        int paragraphIndex,
        int offset,
        int length)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);

        if (length <= 0
            || pageIndex < 0
            || pageIndex >= document.Pages.Count
            || !source.TryGetStoryGeometry(storyId, out StoryTextGeometry? geometry))
        {
            return [];
        }

        var range = new TextRange(
            new TextPosition(storyId, paragraphIndex, offset),
            new TextPosition(storyId, paragraphIndex, offset + length));

        string pageId = document.Pages[pageIndex].Id;
        var rects = new List<RectPt>();
        foreach (SelectionRect rect in geometry.GetSelectionRects(range))
        {
            if (rect.PageId is { } on && !string.Equals(on, pageId, StringComparison.Ordinal))
            {
                continue;
            }

            rects.Add(new RectPt(
                rect.LeftPt,
                rect.TopPt,
                Math.Max(0f, rect.RightPt - rect.LeftPt),
                Math.Max(0f, rect.BottomPt - rect.TopPt)));
        }

        return rects;
    }
}
