using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Editing;
using TrestleBoard.Rendering;

namespace TrestleBoard.Editing;

/// <summary>
/// The editing brain (docs/M4-spec.md §7): owns caret/selection/x-goal/pending-style state,
/// translates gestures into IDocumentCommands, and never mutates the document directly.
/// UI-agnostic and headless-testable; UI-thread affinity, no locking, no timers.
/// </summary>
public sealed class TextEditorController
{
    private readonly DocumentSession _session;
    private readonly DocumentRenderSource _layout;
    private readonly ITextClipboard _clipboard;
    private TextSelection _selection;
    private string? _blockId;
    private CaretXGoal? _xGoal;
    private string? _pendingCharacterStyleRef;

    public TextEditorController(DocumentSession session, DocumentRenderSource layout, ITextClipboard clipboard)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _session.Changed += (_, _) => OnDocumentChanged();
    }

    public bool IsActive { get; private set; }

    public string? StoryId => IsActive ? _selection.StoryId : null;

    public string? BlockId => IsActive ? _blockId : null;

    public TextSelection Selection => _selection;

    /// <summary>Pushed by the canvas so PageUp/PageDown know a screenful (docs/M4-spec.md §4.3).</summary>
    public float ViewportHeightPt { get; set; } = 700f;

    public event EventHandler? Changed;

    public event EventHandler<CaretRevealEventArgs>? RevealRequested;

    // ---- Session lifecycle ------------------------------------------------------------------

    public bool TryBeginAt(int pageIndex, float xPt, float yPt)
    {
        if (!_layout.TryHitTestText(pageIndex, xPt, yPt, out TextHit hit, out string blockId, slopPt: 0f))
        {
            End();
            return false;
        }

        IsActive = true;
        _blockId = blockId;
        _selection = TextSelection.At(hit.Caret);
        _xGoal = null;
        _pendingCharacterStyleRef = null;
        RaiseChanged();
        return true;
    }

    public void End()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        _blockId = null;
        _xGoal = null;
        _pendingCharacterStyleRef = null;
        RaiseChanged();
    }

    // ---- Selection --------------------------------------------------------------------------

    public void ExtendTo(int pageIndex, float xPt, float yPt)
    {
        if (!IsActive
            || !_layout.TryHitTestText(pageIndex, xPt, yPt, out TextHit hit, out _, slopPt: 18f)
            || hit.StoryId != _selection.StoryId)
        {
            return;
        }

        _selection = _selection with { Extent = hit.Caret };
        _xGoal = null;
        _pendingCharacterStyleRef = null;
        RaiseChanged();
    }

    public void SelectWordAt(int pageIndex, float xPt, float yPt)
    {
        if (!TryBeginAt(pageIndex, xPt, yPt))
        {
            return;
        }

        CaretPosition caret = _selection.Caret;
        string text = ParagraphText(caret.ParagraphIndex);
        (int start, int end) = StoryNavigator.WordAt(text, caret.Offset);
        _selection = new TextSelection(
            CaretPosition.Leading(new TextPosition(caret.StoryId, caret.ParagraphIndex, start)),
            CaretPosition.Trailing(new TextPosition(caret.StoryId, caret.ParagraphIndex, end)));
        RaiseChanged();
    }

    public void SelectParagraphAt(int pageIndex, float xPt, float yPt)
    {
        if (!TryBeginAt(pageIndex, xPt, yPt))
        {
            return;
        }

        CaretPosition caret = _selection.Caret;
        string text = ParagraphText(caret.ParagraphIndex);
        _selection = new TextSelection(
            CaretPosition.Leading(new TextPosition(caret.StoryId, caret.ParagraphIndex, 0)),
            CaretPosition.Trailing(new TextPosition(caret.StoryId, caret.ParagraphIndex, text.Length)));
        RaiseChanged();
    }

    public void SelectAll()
    {
        if (!IsActive)
        {
            return;
        }

        Story story = CurrentStory();
        int last = story.Paragraphs.Count - 1;
        _selection = new TextSelection(
            CaretPosition.Leading(new TextPosition(story.Id, 0, 0)),
            CaretPosition.Trailing(new TextPosition(story.Id, last, story.Paragraphs[last].Length)));
        RaiseChanged();
    }

    /// <summary>
    /// Highlights a run of characters anywhere in the newsletter and asks the shell to scroll it
    /// into view — how Find shows what it found (PLAN.md §11 M21).
    ///
    /// <para>This is the one way into a text session that does not start from a point on a page, and
    /// it is what makes finding across a linked chain work: a story flows through several frames, so
    /// the hit is a story position first and a frame second. The frame it landed in is read back
    /// from the geometry afterwards, which is why the second frame of a chain needs no special
    /// case.</para>
    ///
    /// <para>Returns false when the position is not in this document — a hit found before an undo
    /// and selected after it, for instance — rather than throwing at the caller.</para>
    /// </summary>
    public bool SelectRange(string storyId, int paragraphIndex, int offset, int length)
    {
        if (_session.Document.Stories.Find(s => s.Id == storyId) is not { } story
            || paragraphIndex < 0
            || paragraphIndex >= story.Paragraphs.Count)
        {
            return false;
        }

        int paragraphLength = story.Paragraphs[paragraphIndex].Length;
        if (offset < 0 || length < 0 || offset + length > paragraphLength)
        {
            return false;
        }

        IsActive = true;
        _selection = new TextSelection(
            CaretPosition.Leading(new TextPosition(storyId, paragraphIndex, offset)),
            CaretPosition.Trailing(new TextPosition(storyId, paragraphIndex, offset + length)));
        _xGoal = null;
        _pendingCharacterStyleRef = null;
        _blockId = BlockShowing(_selection.Caret) ?? FirstBlockOfStory(storyId);
        RaiseChanged();
        RequestReveal();
        return true;
    }

    /// <summary>Which frame a caret position is actually laid out in, or null when it is overset.</summary>
    private string? BlockShowing(CaretPosition caret) =>
        _layout.TryGetStoryGeometry(caret.StoryId, out StoryTextGeometry? geometry)
        && geometry.TryGetCaretGeometry(caret, out CaretGeometry g)
            ? g.BlockId
            : null;

    /// <summary>The first frame showing a story — the fallback when the hit itself is overset.</summary>
    private string? FirstBlockOfStory(string storyId) => _session.Document.Pages
        .SelectMany(page => page.Blocks)
        .OfType<Core.Model.TextBlock>()
        .FirstOrDefault(b => string.Equals(b.StoryRef, storyId, StringComparison.Ordinal))
        ?.Id;

    // ---- Navigation (docs/M4-spec.md §4.3) --------------------------------------------------

    public bool Move(CaretMotion motion, bool extend)
    {
        if (!IsActive)
        {
            return false;
        }

        // Non-extending motion from a non-empty selection: Left/Up collapse to Range.Start
        // without further motion, Right/Down to Range.End; others collapse then move.
        if (!extend && !_selection.IsEmpty)
        {
            TextRange range = _selection.Range;
            switch (motion)
            {
                case CaretMotion.Left:
                case CaretMotion.Up:
                    SetCaret(CaretPosition.Trailing(range.Start), clearGoal: motion == CaretMotion.Left);
                    return true;
                case CaretMotion.Right:
                case CaretMotion.Down:
                    SetCaret(CaretPosition.Leading(range.End), clearGoal: motion == CaretMotion.Right);
                    return true;
                default:
                    _selection = TextSelection.At(_selection.Caret);
                    break;
            }
        }

        CaretPosition from = _selection.Caret;
        if (!TryComputeMotion(from, motion, out CaretPosition to))
        {
            return false;
        }

        if (extend)
        {
            _selection = _selection with { Extent = to };
        }
        else
        {
            _selection = TextSelection.At(to);
        }

        if (motion is not (CaretMotion.Up or CaretMotion.Down or CaretMotion.PageUp or CaretMotion.PageDown))
        {
            _xGoal = null;
        }

        _pendingCharacterStyleRef = null;
        RaiseChanged();
        RequestReveal();
        return true;
    }

    private bool TryComputeMotion(CaretPosition from, CaretMotion motion, out CaretPosition to)
    {
        to = from;
        Story story = CurrentStory();
        string text = ParagraphText(from.ParagraphIndex);
        switch (motion)
        {
            case CaretMotion.Left:
                if (from.Offset > 0)
                {
                    to = CaretPosition.Trailing(new TextPosition(
                        from.StoryId, from.ParagraphIndex, StoryNavigator.PreviousGrapheme(text, from.Offset)));
                }
                else if (from.ParagraphIndex > 0)
                {
                    int p = from.ParagraphIndex - 1;
                    to = CaretPosition.Trailing(new TextPosition(from.StoryId, p, ParagraphText(p).Length));
                }
                else
                {
                    return false;
                }

                return true;
            case CaretMotion.Right:
                if (from.Offset < text.Length)
                {
                    to = CaretPosition.Leading(new TextPosition(
                        from.StoryId, from.ParagraphIndex, StoryNavigator.NextGrapheme(text, from.Offset)));
                }
                else if (from.ParagraphIndex < story.Paragraphs.Count - 1)
                {
                    to = CaretPosition.Leading(new TextPosition(from.StoryId, from.ParagraphIndex + 1, 0));
                }
                else
                {
                    return false;
                }

                return true;
            case CaretMotion.WordLeft:
                if (from.Offset > 0)
                {
                    to = CaretPosition.Trailing(new TextPosition(
                        from.StoryId, from.ParagraphIndex, StoryNavigator.PreviousWordStart(text, from.Offset)));
                }
                else if (from.ParagraphIndex > 0)
                {
                    int p = from.ParagraphIndex - 1;
                    to = CaretPosition.Trailing(new TextPosition(from.StoryId, p, ParagraphText(p).Length));
                }
                else
                {
                    return false;
                }

                return true;
            case CaretMotion.WordRight:
                if (from.Offset < text.Length)
                {
                    to = CaretPosition.Leading(new TextPosition(
                        from.StoryId, from.ParagraphIndex, StoryNavigator.NextWordStart(text, from.Offset)));
                }
                else if (from.ParagraphIndex < story.Paragraphs.Count - 1)
                {
                    to = CaretPosition.Leading(new TextPosition(from.StoryId, from.ParagraphIndex + 1, 0));
                }
                else
                {
                    return false;
                }

                return true;
            case CaretMotion.StoryStart:
                to = CaretPosition.Leading(new TextPosition(from.StoryId, 0, 0));
                return true;
            case CaretMotion.StoryEnd:
                int lastParagraph = story.Paragraphs.Count - 1;
                to = CaretPosition.Trailing(new TextPosition(from.StoryId, lastParagraph, ParagraphText(lastParagraph).Length));
                return true;
            case CaretMotion.LineStart:
            case CaretMotion.LineEnd:
                if (!TryGetGeometryService(out StoryTextGeometry? bounds)
                    || !bounds.TryGetLineBounds(from, out CaretPosition lineStart, out CaretPosition lineEnd))
                {
                    return false;
                }

                to = motion == CaretMotion.LineStart ? lineStart : lineEnd;
                return true;
            case CaretMotion.Up:
            case CaretMotion.Down:
            case CaretMotion.PageUp:
            case CaretMotion.PageDown:
                if (!TryGetGeometryService(out StoryTextGeometry? geometry))
                {
                    return false;
                }

                int delta = motion switch
                {
                    CaretMotion.Up => -1,
                    CaretMotion.Down => +1,
                    CaretMotion.PageUp => -PageStride(from, geometry),
                    _ => PageStride(from, geometry),
                };
                if (!geometry.TryMoveVertical(from, delta, _xGoal, out to, out CaretXGoal newGoal))
                {
                    return false;
                }

                _xGoal = newGoal;
                return true;
            default:
                return false;
        }
    }

    private int PageStride(CaretPosition from, StoryTextGeometry geometry)
    {
        float lineHeight = geometry.TryGetCaretGeometry(from, out CaretGeometry g) && g.HeightPt > 0 ? g.HeightPt : 14f;
        return Math.Max(1, (int)(ViewportHeightPt / lineHeight) - 1);
    }

    // ---- Editing (docs/M4-spec.md §5) -------------------------------------------------------

    public void InsertText(string text)
    {
        if (!IsActive)
        {
            return;
        }

        string sanitized = Sanitize(text);
        if (sanitized.Length == 0)
        {
            return;
        }

        if (sanitized.Contains('\n', StringComparison.Ordinal))
        {
            PasteText(sanitized);
            return;
        }

        TextRange range = _selection.Range;
        TextPosition at = range.Start;
        string? pending = _pendingCharacterStyleRef;
        _pendingCharacterStyleRef = null;

        if (!range.IsEmpty || pending is not null)
        {
            var children = new List<IDocumentCommand>();
            if (!range.IsEmpty)
            {
                children.AddRange(TextEditBuilder.BuildDeleteRange(CurrentStory(), range));
            }

            children.Add(new InsertTextCommand(at.StoryId, at.ParagraphIndex, at.Offset, sanitized));
            if (pending is not null)
            {
                children.Add(new ApplyCharacterStyleCommand(
                    at.StoryId, at.ParagraphIndex, at.Offset, sanitized.Length, pending));
            }

            _session.Execute(new CompositeCommand(
                "Replace text",
                new ChangeScope(ChangeKind.StoryStructure, StoryId: at.StoryId),
                children));
        }
        else
        {
            // Bare insert so DocumentSession coalescing gives word-burst undo.
            _session.Execute(new InsertTextCommand(at.StoryId, at.ParagraphIndex, at.Offset, sanitized));
        }

        SetCaret(CaretPosition.Leading(new TextPosition(at.StoryId, at.ParagraphIndex, at.Offset + sanitized.Length)));
        RequestReveal();
    }

    public void InsertParagraphBreak()
    {
        if (!IsActive)
        {
            return;
        }

        TextRange range = _selection.Range;
        TextPosition at = range.Start;
        if (range.IsEmpty)
        {
            _session.Execute(new SplitParagraphCommand(at.StoryId, at.ParagraphIndex, at.Offset));
        }
        else
        {
            var children = new List<IDocumentCommand>(TextEditBuilder.BuildDeleteRange(CurrentStory(), range))
            {
                new SplitParagraphCommand(at.StoryId, at.ParagraphIndex, at.Offset),
            };
            _session.Execute(new CompositeCommand(
                "New paragraph",
                new ChangeScope(ChangeKind.StoryStructure, StoryId: at.StoryId),
                children));
        }

        SetCaret(CaretPosition.Leading(new TextPosition(at.StoryId, at.ParagraphIndex + 1, 0)));
        RequestReveal();
    }

    public void Backspace()
    {
        if (!IsActive)
        {
            return;
        }

        TextRange range = _selection.Range;
        if (!range.IsEmpty)
        {
            DeleteRange(range, "Delete text");
            return;
        }

        CaretPosition caret = _selection.Caret;
        string text = ParagraphText(caret.ParagraphIndex);
        if (caret.Offset > 0)
        {
            int previous = StoryNavigator.PreviousGrapheme(text, caret.Offset);
            _session.Execute(new DeleteTextCommand(caret.StoryId, caret.ParagraphIndex, previous, caret.Offset - previous));
            SetCaret(CaretPosition.Trailing(new TextPosition(caret.StoryId, caret.ParagraphIndex, previous)));
        }
        else if (caret.ParagraphIndex > 0)
        {
            int p = caret.ParagraphIndex - 1;
            int previousLength = ParagraphText(p).Length;
            _session.Execute(new MergeParagraphCommand(caret.StoryId, p));
            SetCaret(CaretPosition.Trailing(new TextPosition(caret.StoryId, p, previousLength)));
        }

        RequestReveal();
    }

    public void DeleteForward()
    {
        if (!IsActive)
        {
            return;
        }

        TextRange range = _selection.Range;
        if (!range.IsEmpty)
        {
            DeleteRange(range, "Delete text");
            return;
        }

        CaretPosition caret = _selection.Caret;
        string text = ParagraphText(caret.ParagraphIndex);
        if (caret.Offset < text.Length)
        {
            int next = StoryNavigator.NextGrapheme(text, caret.Offset);
            _session.Execute(new DeleteTextCommand(caret.StoryId, caret.ParagraphIndex, caret.Offset, next - caret.Offset));
            SetCaret(new CaretPosition(caret.Position, TextAffinity.Leading));
        }
        else if (caret.ParagraphIndex < CurrentStory().Paragraphs.Count - 1)
        {
            _session.Execute(new MergeParagraphCommand(caret.StoryId, caret.ParagraphIndex));
            SetCaret(new CaretPosition(caret.Position, TextAffinity.Trailing));
        }
    }

    public async Task CopyAsync()
    {
        if (!IsActive || _selection.IsEmpty)
        {
            return;
        }

        await _clipboard.SetTextAsync(StoryNavigator.GetRangeText(CurrentStory(), _selection.Range)).ConfigureAwait(true);
    }

    public async Task CutAsync()
    {
        if (!IsActive || _selection.IsEmpty)
        {
            return;
        }

        TextRange range = _selection.Range;
        await _clipboard.SetTextAsync(StoryNavigator.GetRangeText(CurrentStory(), range)).ConfigureAwait(true);
        DeleteRange(range, "Cut text");
    }

    /// <summary>
    /// Puts the copied words in at the caret. False when there was nothing to put in — the catalog
    /// promises this command "says so out loud when there is nothing to paste" (M70(c)), and the
    /// clipboard is the shell's to read, so the shell is told and does the saying.
    /// </summary>
    public async Task<bool> PasteAsync()
    {
        if (!IsActive)
        {
            return false;
        }

        string? text = await _clipboard.GetTextAsync().ConfigureAwait(true);
        string sanitized = Sanitize(text ?? "");
        if (sanitized.Length == 0)
        {
            return false;
        }

        if (sanitized.Contains('\n', StringComparison.Ordinal))
        {
            PasteText(sanitized);
        }
        else
        {
            InsertText(sanitized);
        }

        return true;
    }

    /// <summary>
    /// Puts a whole block of ready-made words in at the caret, as ONE undo step of its own
    /// (PLAN.md §11 M54).
    ///
    /// <para>Not <see cref="InsertText"/>, and the difference matters. A bare
    /// <c>InsertTextCommand</c> coalesces with the typing either side of it — which is right for
    /// typing, where a burst of keystrokes is one thing the user did, and wrong for this: somebody
    /// who inserts a memorial notice, types a sentence after it and presses Ctrl+Z means "take back
    /// the sentence", not "take back the memorial as well".</para>
    ///
    /// <para>The composite is also what lets the undo be named after the thing the user chose,
    /// which is what they will be looking for when they change their mind.</para>
    /// </summary>
    public void InsertBlock(string text, string undoLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);
        if (!IsActive)
        {
            return;
        }

        string sanitized = Sanitize(text);
        if (sanitized.Length == 0)
        {
            return;
        }

        TextRange range = _selection.Range;
        TextPosition at = range.Start;
        string[] chunks = sanitized.Split('\n');
        var children = new List<IDocumentCommand>();
        if (!range.IsEmpty)
        {
            children.AddRange(TextEditBuilder.BuildDeleteRange(CurrentStory(), range));
        }

        if (chunks.Length == 1)
        {
            children.Add(new InsertTextCommand(at.StoryId, at.ParagraphIndex, at.Offset, chunks[0]));
        }
        else
        {
            children.AddRange(TextEditBuilder.BuildMultiParagraphInsert(CurrentStory(), at, chunks));
        }

        _session.Execute(new CompositeCommand(
            undoLabel,
            new ChangeScope(ChangeKind.StoryStructure, StoryId: at.StoryId),
            children));
        SetCaret(CaretPosition.Leading(new TextPosition(
            at.StoryId, at.ParagraphIndex + chunks.Length - 1, chunks[^1].Length)));
        RequestReveal();
    }

    /// <summary>
    /// What kind of list the paragraph the caret is in belongs to, or null (M61).
    /// </summary>
    public string? CurrentListKind =>
        IsActive && CurrentStory() is { } story
        && _selection.Range.Start.ParagraphIndex < story.Paragraphs.Count
            ? story.Paragraphs[_selection.Range.Start.ParagraphIndex].ListKind
            : null;

    /// <summary>
    /// Makes the paragraphs the selection touches a list of the given kind — or, when they are
    /// already that kind, puts them back to normal writing (PLAN.md §11 M61).
    ///
    /// <para>A toggle that plainly undoes itself, reached the way Bold is reached: no wizard, no
    /// dialog, and one undo step however many paragraphs the selection covers, because making six
    /// points into a list is one thing the user did.</para>
    /// </summary>
    public bool ToggleList(string listKind)
    {
        ArgumentException.ThrowIfNullOrEmpty(listKind);
        if (!IsActive || CurrentStory() is not { } story)
        {
            return false;
        }

        TextRange range = _selection.Range;
        int first = Math.Clamp(range.Start.ParagraphIndex, 0, story.Paragraphs.Count - 1);
        int last = Math.Clamp(range.End.ParagraphIndex, first, story.Paragraphs.Count - 1);

        // Off when every paragraph the selection touches is already this kind. Anything else turns
        // them all on, which is what somebody who highlighted a mixed run means by pressing it.
        bool allAlready = true;
        for (int p = first; p <= last; p++)
        {
            if (!string.Equals(story.Paragraphs[p].ListKind, listKind, StringComparison.Ordinal))
            {
                allAlready = false;
                break;
            }
        }

        string? wanted = allAlready ? null : listKind;
        var children = new List<IDocumentCommand>();
        for (int p = first; p <= last; p++)
        {
            if (!string.Equals(story.Paragraphs[p].ListKind, wanted, StringComparison.Ordinal))
            {
                children.Add(new SetListKindCommand(story.Id, p, wanted));
            }
        }

        if (children.Count == 0)
        {
            return false;
        }

        _session.Execute(children.Count == 1
            ? children[0]
            : new CompositeCommand(
                children[0].Description,
                new ChangeScope(ChangeKind.Text, StoryId: story.Id),
                children));
        RaiseChanged();
        RequestReveal();
        return true;
    }

    private void PasteText(string sanitized)
    {
        TextRange range = _selection.Range;
        TextPosition at = range.Start;
        string[] chunks = sanitized.Split('\n');
        var children = new List<IDocumentCommand>();
        if (!range.IsEmpty)
        {
            children.AddRange(TextEditBuilder.BuildDeleteRange(CurrentStory(), range));
        }

        children.AddRange(TextEditBuilder.BuildMultiParagraphInsert(CurrentStory(), at, chunks));
        _session.Execute(new CompositeCommand(
            "Paste text",
            new ChangeScope(ChangeKind.StoryStructure, StoryId: at.StoryId),
            children));
        SetCaret(CaretPosition.Leading(new TextPosition(
            at.StoryId, at.ParagraphIndex + chunks.Length - 1, chunks[^1].Length)));
        RequestReveal();
    }

    private void DeleteRange(TextRange range, string description)
    {
        IReadOnlyList<IDocumentCommand> children = TextEditBuilder.BuildDeleteRange(CurrentStory(), range);
        if (children.Count == 1 && range.IsSingleParagraph)
        {
            // Bare so backspace-over-selection bursts can coalesce with following deletes.
            _session.Execute(children[0]);
        }
        else
        {
            _session.Execute(new CompositeCommand(
                description,
                new ChangeScope(ChangeKind.StoryStructure, StoryId: range.StoryId),
                children));
        }

        SetCaret(CaretPosition.Leading(range.Start));
        RequestReveal();
    }

    // ---- Formatting (docs/M4-spec.md §6) ----------------------------------------------------

    public bool IsBoldActive => IsFormatActive(def => def.Weight == FontWeightToken.Bold);

    public bool IsItalicActive => IsFormatActive(def => def.Slant == FontSlantToken.Italic);

    public void ToggleBold() => ToggleFormat(
        isActive: IsBoldActive,
        makeTarget: (def, active) => (active ? FontWeightToken.Regular : FontWeightToken.Bold, def.Slant),
        activeDescription: "Remove bold",
        inactiveDescription: "Bold text");

    public void ToggleItalic() => ToggleFormat(
        isActive: IsItalicActive,
        makeTarget: (def, active) => (def.Weight, active ? FontSlantToken.Normal : FontSlantToken.Italic),
        activeDescription: "Remove italic",
        inactiveDescription: "Italic text");

    // ---- Everyday verbs (PLAN.md §11 M98) -----------------------------------------------------

    /// <summary>How a run of words should be re-cased.</summary>
    public enum LetterCase
    {
        /// <summary>THE WHOLE THING IN CAPITALS.</summary>
        Upper,

        /// <summary>all of it in small letters.</summary>
        Lower,

        /// <summary>The First Letter Of Each Word.</summary>
        Title,
    }

    /// <summary>
    /// Changes the highlighted words to capitals, small letters, or one capital per word (M98).
    ///
    /// <para><b>Why it is a command and not a matter of retyping.</b> A heading arrives from a Word
    /// document IN CAPITALS, or a name is typed in lower case at half past ten at night. Retyping
    /// it loses the styling on it, and this audience types slowly.</para>
    ///
    /// <para><b>The styling is kept</b> because the words are replaced run by run, each with its own
    /// character style put back, rather than deleted and retyped as one plain string.</para>
    /// </summary>
    /// <returns>False when nothing is highlighted, or the words are already like that.</returns>
    public bool ChangeCase(LetterCase letterCase)
    {
        if (!IsActive || _selection.IsEmpty)
        {
            return false;
        }

        Story story = CurrentStory();
        TextRange range = _selection.Range;
        string before = StoryNavigator.GetRangeText(story, range);
        string after = Recase(before, letterCase);
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return false;
        }

        // Run by run, so a bold word inside the highlight stays bold. Walked back to front, because
        // every edit before a run would shift the offsets of the ones after it.
        var children = new List<IDocumentCommand>();
        List<(int Paragraph, int Offset, int Length, string EffectiveRef)> spans =
            EnumerateStyleSpans(range);

        for (int i = spans.Count - 1; i >= 0; i--)
        {
            (int paragraph, int offset, int length, _) = spans[i];
            string text = StoryNavigator.GetRangeText(
                story,
                new TextRange(
                    new TextPosition(story.Id, paragraph, offset),
                    new TextPosition(story.Id, paragraph, offset + length)));

            string recased = Recase(text, letterCase);
            if (string.Equals(text, recased, StringComparison.Ordinal))
            {
                continue;
            }

            children.Add(new DeleteTextCommand(story.Id, paragraph, offset, length));
            children.Add(new InsertTextCommand(story.Id, paragraph, offset, recased));
        }

        if (children.Count == 0)
        {
            return false;
        }

        _session.Execute(new CompositeCommand(
            letterCase switch
            {
                LetterCase.Upper => "Make it capitals",
                LetterCase.Lower => "Make it small letters",
                _ => "Capitalise each word",
            },
            new ChangeScope(ChangeKind.Text, StoryId: story.Id),
            children));
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// About how many words are in this piece of writing, and how many are highlighted.
    ///
    /// <para><b>"About" is honest rather than modest.</b> A word count is a count of whitespace
    /// runs, and every program disagrees about hyphens, ampersands and "St." — the app says about
    /// so nobody has to wonder why it differs from the one Word gave them.</para>
    /// </summary>
    /// <returns>Null when there is no caret in a piece of writing.</returns>
    public (int Words, int Characters, int? HighlightedWords)? CountWords()
    {
        if (!IsActive)
        {
            return null;
        }

        Story story = CurrentStory();
        string all = string.Join(
            Environment.NewLine, story.Paragraphs.Select(StoryNavigator.GetParagraphText));

        int? highlighted = _selection.IsEmpty
            ? null
            : WordsIn(StoryNavigator.GetRangeText(story, _selection.Range));

        // Characters counts what is written, not the paragraph breaks between: those are structure,
        // and nobody counting the length of an article means to count them.
        int characters = story.Paragraphs.Sum(p => StoryNavigator.GetParagraphText(p).Length);
        return (WordsIn(all), characters, highlighted);
    }

    /// <summary>Whitespace-separated runs. The one definition, used for every number the app says.</summary>
    public static int WordsIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string Recase(string text, LetterCase letterCase) => letterCase switch
    {
        LetterCase.Upper => text.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
        LetterCase.Lower => text.ToLower(System.Globalization.CultureInfo.CurrentCulture),
        _ => TitleCase(text),
    };

    /// <summary>
    /// One capital per word, and small letters after it.
    ///
    /// <para><b>Not <c>TextInfo.ToTitleCase</c>.</b> That leaves a word already in capitals alone —
    /// "THE STATED COMMUNICATION" comes back unchanged — which is the one input somebody reaches
    /// for this command to fix.</para>
    /// </summary>
    private static string TitleCase(string text)
    {
        var built = new System.Text.StringBuilder(text.Length);
        bool startOfWord = true;
        foreach (char c in text)
        {
            built.Append(startOfWord
                ? char.ToUpper(c, System.Globalization.CultureInfo.CurrentCulture)
                : char.ToLower(c, System.Globalization.CultureInfo.CurrentCulture));

            // An apostrophe does NOT start a new word, or "O'Brien" becomes "O'brien" — but
            // "brother's" must not become "Brother'S" either, so the letter after one is left as
            // the lower-case branch above.
            startOfWord = !char.IsLetterOrDigit(c) && c != '\'' && c != '’';
        }

        return built.ToString();
    }

    // ---- Lining the writing up (PLAN.md §11 M96) ----------------------------------------------

    /// <summary>
    /// Which way the paragraph the caret is in is lined up, or null when not typing.
    ///
    /// <para>Read off the style rather than kept in a field, so the pressed button and the page can
    /// never disagree — the M55 rule.</para>
    /// </summary>
    public TextAlignment? CurrentAlignment
    {
        get
        {
            if (!IsActive || CurrentParagraphStyle() is not { } style)
            {
                return null;
            }

            return style.Align;
        }
    }

    /// <summary>
    /// Lines the chosen paragraphs up left, centred or right (M96).
    ///
    /// <para><b>Alignment has been in the model and honoured by the layout engine since M1</b> —
    /// <c>TextAlignment</c>, <c>ParagraphStyleDef.Align</c> and the shift arithmetic in
    /// <c>TextLayoutEngine</c> are all live and exercised by two sample styles — and no command
    /// could reach it. A committee wanting a centred heading had to mint a style by hand.</para>
    ///
    /// <para>It rides on a DERIVED paragraph style, minted once per role and reused, exactly as
    /// bold and italic ride on derived character styles. Nothing carries direct formatting, so the
    /// resolver, the serialiser and the canonicaliser learn nothing new.</para>
    ///
    /// <para><b>Justified is not offered.</b> §1 rules it out for v1, and rivers in a two-column
    /// frame are what it would give this audience.</para>
    /// </summary>
    /// <returns>False when there is no caret, or the paragraphs are already lined up that way.</returns>
    public bool SetAlignment(TextAlignment alignment)
    {
        if (!IsActive)
        {
            return false;
        }

        Story story = CurrentStory();
        StyleSheet sheet = _session.Document.StyleSheet;
        TextRange range = _selection.Range;

        var children = new List<IDocumentCommand>();
        var ensured = new HashSet<string>(StringComparer.Ordinal);

        for (int p = range.Start.ParagraphIndex; p <= range.End.ParagraphIndex; p++)
        {
            StoryParagraph paragraph = story.Paragraphs[p];
            string role = ParagraphAlignmentNames.RoleOf(paragraph.ParagraphStyleRef);
            string wanted = ParagraphAlignmentNames.NameFor(role, alignment);
            if (string.Equals(wanted, paragraph.ParagraphStyleRef, StringComparison.Ordinal))
            {
                continue;
            }

            if (sheet.ParagraphStyles.TrueForAll(s => s.Name != wanted) && ensured.Add(wanted))
            {
                // Derived from the ROLE, so a centred heading keeps the heading's spacing and its
                // character style — centring must not quietly restyle anything else.
                children.Insert(
                    0,
                    new EnsureParagraphStyleCommand(
                        ParagraphAlignmentNames.Derive(sheet.GetParagraphStyle(role), alignment)));
            }

            children.Add(new ApplyParagraphStyleCommand(story.Id, p, wanted));
        }

        if (children.Count == 0)
        {
            return false;
        }

        _session.Execute(new CompositeCommand(
            alignment switch
            {
                TextAlignment.Center => "Line it up down the middle",
                TextAlignment.Right => "Line it up on the right",
                _ => "Line it up on the left",
            },
            new ChangeScope(ChangeKind.Text, StoryId: story.Id),
            children));
        RaiseChanged();
        return true;
    }

    private ParagraphStyleDef? CurrentParagraphStyle()
    {
        Story story = CurrentStory();
        int index = _selection.Caret.ParagraphIndex;
        if (index < 0 || index >= story.Paragraphs.Count)
        {
            return null;
        }

        string name = story.Paragraphs[index].ParagraphStyleRef;
        return _session.Document.StyleSheet.ParagraphStyles.Find(s => s.Name == name);
    }

    public IReadOnlyList<string> AvailableParagraphStyles =>
        _session.Document.StyleSheet.ParagraphStyles.Select(s => s.Name).ToList();

    // ---- Fonts and sizes (PLAN.md M14) ---------------------------------------------------------

    /// <summary>The character style in force where the caret is, or null when not editing.</summary>
    public CharacterStyleDef? CurrentCharacterStyle
    {
        get
        {
            if (!IsActive)
            {
                return null;
            }

            string? reference = CurrentCharacterStyleRef;
            return reference is null ? null : _session.Document.StyleSheet.GetCharacterStyle(reference);
        }
    }

    /// <summary>
    /// The style name in force. For a selection this is the FIRST span's style — the styles window
    /// and the size stepper both act on a group, and a group has to be chosen from somewhere.
    /// </summary>
    public string? CurrentCharacterStyleRef
    {
        get
        {
            if (!IsActive)
            {
                return null;
            }

            if (_selection.IsEmpty)
            {
                return _pendingCharacterStyleRef
                    ?? EffectiveRefAt(_selection.Caret.ParagraphIndex, _selection.Caret.Offset);
            }

            List<(int Paragraph, int Offset, int Length, string EffectiveRef)> spans =
                EnumerateStyleSpans(_selection.Range);
            return spans.Count > 0 ? spans[0].EffectiveRef : null;
        }
    }

    /// <summary>
    /// The highlighted words, or null when nothing is highlighted. Read-only, and read by the
    /// "just here" font picker so its preview shows the words that are actually about to change
    /// (PLAN.md M20) rather than the first words of the newsletter.
    /// </summary>
    public string? SelectedText => IsActive && !_selection.IsEmpty
        ? StoryNavigator.GetRangeText(CurrentStory(), _selection.Range)
        : null;

    /// <summary>True when the text here carries a "just here" font, rather than its role's font.</summary>
    public bool SelectionUsesFontOverride =>
        CurrentCharacterStyleRef is { } reference && StyleOverrides.IsOverride(reference);

    /// <summary>
    /// "This text uses EB Garamond instead of the Body text font." Null when the text is using its
    /// role's own font, which is the ordinary case and needs no sentence.
    /// </summary>
    public string? DescribeFontOverride()
    {
        if (CurrentCharacterStyle is not { } style || !StyleOverrides.IsOverride(style.Name))
        {
            return null;
        }

        string roleName = StyleOverrides.RoleOf(style.Name);
        CharacterStyleDef? role = _session.Document.StyleSheet.CharacterStyles
            .Find(s => s.Name == roleName);
        return role is null ? null : StyleOverrides.Describe(style, role);
    }

    /// <summary>How many pieces of text in the whole newsletter carry a "just here" font.</summary>
    public int CountFontOverrides() => FontOverrideSpans().Count;

    /// <summary>
    /// Where every "just here" font sits, for the View overlay and for "Show me". Whole-document,
    /// because the styles window's footer offers to put them all back.
    /// </summary>
    public IReadOnlyList<SourceSpan> FontOverrideSpans()
    {
        var spans = new List<SourceSpan>();
        foreach (Story story in _session.Document.Stories)
        {
            for (int p = 0; p < story.Paragraphs.Count; p++)
            {
                int offset = 0;
                foreach (StoryRun run in story.Paragraphs[p].Runs)
                {
                    if (run.CharacterStyleRef is { } reference && StyleOverrides.IsOverride(reference))
                    {
                        spans.Add(new SourceSpan(story.Id, p, offset, offset + run.Text.Length));
                    }

                    offset += run.Text.Length;
                }
            }
        }

        return spans;
    }

    /// <summary>
    /// Applies a font (and optionally a size) to THIS text only, by minting a derived style and
    /// applying it by reference. Runs never carry direct formatting in v1 and this does not change
    /// that — it is the same machinery bold and italic already use, so no new command type is
    /// needed, just EnsureCharacterStyle + ApplyCharacterStyle.
    /// </summary>
    /// <returns>
    /// False when nothing changed — the words already used that font, or there was no text session
    /// at all. M73(e): this was literally <c>_ = RetargetSpans(...)</c>, and the shell announced
    /// "Those words now use their own font" over both of those.
    /// </returns>
    public bool UseFontJustHere(string fontFamily, float? sizePt)
    {
        ArgumentException.ThrowIfNullOrEmpty(fontFamily);
        return RetargetSpans(
            "Use a different font here",
            (sheet, effectiveRef) =>
            {
                CharacterStyleDef from = sheet.GetCharacterStyle(effectiveRef);
                string roleName = StyleOverrides.RoleOf(effectiveRef);
                CharacterStyleDef role = sheet.CharacterStyles.Find(s => s.Name == roleName) ?? from;
                float size = sizePt ?? from.SizePt;
                string overrideBase = StyleOverrides.NameFor(roleName, fontFamily, size, role.SizePt);
                string name = CharacterStyleResolver.VariantName(overrideBase, from.Weight, from.Slant);
                CharacterStyleDef? existing = sheet.CharacterStyles.Find(s => s.Name == name);
                if (existing is not null)
                {
                    return (name, null);
                }

                CharacterStyleDef derived =
                    CharacterStyleResolver.Derive(from, name, from.Weight, from.Slant);
                derived.FontFamily = fontFamily;
                derived.SizePt = size;
                return (name, derived);
            });
    }

    /// <summary>
    /// Writes the highlighted words in a colour (PLAN.md §11 M99 — M86's third deliverable).
    ///
    /// <para><b><see cref="CharacterStyleDef.ColorArgb"/> has been plumbed end to end since M1</b> —
    /// through the resolver, the layout adapter, the shaper and both renderers — and nothing could
    /// set it. Every piece of writing in every newsletter has been black because no command existed
    /// to make it anything else, not because anybody chose black.</para>
    ///
    /// <para>It rides on a derived style under the <c>~</c> convention, exactly as the "just here"
    /// font does, so bold and italic keep working inside coloured text and nothing carries direct
    /// formatting.</para>
    ///
    /// <para><b>Black is the way out</b>, not a fourth command: choosing it names the role again,
    /// which is what puts the writing back the way it was.</para>
    /// </summary>
    /// <returns>False when nothing is highlighted, or it is already that colour.</returns>
    public bool UseColourJustHere(uint argb)
    {
        return RetargetSpans(
            "Change the colour of the writing",
            (sheet, effectiveRef) =>
            {
                CharacterStyleDef from = sheet.GetCharacterStyle(effectiveRef);
                string roleName = StyleOverrides.RoleOf(effectiveRef);
                CharacterStyleDef role = sheet.CharacterStyles.Find(s => s.Name == roleName) ?? from;

                // Asking for the role's own colour puts the writing back on the role rather than
                // minting an override that says "the same as the role" — the rule the alignment
                // verbs follow for left, and what keeps the canonical form small.
                string overrideBase = argb == role.ColorArgb
                    ? roleName
                    : StyleOverrides.ColourNameFor(roleName, argb);

                string name = CharacterStyleResolver.VariantName(overrideBase, from.Weight, from.Slant);
                CharacterStyleDef? existing = sheet.CharacterStyles.Find(s => s.Name == name);
                if (existing is not null)
                {
                    return (name, null);
                }

                CharacterStyleDef derived =
                    CharacterStyleResolver.Derive(from, name, from.Weight, from.Slant);
                derived.ColorArgb = argb;
                return (name, derived);
            });
    }

    /// <summary>The colour the writing at the caret is, or null when not typing.</summary>
    public uint? CurrentTextColour => CurrentCharacterStyle?.ColorArgb;

    /// <summary>
    /// Puts overridden text back on its role's own font, leaving bold and italic alone.    /// <summary>
    /// Puts overridden text back on its role's own font, leaving bold and italic alone.
    /// False when nothing was using a font of its own, so nothing was put back.
    /// </summary>
    public bool ClearFontOverride()
    {
        return RetargetSpans(
            "Put the font back",
            (sheet, effectiveRef) =>
            {
                if (!StyleOverrides.IsOverride(effectiveRef))
                {
                    return (effectiveRef, null);
                }

                CharacterStyleDef from = sheet.GetCharacterStyle(effectiveRef);
                string roleName = StyleOverrides.RoleOf(effectiveRef);
                string name = CharacterStyleResolver.VariantName(roleName, from.Weight, from.Slant);
                CharacterStyleDef? existing = sheet.CharacterStyles.Find(s => s.Name == name);
                if (existing is not null)
                {
                    return (name, null);
                }

                CharacterStyleDef role = sheet.CharacterStyles.Find(s => s.Name == roleName) ?? from;
                return (name, CharacterStyleResolver.Derive(role, name, from.Weight, from.Slant));
            });
    }

    /// <summary>
    /// Puts <b>every</b> "just here" font in the whole newsletter back on its role's own font, in
    /// one undo step, and says how many pieces of text that was.
    ///
    /// <para>M73(b2). The styles window offers "Put them all back" whenever
    /// <see cref="CountFontOverrides"/> is above zero, and that count is whole-document and needs no
    /// caret — but the shell used to carry it out with <c>SelectAll(); ClearFontOverride();</c>,
    /// which needs a caret and reaches one story even when it has one. With no caret both calls
    /// returned early and the app still announced that N pieces had been put back and offered
    /// Ctrl+Z, which then undid an unrelated earlier edit. The offer is whole-document, so the
    /// deed has to be too, and the number said out loud has to be the number this returns.</para>
    /// </summary>
    /// <returns>How many pieces of text were put back. Zero means nothing changed at all.</returns>
    public int ClearEveryFontOverride()
    {
        StyleSheet sheet = _session.Document.StyleSheet;
        var children = new List<IDocumentCommand>();
        var ensured = new HashSet<string>(StringComparer.Ordinal);
        int putBack = 0;

        foreach (Story story in _session.Document.Stories)
        {
            for (int p = 0; p < story.Paragraphs.Count; p++)
            {
                StoryParagraph paragraph = story.Paragraphs[p];
                string paragraphDefault =
                    sheet.GetParagraphStyle(paragraph.ParagraphStyleRef).CharacterStyleRef;
                int offset = 0;
                foreach (StoryRun run in paragraph.Runs)
                {
                    int start = offset;
                    offset += run.Text.Length;
                    if (run.CharacterStyleRef is not { } reference
                        || !StyleOverrides.IsOverride(reference))
                    {
                        continue;
                    }

                    CharacterStyleDef from = sheet.GetCharacterStyle(reference);
                    string roleName = StyleOverrides.RoleOf(reference);
                    string name = CharacterStyleResolver.VariantName(roleName, from.Weight, from.Slant);
                    if (sheet.CharacterStyles.Find(s => s.Name == name) is null && ensured.Add(name))
                    {
                        CharacterStyleDef role =
                            sheet.CharacterStyles.Find(s => s.Name == roleName) ?? from;
                        children.Insert(0, new EnsureCharacterStyleCommand(
                            CharacterStyleResolver.Derive(role, name, from.Weight, from.Slant)));
                    }

                    // Bold and italic are carried by the variant name, so they survive; the run
                    // simply stops naming a style of its own when the role's is what it lands on.
                    string? applied = string.Equals(name, paragraphDefault, StringComparison.Ordinal)
                        ? null
                        : name;
                    children.Add(new ApplyCharacterStyleCommand(
                        story.Id, p, start, run.Text.Length, applied));
                    putBack++;
                }
            }
        }

        if (putBack == 0)
        {
            return 0;
        }

        // No StoryId on the scope: this is a broad change on purpose, because it crosses stories.
        _session.Execute(new CompositeCommand(
            "Put every font back",
            new ChangeScope(ChangeKind.Text),
            children));
        RaiseChanged();
        return putBack;
    }

    /// <summary>
    /// Retargets every span in the selection (or the caret's pending style) at a style chosen per
    /// span. Factored out because "use a different font here" and "put it back" differ only in how
    /// they name the target.
    /// </summary>
    /// <returns>
    /// False when nothing was changed at all. M70(d): the shell announced "Put back to the usual
    /// font" before knowing whether anything had been, and three silent early returns could make
    /// that a false report.
    /// </returns>
    private bool RetargetSpans(
        string description,
        Func<StyleSheet, string, (string Name, CharacterStyleDef? ToEnsure)> chooseTarget)
    {
        if (!IsActive)
        {
            return false;
        }

        StyleSheet sheet = _session.Document.StyleSheet;
        if (_selection.IsEmpty)
        {
            string? reference = _pendingCharacterStyleRef
                ?? EffectiveRefAt(_selection.Caret.ParagraphIndex, _selection.Caret.Offset);
            if (reference is null)
            {
                return false;
            }

            (string name, CharacterStyleDef? toEnsure) = chooseTarget(sheet, reference);
            if (toEnsure is null && string.Equals(name, reference, StringComparison.Ordinal))
            {
                return false;
            }

            if (toEnsure is not null)
            {
                _session.Execute(new EnsureCharacterStyleCommand(toEnsure));
            }

            _pendingCharacterStyleRef = name;
            RaiseChanged();
            return true;
        }

        List<(int Paragraph, int Offset, int Length, string EffectiveRef)> spans =
            EnumerateStyleSpans(_selection.Range);
        if (spans.Count == 0)
        {
            return false;
        }

        var children = new List<IDocumentCommand>();
        var ensured = new HashSet<string>(StringComparer.Ordinal);
        string storyId = _selection.StoryId;
        bool anythingChanges = false;
        foreach ((int paragraph, int offset, int length, string effectiveRef) in spans)
        {
            (string target, CharacterStyleDef? toEnsure) = chooseTarget(sheet, effectiveRef);
            if (toEnsure is not null && ensured.Add(toEnsure.Name))
            {
                children.Insert(0, new EnsureCharacterStyleCommand(toEnsure));
            }

            // M70(d): a span whose chosen target is the style it already has is a command that
            // rewrites a run as itself. Left in, it put a step on the undo stack, marked the
            // newsletter unsaved and let the shell report a change nobody could see.
            anythingChanges |= toEnsure is not null
                || !string.Equals(target, effectiveRef, StringComparison.Ordinal);

            string? applied = target == ParagraphDefaultRef(paragraph) ? null : target;
            children.Add(new ApplyCharacterStyleCommand(storyId, paragraph, offset, length, applied));
        }

        if (!anythingChanges)
        {
            return false;
        }

        _session.Execute(new CompositeCommand(
            description,
            new ChangeScope(ChangeKind.Text, StoryId: storyId),
            children));
        RaiseChanged();
        return true;
    }

    public void ApplyParagraphStyle(string paragraphStyleRef)
    {
        if (!IsActive)
        {
            return;
        }

        // Throws before any mutation when the style is unknown.
        _ = _session.Document.StyleSheet.GetParagraphStyle(paragraphStyleRef);
        TextRange range = _selection.Range;
        string storyId = range.StoryId;
        if (range.Start.ParagraphIndex == range.End.ParagraphIndex)
        {
            _session.Execute(new ApplyParagraphStyleCommand(storyId, range.Start.ParagraphIndex, paragraphStyleRef));
        }
        else
        {
            var children = new List<IDocumentCommand>();
            for (int p = range.Start.ParagraphIndex; p <= range.End.ParagraphIndex; p++)
            {
                children.Add(new ApplyParagraphStyleCommand(storyId, p, paragraphStyleRef));
            }

            _session.Execute(new CompositeCommand(
                "Change paragraph style",
                new ChangeScope(ChangeKind.Text, StoryId: storyId),
                children));
        }

        RaiseChanged();
    }

    private bool IsFormatActive(Func<CharacterStyleDef, bool> predicate)
    {
        if (!IsActive)
        {
            return false;
        }

        StyleSheet sheet = _session.Document.StyleSheet;
        if (_selection.IsEmpty)
        {
            string? reference = _pendingCharacterStyleRef
                ?? EffectiveRefAt(_selection.Caret.ParagraphIndex, _selection.Caret.Offset);
            return reference is not null && predicate(sheet.GetCharacterStyle(reference));
        }

        List<(int Paragraph, int Offset, int Length, string EffectiveRef)> spans = EnumerateStyleSpans(_selection.Range);
        return spans.Count > 0 && spans.All(s => predicate(sheet.GetCharacterStyle(s.EffectiveRef)));
    }

    private void ToggleFormat(
        bool isActive,
        Func<CharacterStyleDef, bool, (FontWeightToken Weight, FontSlantToken Slant)> makeTarget,
        string activeDescription,
        string inactiveDescription)
    {
        if (!IsActive)
        {
            return;
        }

        StyleSheet sheet = _session.Document.StyleSheet;
        if (_selection.IsEmpty)
        {
            // Pending style: applied by the next InsertText, cleared by any motion (§6.1).
            string? reference = _pendingCharacterStyleRef
                ?? EffectiveRefAt(_selection.Caret.ParagraphIndex, _selection.Caret.Offset);
            if (reference is null)
            {
                return;
            }

            CharacterStyleDef def = sheet.GetCharacterStyle(reference);
            (FontWeightToken w, FontSlantToken s) = makeTarget(def, isActive);
            _pendingCharacterStyleRef = ResolveOrDeriveVariant(sheet, reference, w, s, out CharacterStyleDef? toEnsure);
            if (toEnsure is not null)
            {
                _session.Execute(new EnsureCharacterStyleCommand(toEnsure));
            }

            RaiseChanged();
            return;
        }

        List<(int Paragraph, int Offset, int Length, string EffectiveRef)> spans = EnumerateStyleSpans(_selection.Range);
        if (spans.Count == 0)
        {
            return;
        }

        var children = new List<IDocumentCommand>();
        var ensured = new HashSet<string>(StringComparer.Ordinal);
        string storyId = _selection.StoryId;
        foreach ((int paragraph, int offset, int length, string effectiveRef) in spans)
        {
            CharacterStyleDef def = sheet.GetCharacterStyle(effectiveRef);
            (FontWeightToken w, FontSlantToken s) = makeTarget(def, isActive);
            string target = ResolveOrDeriveVariant(sheet, effectiveRef, w, s, out CharacterStyleDef? toEnsure);
            if (toEnsure is not null && ensured.Add(toEnsure.Name))
            {
                children.Insert(0, new EnsureCharacterStyleCommand(toEnsure));
            }

            // Resolving back to the paragraph default becomes a null ref so the run merges
            // with inherited neighbours (canonical form counts refs, not resolved styles).
            string? applied = target == ParagraphDefaultRef(paragraph) ? null : target;
            children.Add(new ApplyCharacterStyleCommand(storyId, paragraph, offset, length, applied));
        }

        _session.Execute(new CompositeCommand(
            isActive ? activeDescription : inactiveDescription,
            new ChangeScope(ChangeKind.Text, StoryId: storyId),
            children));
        RaiseChanged();
    }

    private static string ResolveOrDeriveVariant(
        StyleSheet sheet,
        string sourceRef,
        FontWeightToken weight,
        FontSlantToken slant,
        out CharacterStyleDef? toEnsure)
    {
        toEnsure = null;
        if (CharacterStyleResolver.TryResolve(sheet, sourceRef, weight, slant, out CharacterStyleDef existing))
        {
            return existing.Name;
        }

        string name = CharacterStyleResolver.VariantName(CharacterStyleResolver.BaseName(sourceRef), weight, slant);
        toEnsure = CharacterStyleResolver.Derive(sheet.GetCharacterStyle(sourceRef), name, weight, slant);
        return name;
    }

    /// <summary>Contiguous same-effective-style spans inside the range, per paragraph.</summary>
    private List<(int Paragraph, int Offset, int Length, string EffectiveRef)> EnumerateStyleSpans(TextRange range)
    {
        var spans = new List<(int, int, int, string)>();
        Story story = CurrentStory();
        for (int p = range.Start.ParagraphIndex; p <= range.End.ParagraphIndex; p++)
        {
            StoryParagraph paragraph = story.Paragraphs[p];
            int from = p == range.Start.ParagraphIndex ? range.Start.Offset : 0;
            int to = p == range.End.ParagraphIndex ? range.End.Offset : paragraph.Length;
            if (to <= from)
            {
                continue;
            }

            int cursor = from;
            int runStart = 0;
            foreach (StoryRun run in paragraph.Runs)
            {
                int runEnd = runStart + run.Text.Length;
                int s = Math.Max(cursor, runStart);
                int e = Math.Min(to, runEnd);
                if (e > s)
                {
                    string effective = run.CharacterStyleRef ?? ParagraphDefaultRef(p);
                    if (spans.Count > 0
                        && spans[^1].Item1 == p
                        && spans[^1].Item4 == effective
                        && spans[^1].Item2 + spans[^1].Item3 == s)
                    {
                        spans[^1] = (p, spans[^1].Item2, spans[^1].Item3 + (e - s), effective);
                    }
                    else
                    {
                        spans.Add((p, s, e - s, effective));
                    }
                }

                runStart = runEnd;
            }
        }

        return spans;
    }

    private string ParagraphDefaultRef(int paragraphIndex)
    {
        StoryParagraph paragraph = CurrentStory().Paragraphs[paragraphIndex];
        return _session.Document.StyleSheet.GetParagraphStyle(paragraph.ParagraphStyleRef).CharacterStyleRef;
    }

    private string? EffectiveRefAt(int paragraphIndex, int offset)
    {
        StoryParagraph paragraph = CurrentStory().Paragraphs[paragraphIndex];
        return StoryText.StyleRefAt(paragraph, offset) ?? ParagraphDefaultRef(paragraphIndex);
    }

    // ---- View coupling ----------------------------------------------------------------------

    public bool TryGetCaretGeometry(out CaretGeometry geometry)
    {
        geometry = default;
        return IsActive
            && TryGetGeometryService(out StoryTextGeometry? service)
            && service.TryGetCaretGeometry(_selection.Caret, out geometry);
    }

    public IReadOnlyList<SelectionRect> GetSelectionRects()
    {
        if (!IsActive || _selection.IsEmpty || !TryGetGeometryService(out StoryTextGeometry? service))
        {
            return [];
        }

        return service.GetSelectionRects(_selection.Range);
    }

    /// <summary>The logical caret sits past the laid-out text (frame chain full); editing still
    /// works but the caret has no geometry (docs/M4-spec.md §2.5).</summary>
    public bool IsCaretOverset =>
        IsActive
        && TryGetGeometryService(out StoryTextGeometry? service)
        && !service.TryGetCaretGeometry(_selection.Caret, out _);

    // ---- Internals --------------------------------------------------------------------------

    private Story CurrentStory() => _session.Document.GetStory(_selection.StoryId);

    private string ParagraphText(int paragraphIndex) =>
        StoryNavigator.GetParagraphText(CurrentStory().Paragraphs[paragraphIndex]);

    private bool TryGetGeometryService(out StoryTextGeometry service)
    {
        service = null!;
        return IsActive && _layout.TryGetStoryGeometry(_selection.StoryId, out service);
    }

    private void SetCaret(CaretPosition caret, bool clearGoal = true)
    {
        _selection = TextSelection.At(caret);
        if (clearGoal)
        {
            _xGoal = null;
        }

        RaiseChanged();
    }

    private void OnDocumentChanged()
    {
        if (!IsActive)
        {
            return;
        }

        // Undo/redo can take the story out from under the caret altogether, not merely move text
        // inside it: "add a text frame" is one composite command over a story AND a block, so
        // Ctrl+Z straight after clicking into the new frame removes the story this session is
        // pointing at. Clamping cannot describe that, and trying threw KeyNotFoundException out of
        // an event handler — the app went down. A session whose story has gone is simply over.
        // FrameEditorController.OnDocumentChanged has handled the same case for its block since M5;
        // this is that guard, for the other half of the pair.
        if (!_session.Document.TryGetStory(_selection.StoryId, out Story? story)
            || story.Paragraphs.Count == 0)
        {
            End();
            return;
        }

        // Undo/redo can also move text under the caret; clamp both endpoints to valid document
        // coordinates snapped to grapheme boundaries (docs/M4-spec.md §7.4).
        _selection = new TextSelection(Clamp(story, _selection.Anchor), Clamp(story, _selection.Extent));
        RaiseChanged();
    }

    private static CaretPosition Clamp(Story story, CaretPosition caret)
    {
        int paragraph = Math.Clamp(caret.ParagraphIndex, 0, story.Paragraphs.Count - 1);
        string text = StoryNavigator.GetParagraphText(story.Paragraphs[paragraph]);
        int offset = StoryNavigator.SnapToGrapheme(text, Math.Clamp(caret.Offset, 0, text.Length));
        return new CaretPosition(new TextPosition(caret.StoryId, paragraph, offset), caret.Affinity);
    }

    private static string Sanitize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // Tabs become a SPACE, not nothing. char.IsControl('\t') is true, so the filter below was
        // deleting them outright and columnar text pasted from a spreadsheet or an email arrived
        // with its words run together — "Name\tOffice" pasted as "NameOffice" (review §14.2).
        // This engine has no tab stops, so a tab cannot be honoured; turning it into the separator
        // it was standing in for at least keeps the words apart.
        return string.Concat(
            text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Replace('\t', ' ')
                .Where(c => c == '\n' || !char.IsControl(c)));
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void RequestReveal()
    {
        if (RevealRequested is null || !TryGetCaretGeometry(out CaretGeometry geometry))
        {
            return;
        }

        int pageIndex = 0;
        if (geometry.BlockId is { } blockId && _layout.TryGetPageIndexOfBlock(blockId, out int found))
        {
            pageIndex = found;
        }

        RevealRequested.Invoke(this, new CaretRevealEventArgs(
            pageIndex,
            geometry.XPt - 4f,
            geometry.TopPt - 4f,
            geometry.XPt + 4f,
            geometry.BottomPt + 4f));
    }
}
