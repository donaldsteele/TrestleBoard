using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;

namespace TrestleBoard.Core.Commands;

/// <summary>
/// Changes the font family and/or size of a base character style AND every -bold/-italic
/// sibling of it, atomically (PLAN.md M14).
/// <para>
/// One command rather than a <see cref="CompositeCommand"/> of per-style edits, because
/// atomicity is the entire point. <see cref="CharacterStyleResolver.TryResolve"/> falls back to
/// an attribute scan matching same family + same size + same colour; change "body" and leave
/// "body-bold" behind and Ctrl+B silently stops finding its pair and starts minting duplicates
/// through <see cref="EnsureCharacterStyleCommand"/>.
/// </para>
/// <para>
/// Group membership is by NAME — every style whose <see cref="CharacterStyleResolver.BaseName"/>
/// equals <paramref name="baseStyleName"/> — never by attribute scan, since the scan is the very
/// thing being protected. It also composes with the "~" override convention for free: changing
/// "body" correctly leaves "body~ebgaramond" alone, because that bases to itself.
/// </para>
/// </summary>
public sealed class SetCharacterStyleFontCommand(
    string baseStyleName,
    string? fontFamily,
    float? sizePt) : IDocumentCommand
{
    private readonly List<(string Name, string Family, float SizePt)> _before = [];

    public string BaseStyleName { get; } = !string.IsNullOrEmpty(baseStyleName)
        ? baseStyleName
        : throw new ArgumentException("A base style name is required.", nameof(baseStyleName));

    /// <summary>New family, or null to leave every sibling's family alone.</summary>
    public string? FontFamily { get; } = fontFamily;

    /// <summary>New size in points, or null to leave every sibling's size alone.</summary>
    public float? SizePt { get; } = sizePt;

    public string Description => FontFamily is null ? "Change text size" : "Change font";

    public ChangeScope Scope => new(ChangeKind.Metadata);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _before.Clear();
        foreach (CharacterStyleDef style in Group(document))
        {
            _before.Add((style.Name, style.FontFamily, style.SizePt));
            if (FontFamily is not null)
            {
                style.FontFamily = FontFamily;
            }

            if (SizePt is not null)
            {
                style.SizePt = SizePt.Value;
            }
        }
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach ((string name, string family, float sizePt) in _before)
        {
            CharacterStyleDef? style = document.StyleSheet.CharacterStyles.Find(s => s.Name == name);
            if (style is null)
            {
                continue;
            }

            style.FontFamily = family;
            style.SizePt = sizePt;
        }

        _before.Clear();
    }

    /// <summary>
    /// Never merges. Two font changes in a row are two things the user did and expects two
    /// undo steps for — and a merged pair would have to reconcile two different snapshots.
    /// </summary>
    public bool TryMerge(IDocumentCommand newer) => false;

    /// <summary>The base style and every -bold/-italic sibling of it, in stylesheet order.</summary>
    public IEnumerable<CharacterStyleDef> Group(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.StyleSheet.CharacterStyles
            .Where(s => CharacterStyleResolver.BaseName(s.Name) == BaseStyleName);
    }
}

/// <summary>
/// Changes how a paragraph style sits on the page — the space between its lines, the gap above and
/// below it, and how far its first line is pushed in (PLAN.md §11 M93).
///
/// <para><b>Every one of these four has been honoured by the layout engine since M1 and reachable
/// by nobody.</b> <see cref="ParagraphStyleDef.LineSpacing"/>, <see cref="ParagraphStyleDef.SpaceBeforePt"/>,
/// <see cref="ParagraphStyleDef.SpaceAfterPt"/> and <see cref="ParagraphStyleDef.FirstLineIndentPt"/>
/// are set by the templates, read by <c>TextLayoutEngine</c>, saved and loaded — and no command in
/// the app could change any of them. That is the shape of gap this milestone exists to close: the
/// hard half was finished and tested, and only the verb was missing.</para>
///
/// <para><b>A style, not a paragraph.</b> The locked constraint (PLAN.md §1, M14) is that runs and
/// paragraphs never carry direct formatting — everything is a named style applied by reference. So
/// this edits the style, which means it changes every paragraph wearing it, which is also what the
/// committee means when they ask for the newsletter to be more spaced out.</para>
///
/// <para>Null leaves a field alone, exactly as <see cref="SetCharacterStyleFontCommand"/> does, so
/// one command can carry a spacing change, an indent change, or both.</para>
/// </summary>
public sealed class SetParagraphSpacingCommand(
    string styleName,
    float? lineSpacing,
    float? spaceBeforePt,
    float? spaceAfterPt,
    float? firstLineIndentPt) : IDocumentCommand
{
    private (float LineSpacing, float Before, float After, float Indent)? _before;

    public string StyleName { get; } = !string.IsNullOrEmpty(styleName)
        ? styleName
        : throw new ArgumentException("A style name is required.", nameof(styleName));

    public float? LineSpacing { get; } = lineSpacing;

    public float? SpaceBeforePt { get; } = spaceBeforePt;

    public float? SpaceAfterPt { get; } = spaceAfterPt;

    public float? FirstLineIndentPt { get; } = firstLineIndentPt;

    public string Description => LineSpacing is not null || SpaceAfterPt is not null
        ? "Change how spaced out the writing is"
        : "Change how the first line is indented";

    /// <summary>
    /// Metadata, like every other stylesheet edit: the change is to a named style rather than to
    /// anything on a page, so no single block or story is the dirty scope.
    /// </summary>
    public ChangeScope Scope => new(ChangeKind.Metadata);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        ParagraphStyleDef style = Find(document);
        _before = (style.LineSpacing, style.SpaceBeforePt, style.SpaceAfterPt, style.FirstLineIndentPt);

        style.LineSpacing = LineSpacing ?? style.LineSpacing;
        style.SpaceBeforePt = SpaceBeforePt ?? style.SpaceBeforePt;
        style.SpaceAfterPt = SpaceAfterPt ?? style.SpaceAfterPt;
        style.FirstLineIndentPt = FirstLineIndentPt ?? style.FirstLineIndentPt;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_before is not { } was)
        {
            throw new InvalidOperationException("Revert before Apply.");
        }

        ParagraphStyleDef style = Find(document);
        style.LineSpacing = was.LineSpacing;
        style.SpaceBeforePt = was.Before;
        style.SpaceAfterPt = was.After;
        style.FirstLineIndentPt = was.Indent;
        _before = null;
    }

    /// <summary>
    /// Never merges. The dialog behind this hands over one finished choice when the user presses
    /// the button, so there is no burst to coalesce, and merging two deliberate choices into one
    /// undo step would take away the step between them.
    /// </summary>
    public bool TryMerge(IDocumentCommand newer) => false;

    private ParagraphStyleDef Find(Document document) =>
        document.StyleSheet.ParagraphStyles.Find(s => s.Name == StyleName)
            ?? throw new KeyNotFoundException($"Paragraph style not found: {StyleName}");
}
