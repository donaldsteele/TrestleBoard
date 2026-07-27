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

    public async Task PasteAsync()
    {
        if (!IsActive)
        {
            return;
        }

        string? text = await _clipboard.GetTextAsync().ConfigureAwait(true);
        string sanitized = Sanitize(text ?? "");
        if (sanitized.Length == 0)
        {
            return;
        }

        if (sanitized.Contains('\n', StringComparison.Ordinal))
        {
            PasteText(sanitized);
        }
        else
        {
            InsertText(sanitized);
        }
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
    public void UseFontJustHere(string fontFamily, float? sizePt)
    {
        ArgumentException.ThrowIfNullOrEmpty(fontFamily);
        RetargetSpans(
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

    /// <summary>Puts overridden text back on its role's own font, leaving bold and italic alone.</summary>
    public void ClearFontOverride()
    {
        RetargetSpans(
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
    /// Retargets every span in the selection (or the caret's pending style) at a style chosen per
    /// span. Factored out because "use a different font here" and "put it back" differ only in how
    /// they name the target.
    /// </summary>
    private void RetargetSpans(
        string description,
        Func<StyleSheet, string, (string Name, CharacterStyleDef? ToEnsure)> chooseTarget)
    {
        if (!IsActive)
        {
            return;
        }

        StyleSheet sheet = _session.Document.StyleSheet;
        if (_selection.IsEmpty)
        {
            string? reference = _pendingCharacterStyleRef
                ?? EffectiveRefAt(_selection.Caret.ParagraphIndex, _selection.Caret.Offset);
            if (reference is null)
            {
                return;
            }

            (string name, CharacterStyleDef? toEnsure) = chooseTarget(sheet, reference);
            if (toEnsure is not null)
            {
                _session.Execute(new EnsureCharacterStyleCommand(toEnsure));
            }

            _pendingCharacterStyleRef = name;
            RaiseChanged();
            return;
        }

        List<(int Paragraph, int Offset, int Length, string EffectiveRef)> spans =
            EnumerateStyleSpans(_selection.Range);
        if (spans.Count == 0)
        {
            return;
        }

        var children = new List<IDocumentCommand>();
        var ensured = new HashSet<string>(StringComparer.Ordinal);
        string storyId = _selection.StoryId;
        foreach ((int paragraph, int offset, int length, string effectiveRef) in spans)
        {
            (string target, CharacterStyleDef? toEnsure) = chooseTarget(sheet, effectiveRef);
            if (toEnsure is not null && ensured.Add(toEnsure.Name))
            {
                children.Insert(0, new EnsureCharacterStyleCommand(toEnsure));
            }

            string? applied = target == ParagraphDefaultRef(paragraph) ? null : target;
            children.Add(new ApplyCharacterStyleCommand(storyId, paragraph, offset, length, applied));
        }

        _session.Execute(new CompositeCommand(
            description,
            new ChangeScope(ChangeKind.Text, StoryId: storyId),
            children));
        RaiseChanged();
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

        // Undo/redo can move text under the caret; clamp both endpoints to valid document
        // coordinates snapped to grapheme boundaries (docs/M4-spec.md §7.4).
        _selection = new TextSelection(Clamp(_selection.Anchor), Clamp(_selection.Extent));
        RaiseChanged();
    }

    private CaretPosition Clamp(CaretPosition caret)
    {
        Story story = CurrentStory();
        int paragraph = Math.Clamp(caret.ParagraphIndex, 0, story.Paragraphs.Count - 1);
        string text = StoryNavigator.GetParagraphText(story.Paragraphs[paragraph]);
        int offset = StoryNavigator.SnapToGrapheme(text, Math.Clamp(caret.Offset, 0, text.Length));
        return new CaretPosition(new TextPosition(caret.StoryId, paragraph, offset), caret.Affinity);
    }

    private static string Sanitize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Concat(
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
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
