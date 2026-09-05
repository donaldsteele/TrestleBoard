using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Editing;

/// <summary>
/// How spaced out the writing is, and whether each paragraph starts pushed in (PLAN.md §11 M93).
///
/// <para><b>This closes the largest of the "built, working, unreachable" gaps.</b>
/// <see cref="ParagraphStyleDef.LineSpacing"/>, <see cref="ParagraphStyleDef.SpaceBeforePt"/>,
/// <see cref="ParagraphStyleDef.SpaceAfterPt"/> and <see cref="ParagraphStyleDef.FirstLineIndentPt"/>
/// have been honoured by the layout engine since M1, are set by every template, and were reachable
/// by no command in the application. The hard half was finished and tested; only the verb was
/// missing.</para>
///
/// <para><b>Three named choices, not four numbers.</b> §6 is explicit that this audience should not
/// be asked to operate spinners over points and multipliers to say a thing they can say in a word.
/// "Roomier" is the sentence the committee actually says when a page looks cramped, and the app
/// already prefers naming outcomes over numbers — "Make it fit what is in it", "Make the rest fit".
/// The numbers behind each choice are here, in one place, where they can be read and argued with.</para>
///
/// <para><b>It changes a style, so it changes the whole newsletter.</b> That is the locked
/// constraint (§1, M14) — nothing carries direct formatting — and it is also what is meant: nobody
/// asks for the third paragraph on page four to be roomier than the rest.</para>
/// </summary>
public sealed class WritingLookController
{
    /// <summary>The style the articles are written in — what "the writing" means here.</summary>
    public const string BodyStyleName = "body";

    private readonly DocumentSession _session;

    public WritingLookController(DocumentSession session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>How much air the writing has.</summary>
    public enum Spacing
    {
        /// <summary>Lines close together — for a month with too much news for the pages.</summary>
        Tight,

        /// <summary>What the templates ship with.</summary>
        Normal,

        /// <summary>More air, which is easier on older eyes and is why it is offered at all.</summary>
        Roomy,
    }

    /// <summary>
    /// The numbers behind each choice. Line spacing is a multiple of the type size and the gap
    /// after a paragraph is in points, which is how <c>TextLayoutEngine</c> reads them.
    ///
    /// <para><b>Normal is exactly what <c>StandardStyles</c> ships</b> (1.3 and 7pt), so choosing
    /// Normal on an untouched newsletter changes nothing at all, and a user who wanders through the
    /// three choices and comes back can get to where they started without Ctrl+Z.</para>
    /// </summary>
    public static (float LineSpacing, float SpaceAfterPt) NumbersFor(Spacing spacing) => spacing switch
    {
        Spacing.Tight => (1.15f, 4f),
        Spacing.Normal => (1.3f, 7f),
        Spacing.Roomy => (1.5f, 11f),
        _ => throw new ArgumentOutOfRangeException(nameof(spacing)),
    };

    /// <summary>How far in a paragraph's first line starts when indenting is on (M93).</summary>
    public const float IndentPt = 14f;

    /// <summary>True when there is a body style to change — every real newsletter has one.</summary>
    public bool CanChangeTheWritingsLook => Body is not null;

    /// <summary>
    /// Which of the three the newsletter is set to now, or null when it matches none of them —
    /// which a newsletter written before this existed, or edited by a later version, legitimately
    /// can. The dialog shows no choice as current rather than lying about one.
    /// </summary>
    public Spacing? CurrentSpacing
    {
        get
        {
            if (Body is not { } body)
            {
                return null;
            }

            foreach (Spacing candidate in Enum.GetValues<Spacing>())
            {
                (float lineSpacing, float spaceAfter) = NumbersFor(candidate);
                if (Near(body.LineSpacing, lineSpacing) && Near(body.SpaceAfterPt, spaceAfter))
                {
                    return candidate;
                }
            }

            return null;
        }
    }

    /// <summary>Whether each paragraph currently starts pushed in.</summary>
    public bool FirstLineIsIndented => Body is { FirstLineIndentPt: > 0f };

    /// <summary>
    /// Sets how spaced out the writing is. Returns false when it is already that way, so the shell
    /// can say so instead of announcing a change that did not happen (M70(c)).
    /// </summary>
    public bool SetSpacing(Spacing spacing)
    {
        if (Body is not { } body)
        {
            return false;
        }

        (float lineSpacing, float spaceAfter) = NumbersFor(spacing);
        if (Near(body.LineSpacing, lineSpacing) && Near(body.SpaceAfterPt, spaceAfter))
        {
            return false;
        }

        _session.Execute(new SetParagraphSpacingCommand(
            BodyStyleName, lineSpacing, spaceBeforePt: null, spaceAfter, firstLineIndentPt: null));
        return true;
    }

    /// <summary>Turns the first-line indent on or off. False when it is already that way.</summary>
    public bool SetFirstLineIndent(bool indented)
    {
        if (Body is not { } body || FirstLineIsIndented == indented)
        {
            return false;
        }

        _session.Execute(new SetParagraphSpacingCommand(
            BodyStyleName,
            lineSpacing: null,
            spaceBeforePt: null,
            spaceAfterPt: null,
            indented ? IndentPt : 0f));
        return body is not null;
    }

    private ParagraphStyleDef? Body =>
        _session.Document.StyleSheet.ParagraphStyles.Find(s => s.Name == BodyStyleName);

    /// <summary>
    /// Points and multipliers are floats that have been through a JSON round trip, so they are
    /// compared with a tolerance rather than with <c>==</c> — the alternative is a dialog that
    /// shows no choice as current on a newsletter it wrote itself.
    /// </summary>
    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;
}
