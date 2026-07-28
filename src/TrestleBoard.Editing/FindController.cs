using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Text;

namespace TrestleBoard.Editing;

/// <summary>
/// Find and replace (PLAN.md §11 M21). Headless like every other controller here: it owns what is
/// being looked for and where the last hit was, drives <see cref="TextEditorController"/> to show
/// it, and turns a replacement into an <see cref="IDocumentCommand"/> — never a direct mutation.
///
/// <para>Every sentence it produces is written for the user, because they all end up in the find
/// window and in the status bar, which is a polite live region (PLAN.md §6). In particular the
/// nothing-found message says what was NOT searched: widget payloads are out of scope for pass one,
/// and a search that silently skipped the officers table would teach the user the words are not
/// there when they are.</para>
/// </summary>
public sealed class FindController
{
    /// <summary>The part of the newsletter this pass cannot look inside, said out loud.</summary>
    public const string WidgetsNotSearched =
        "TrestleBoard does not look inside the lists it fills in for you, such as the officers "
        + "table or the birthday list.";

    private readonly DocumentSession _session;
    private readonly TextEditorController _editor;
    private FindHit? _current;

    public FindController(DocumentSession session, TextEditorController editor)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _session.Changed += (_, _) => _current = null;
    }

    /// <summary>Raised whenever the message or the current hit changes.</summary>
    public event EventHandler? Changed;

    /// <summary>What is being looked for.</summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>What it is being replaced with; only "replace" reads it.</summary>
    public string ReplacementText { get; set; } = string.Empty;

    /// <summary>Whether upper and lower case have to agree. Off by default — the kinder default.</summary>
    public bool MatchCase { get; set; }

    /// <summary>The last thing this said, in plain language.</summary>
    public string? StatusMessage { get; private set; }

    /// <summary>Where the last hit was, or null before the first search.</summary>
    public FindHit? Current => _current;

    /// <summary>How many times the words appear in the newsletter's writing right now.</summary>
    public int CountAll() => StoryFinder.All(_session.Document, SearchText, MatchCase).Count;

    /// <summary>
    /// Finds the next occurrence after the last one, wrapping round at the end, and highlights it.
    /// </summary>
    public bool FindNext()
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            Say("Type the words you are looking for first.");
            return false;
        }

        FindHit? hit = StoryFinder.Next(
            _session.Document, SearchText, _current, MatchCase, out bool wrapped);
        if (hit is not { } found)
        {
            _current = null;
            Say($"“{SearchText}” is not in the writing on the page. {WidgetsNotSearched}");
            return false;
        }

        if (!_editor.SelectRange(found.StoryId, found.ParagraphIndex, found.Offset, found.Length))
        {
            _current = null;
            Say($"“{SearchText}” was there a moment ago but is not any more. Try again.");
            return false;
        }

        _current = found;
        int total = CountAll();
        Say(wrapped
            ? $"Carried on from the beginning. {Describe(total)}"
            : Describe(total));
        return true;
    }

    /// <summary>
    /// Replaces the highlighted occurrence and moves to the next one — one
    /// <see cref="IDocumentCommand"/>, so one Ctrl+Z puts the old words back.
    /// </summary>
    public bool ReplaceCurrent()
    {
        if (_current is not { } hit)
        {
            // Nothing is highlighted yet: find the first one rather than refusing. Replace is
            // reached by pressing a button labelled "Replace", and the user means "get on with it".
            return FindNext();
        }

        if (!Matches(hit))
        {
            _current = null;
            return FindNext();
        }

        _session.Execute(BuildReplacement(hit, ReplacementText, "Replace words"));

        // The document changed, so the hit coordinates moved; carry on from where this one ended.
        _current = hit with { Length = ReplacementText.Length };
        Say($"Replaced one. {CountAll()} left.");
        FindNext();
        return true;
    }

    /// <summary>
    /// Replaces every occurrence as ONE undo step. Replacing forty words is one thing the user did,
    /// and forty Ctrl+Zs to take it back would be a punishment for using the command.
    /// </summary>
    public int ReplaceAll()
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            Say("Type the words you are looking for first.");
            return 0;
        }

        IReadOnlyList<FindHit> hits = StoryFinder.All(_session.Document, SearchText, MatchCase);
        if (hits.Count == 0)
        {
            Say($"“{SearchText}” is not in the writing on the page. {WidgetsNotSearched}");
            return 0;
        }

        // Back to front: each replacement changes the offsets after it, and none of the offsets
        // before it.
        var children = new List<IDocumentCommand>();
        for (int i = hits.Count - 1; i >= 0; i--)
        {
            children.AddRange(ReplacementChildren(hits[i], ReplacementText));
        }

        _session.Execute(new CompositeCommand(
            "Replace all", new ChangeScope(ChangeKind.StoryStructure), children));
        _current = null;
        Say(hits.Count == 1
            ? $"Replaced one “{SearchText}”."
            : $"Replaced all {hits.Count} of them.");
        return hits.Count;
    }

    /// <summary>Forgets where the last hit was, so the next search starts from the beginning.</summary>
    public void Restart()
    {
        _current = null;
        StatusMessage = null;
        Raise();
    }

    private static IEnumerable<IDocumentCommand> ReplacementChildren(FindHit hit, string replacement)
    {
        yield return new DeleteTextCommand(hit.StoryId, hit.ParagraphIndex, hit.Offset, hit.Length);
        if (replacement.Length > 0)
        {
            yield return new InsertTextCommand(hit.StoryId, hit.ParagraphIndex, hit.Offset, replacement);
        }
    }

    private static CompositeCommand BuildReplacement(FindHit hit, string replacement, string description) =>
        new CompositeCommand(
            description,
            new ChangeScope(ChangeKind.Text, StoryId: hit.StoryId),
            [.. ReplacementChildren(hit, replacement)]);

    /// <summary>Is the remembered hit still the words we are looking for?</summary>
    private bool Matches(FindHit hit)
    {
        if (_session.Document.Stories.Find(s => s.Id == hit.StoryId) is not { } story
            || hit.ParagraphIndex >= story.Paragraphs.Count)
        {
            return false;
        }

        string text = StoryNavigator.GetParagraphText(story.Paragraphs[hit.ParagraphIndex]);
        return hit.Offset + hit.Length <= text.Length
            && text.Substring(hit.Offset, hit.Length).Equals(
                SearchText, MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    private string Describe(int total) => total <= 1
        ? $"Found “{SearchText}”. It is the only one."
        : $"Found “{SearchText}”. There are {total} altogether.";

    private void Say(string message)
    {
        StatusMessage = message;
        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
