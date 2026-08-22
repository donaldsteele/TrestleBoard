using System.Globalization;
using System.Text;
using TrestleBoard.Editing.Actions;

namespace TrestleBoard.Site;

/// <summary>
/// The command reference, projected from <see cref="ActionCatalog"/>.
///
/// <para><b>This page is the reason the site has a build step at all.</b>
/// <c>TrestleBoard.Editing/Help/HelpIndex.cs</c> generates the in-app "How do I…?" index from the
/// same catalog, and says why in a comment worth repeating here: help that is written by hand is a
/// promise nobody can keep, because the wording drifts, a command is renamed, a shortcut moves, and
/// the help goes on confidently describing the app of eighteen months ago. A website is that
/// promise one layer further from the code. So the reference is not written; it is read out of the
/// declaration the menu bar, the action panel, the keyboard table and the in-app help all read, and
/// it is rebuilt on every push.</para>
///
/// <para>What is deliberately NOT here: whether a command is available, and the plain sentence it
/// gives when it is not. Those are answers about a live newsletter — <c>ActionCatalog.Evaluate</c>
/// needs a context, and a context needs a document. A website cannot have one, and inventing a
/// plausible availability would be the site telling somebody something untrue about their own copy.
/// The page says where each command lives instead, and points at the app's own help window, which
/// can answer the availability question because it is standing in the newsletter.</para>
/// </summary>
internal static class Reference
{
    /// <summary>
    /// What each group is called on the site, and the one line under it.
    ///
    /// <para>The enum's own names are C# identifiers — "TextFlow", "Everything" — and two of them
    /// would actively mislead a reader. These are the words the app uses when it talks about the
    /// same things out loud. The map is exhaustive by construction: <see cref="Build"/> throws on a
    /// group it has no entry for, so adding a group to the enum breaks the site build rather than
    /// quietly publishing a heading that says "Everything".</para>
    /// </summary>
    private static readonly Dictionary<ActionGroup, (string Title, string Blurb)> Groups = new()
    {
        [ActionGroup.Newsletter] = (
            "The newsletter",
            "Starting one, opening one, saving it, and making the PDF you email round."),
        [ActionGroup.Edit] = (
            "Undoing and the clipboard",
            "Everything can be undone, including the things most programs will not let you take back."),
        [ActionGroup.Text] = (
            "Writing",
            "Typing, bold and italic, the kind of writing a paragraph is, and choosing a typeface."),
        [ActionGroup.Insert] = (
            "Putting something on the page",
            "A box for writing, a photograph, and the lists the app lays out for you."),
        [ActionGroup.Item] = (
            "Whatever you have chosen",
            "These act on the thing selected on the page right now, and appear beside it when it is."),
        [ActionGroup.Picture] = (
            "Photographs",
            "Your original file is never altered — every change is a recipe applied when the page is drawn."),
        [ActionGroup.TextFlow] = (
            "How the writing flows",
            "Around a picture, and on from one box into the next."),
        [ActionGroup.Arrange] = (
            "Front and back",
            "Which of two overlapping things is on top."),
        [ActionGroup.Page] = (
            "Pages",
            "Moving between them, adding one, and changing the order they come in."),
        [ActionGroup.View] = (
            "Looking at it",
            "How large the page is shown, how large the app's own text is, and which theme is on."),
        [ActionGroup.People] = (
            "The address book",
            "The lodge's members. This is the app's own list, not part of any one newsletter."),
        [ActionGroup.Everything] = (
            "Handing over",
            "Packing up everything a successor needs, and taking in a pack from whoever came before."),
        [ActionGroup.Help] = (
            "Help and updates",
            "The example newsletter, the guided tour, the licence, and checking for a new version."),
    };

    internal static string Build()
    {
        IReadOnlyList<EditorAction> all = ActionCatalog.All;
        var html = new StringBuilder();

        html.Append(
            CultureInfo.InvariantCulture,
            $"""
            <p class="lede">
              Every command TrestleBoard has — {all.Count.ToString(CultureInfo.InvariantCulture)} of
              them — with the sentence the app itself uses to explain it, and its keyboard shortcut
              where it has one.
            </p>

            <div class="note">
              <p><strong>This page is not written, it is read out of the program.</strong> The
              application keeps one declaration of what it can do, and the menu bar, the panel beside
              your selection, the keyboard shortcuts and the in-app help are all views of it. So is
              this page, rebuilt every time the program changes — which is what stops a help page
              quietly describing a version nobody is running.</p>
              <p>Inside TrestleBoard the same list answers questions and can act on them: press
              <kbd>F1</kbd>, type what you are trying to do in your own words, and the answer has a
              <strong>Take me there</strong> button that runs it.</p>
            </div>

            <div class="finder">
              <label class="finder-label" for="command-search">Find a command</label>
              <input id="command-search" type="search" autocomplete="off"
                     placeholder="Type a word, such as photo, birthday or PDF">
              <p class="finder-count" id="command-count" role="status" aria-live="polite"></p>
            </div>

            """);

        foreach (ActionGroup group in Enum.GetValues<ActionGroup>())
        {
            if (!Groups.TryGetValue(group, out (string Title, string Blurb) heading))
            {
                throw new InvalidOperationException(
                    $"ActionGroup.{group} has no heading on the site. Add one to Reference.Groups — "
                    + "this build fails on purpose rather than publishing the enum's identifier.");
            }

            EditorAction[] inGroup = [.. all.Where(a => a.Group == group)];
            if (inGroup.Length == 0)
            {
                continue;
            }

            string id = group.ToString().ToLowerInvariant();

            html.Append(
                CultureInfo.InvariantCulture,
                $"""
                <section class="commands" id="{id}" data-group>
                  <h2>{Html.Escape(heading.Title)}</h2>
                  <p class="group-blurb">{Html.Escape(heading.Blurb)}</p>
                  <ul class="command-list">

                """);

            foreach (EditorAction action in inGroup)
            {
                string keys = action.DisplayGesture is { Length: > 0 } gesture
                    ? $"<kbd class=\"shortcut\">{Html.Escape(gesture)}</kbd>"
                    : string.Empty;

                string search = Html.Attribute(
                    $"{action.Title} {action.ShortDescription} {heading.Title}".ToLowerInvariant());

                html.Append(
                    CultureInfo.InvariantCulture,
                    $"""
                      <li class="command" data-search="{search}">
                        <p class="command-name">{Html.Escape(action.Title)}{keys}</p>
                        <p class="command-what">{Html.Escape(action.ShortDescription)}</p>
                      </li>

                    """);
            }

            html.Append(
                """
                  </ul>
                </section>

                """);
        }

        html.Append(
            """
            <p class="finder-empty" id="command-empty" hidden>
              Nothing here matches that. Try one word rather than several — the app's own
              <kbd>F1</kbd> window searches the same list and knows more words for the same things.
            </p>

            """);

        return html.ToString();
    }
}
