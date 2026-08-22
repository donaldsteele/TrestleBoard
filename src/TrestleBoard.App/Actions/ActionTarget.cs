using Avalonia.Controls;

namespace TrestleBoard.App.Actions;

/// <summary>
/// An action id together with the one thing that action needs to be told (M76 (g),
/// docs/M76-spec.md §6).
///
/// <para><b>Why this exists at all.</b> Every surface in this application — the menu bar, the
/// toolbar, the canvas footer, the panel, the right-click flyout — carries the id of the command it
/// runs in its <c>Tag</c>, and one <c>Click</c> handler dispatches on it. That is what makes
/// availability, the plain-language refusal and the code that performs the command come from one
/// place (M11). <c>page.goTo</c> is the first command in the catalog that also needs a
/// <i>parameter</i>: which page. The spec considered giving each page its own <see cref="ActionId"/>
/// and rejected it, because that makes the size of the catalog depend on the newsletter that
/// happens to be open — a catalog that grows when somebody adds a page is not a catalog.</para>
///
/// <para>So the parameter rides <b>beside</b> the id, in the same <c>Tag</c> the id was already
/// riding in, and <c>ActionRunner</c> reads it off the control the user actually pressed. The
/// catalog's entry then means "can you jump to a page at all", which is a true thing it can answer
/// and a true thing it can refuse: with one page there is nowhere to go, and it says so.</para>
///
/// <para><b>A record, and deliberately not an <c>object[]</c> or a tuple.</b> Whatever ends up in a
/// <c>Tag</c> is untyped by the time it is read back, so the one defence available is that the thing
/// put in has a name and a shape a reader can look up. <see cref="IdOf"/> is the only way anything
/// in the shell asks a <c>Tag</c> what command it means, so a control tagged the old way — a bare
/// string — and a control tagged this way are answered by the same line of code.</para>
/// </summary>
/// <param name="ActionId">One of the <see cref="Editing.Actions.ActionId"/> constants.</param>
/// <param name="PageIndex">The page this control means, counted from zero.</param>
internal sealed record ActionTarget(string ActionId, int PageIndex)
{
    /// <summary>
    /// Which command a control's <c>Tag</c> names, or null if it names none.
    ///
    /// <para>Both shapes are answered here so that no caller has to know which surfaces carry which:
    /// a menu item's bare string and a page tile's <see cref="ActionTarget"/> are the same question
    /// asked twice.</para>
    /// </summary>
    internal static string? IdOf(object? tag) => tag switch
    {
        string id => id,
        ActionTarget target => target.ActionId,
        _ => null,
    };

    /// <summary>
    /// The page a control was pressed for, or null when it was not pressed for one — which is the
    /// ordinary case for the menu item, whose whole job is that it does not know one yet.
    /// </summary>
    internal static int? PageOf(Control? control) =>
        control?.Tag is ActionTarget target ? target.PageIndex : null;
}
