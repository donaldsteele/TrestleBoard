using System;
using System.Collections.Generic;
using System.Linq;
using TrestleBoard.App.Settings;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using TrestleBoard.Layout.Editing;
using TrestleBoard.Rendering;
using TrestleBoard.Spelling;

namespace TrestleBoard.App.Integration;

/// <summary>
/// What the shell needs from the spell checker (PLAN.md §11 M52): the words on a page and where
/// they sit, and the whole newsletter's worth for the wizard.
///
/// <para>The checker itself knows nothing about pages, zoom or Avalonia; the geometry service knows
/// nothing about spelling. This is the one place the two meet, and it is in App on purpose — a
/// spelling underline is chrome, and chrome is App's business.</para>
/// </summary>
internal sealed class SpellingService
{
    private readonly PersonalDictionary _personal;

    internal SpellingService(string? personalDictionaryPath = null)
    {
        _personal = new PersonalDictionary(personalDictionaryPath ?? AppPaths.PersonalDictionaryFile);
        Checker = new SpellChecker(_personal);
    }

    internal SpellChecker Checker { get; }

    internal PersonalDictionary Personal => _personal;

    /// <summary>
    /// The craft's vocabulary and the address book's names, once, so the first newsletter is not a
    /// wall of underlines. Cheap and idempotent — it does nothing once the file exists.
    /// </summary>
    internal int SeedIfEmpty(IEnumerable<string>? namesFromTheAddressBook) =>
        _personal.SeedIfEmpty(namesFromTheAddressBook);

    /// <summary>Teaches it the address book's surnames when the roster changes.</summary>
    internal int Learn(IEnumerable<string> names) => _personal.Learn(names);

    /// <summary>Every word in the newsletter the checker does not know, in reading order.</summary>
    internal IReadOnlyList<Misspelling> ScanDocument(Document document) =>
        SpellCheckScan.Run(document, Checker);

    /// <summary>
    /// Where to draw the underlines on one page, in page points.
    ///
    /// <para>One page rather than the whole document, because this is recomputed whenever the page
    /// changes underneath it and a six-page scan for six pages' worth of marks would be five pages
    /// of wasted work every time.</para>
    /// </summary>
    internal IReadOnlyList<RectPt> MarksOnPage(Document document, DocumentRenderSource source, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);

        if (pageIndex < 0 || pageIndex >= document.Pages.Count)
        {
            return [];
        }

        var marks = new List<RectPt>();
        foreach (string storyId in StoriesOn(document, pageIndex))
        {
            if (!document.TryGetStory(storyId, out Story? story)
                || !source.TryGetStoryGeometry(storyId, out StoryTextGeometry? geometry))
            {
                continue;
            }

            for (int p = 0; p < story.Paragraphs.Count; p++)
            {
                string text = StoryNavigator.GetParagraphText(story.Paragraphs[p]);
                foreach ((int start, string word) in SpellCheckScan.WordsIn(text))
                {
                    if (Checker.IsSpelledRight(word))
                    {
                        continue;
                    }

                    AddRects(marks, geometry, storyId, p, start, word.Length, pageIndex, document);
                }
            }
        }

        return marks;
    }

    private static void AddRects(
        List<RectPt> marks,
        StoryTextGeometry geometry,
        string storyId,
        int paragraphIndex,
        int offset,
        int length,
        int pageIndex,
        Document document)
    {
        var range = new TextRange(
            new TextPosition(storyId, paragraphIndex, offset),
            new TextPosition(storyId, paragraphIndex, offset + length));

        foreach (SelectionRect rect in geometry.GetSelectionRects(range))
        {
            // A story can flow through frames on more than one page, and the geometry answers for
            // all of them. Only this page's marks belong on this page.
            if (rect.PageId is { } pageId
                && !string.Equals(pageId, document.Pages[pageIndex].Id, StringComparison.Ordinal))
            {
                continue;
            }

            marks.Add(new RectPt(
                rect.LeftPt,
                rect.TopPt,
                Math.Max(0f, rect.RightPt - rect.LeftPt),
                Math.Max(0f, rect.BottomPt - rect.TopPt)));
        }
    }

    private static IEnumerable<string> StoriesOn(Document document, int pageIndex) =>
        document.Pages[pageIndex].Blocks
            .OfType<TextBlock>()
            .Select(b => b.StoryRef)
            .Distinct(StringComparer.Ordinal);
}
