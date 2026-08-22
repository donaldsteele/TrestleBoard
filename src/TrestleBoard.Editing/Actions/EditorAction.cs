namespace TrestleBoard.Editing.Actions;

/// <summary>
/// Where an action belongs in the panel and in the restructured Object menu (PLAN.md §11 M11).
/// The grouping is by what the user is looking at, not by which class implements it.
/// </summary>
public enum ActionGroup
{
    /// <summary>Opening, starting and exporting a newsletter.</summary>
    Newsletter,

    /// <summary>Undo, redo, and the clipboard.</summary>
    Edit,

    /// <summary>Bold, italic, paragraph style.</summary>
    Text,

    /// <summary>Putting a new thing on the page.</summary>
    Insert,

    /// <summary>Acting on whatever is selected right now.</summary>
    Item,

    /// <summary>Pictures.</summary>
    Picture,

    /// <summary>How text flows: wrap, link, continue.</summary>
    TextFlow,

    /// <summary>Front-to-back order.</summary>
    Arrange,

    /// <summary>Pages.</summary>
    Page,

    /// <summary>Zoom, theme, window regions.</summary>
    View,

    /// <summary>
    /// The lodge address book (M12). Its own group because it is app state rather than newsletter
    /// state — nothing here is about what is selected on the page, which is why the selection panel
    /// never shows it and the menu bar always does.
    /// </summary>
    People,

    /// <summary>
    /// The whole installation at once (M64): packing everything up for a successor and bringing a
    /// predecessor's pack in.
    ///
    /// <para>Its own group rather than <see cref="Newsletter"/> or <see cref="People"/>, because it
    /// is neither: a pack is not a newsletter and it is much more than the address book. The help
    /// window prints the group beside every answer, and "Pack everything up — This newsletter"
    /// would have been the app telling somebody the wrong thing about its most consequential file.
    /// Like <see cref="People"/> it never reaches the action panel — nothing here is about what is
    /// selected on the page.</para>
    /// </summary>
    Everything,

    /// <summary>Updates and the about box.</summary>
    Help,
}

/// <summary>
/// One thing the user can do, described the way it is shown to them. This is the *declaration* —
/// how to actually do it lives in the App layer's action runner, which is the whole point of the
/// split: this project owns "can I, and if not, why not, in plain English" and nothing else, so
/// every reason string in the app is testable without an Avalonia session.
/// </summary>
/// <param name="Id">One of the constants on <see cref="ActionId"/>.</param>
/// <param name="Title">What the button says. Sentence case, plain language, no jargon (PLAN.md §6).</param>
/// <param name="ShortDescription">One sentence saying what will happen; shown under the title in the panel.</param>
/// <param name="Group">Which part of the panel and which submenu it belongs to.</param>
/// <param name="DisplayGesture">The shortcut as the user would type it, or null if it has none.</param>
/// <param name="PrimaryRank">
/// How strong this action's claim is to being <b>the one offer in this group most people came
/// for</b>. Zero — the default — is no claim at all; 1 is the group's first choice, 2 the
/// runner-up, and so on. From M16 the panel draws a primary offer with the accent fill, a taller
/// minimum and a gold left bar — three signals, so colour is never the only one (PLAN.md §6).
/// <para><b>M76 (f): this is a rank the group resolves, not a flag each action asserts, and that
/// change is the whole point of it.</b> It used to be a boolean, and fifteen actions set it for
/// themselves — which is how <c>action-panel-photo.png</c> came to show three navy-and-gold slabs
/// stacked in a single panel. "The offer you probably came for" is
/// singular by construction: three simultaneous offers are not three answers to that question,
/// they are the absence of an answer, and the gold bar is the most expensive ornament in the
/// palette — the only place the lodge's own gold is legal — being spent on all of them at once.
/// So the claim is now declared here and <i>settled</i> by
/// <see cref="ActionCatalog.PrimaryOffers"/>, which grants the treatment to at most one action per
/// <see cref="ActionGroup"/>.</para>
/// <para><b>Losing is not being disabled and is not being unstyled.</b> Every action that does not
/// win its group renders with the ordinary action treatment, exactly as the sixty-odd actions that
/// never claimed a rank always have. Nothing in the panel is ever greyed (M11), and
/// <c>ActionSurfaceTests</c> walks every window to prove no button carries neither treatment.</para>
/// <para><b>Ties are impossible rather than broken.</b> Two actions in one group may not share a
/// non-zero rank: <see cref="ActionCatalog"/> refuses to initialise if they do, in the spirit of
/// <c>ActionAvailability</c> refusing an empty reason at construction. The alternative — resolving
/// a tie by declaration order — was rejected because "whichever one happened to be enumerated
/// first" is not a rule anybody can read off the source and predict.</para>
/// <para>This field used to promise that primary actions "sort first in their group" as well.
/// <b>That half is deliberately not implemented and the promise is withdrawn rather than left
/// lying:</b> declaration order in <see cref="ActionCatalog"/> already agrees with it, so a sort
/// would be a near-no-op carrying real risk of reordering a group a test depends on.</para>
/// </param>
public sealed record EditorAction(
    string Id,
    string Title,
    string ShortDescription,
    ActionGroup Group,
    string? DisplayGesture = null,
    int PrimaryRank = 0);
