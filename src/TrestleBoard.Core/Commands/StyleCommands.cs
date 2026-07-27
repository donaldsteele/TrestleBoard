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
