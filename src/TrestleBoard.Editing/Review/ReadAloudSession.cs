using System;
using System.Collections.Generic;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;

namespace TrestleBoard.Editing.Review;

/// <summary>
/// Walking the newsletter one sentence at a time (PLAN.md §11 M58).
///
/// <para>The committee's best proofreading tool for aging eyes is hearing the text: errors the eye
/// slides over, the ear catches. Today that means recruiting a second person to read aloud.</para>
///
/// <para><b>There is no audio in this class, and that is the point.</b> Where a voice engine
/// answers, the shell asks it to speak whatever <see cref="Current"/> is; where none answers — a
/// bare Linux box, a machine with no voices installed — the very same walk is driven by the
/// spacebar and the sentence is highlighted rather than spoken. The silent mode is not a
/// consolation prize: it is independently valuable, it is what every test here exercises, and it is
/// the reason this milestone does not need a sound card to be verified.</para>
///
/// <para><b>M73 (g): the walk is re-read from the document, not remembered.</b> Handed a live
/// <see cref="DocumentSession"/> this class copies <c>FindController</c>'s one-line pattern —
/// subscribe to <c>Changed</c> — but it does NOT copy what <c>FindController</c> does with it.
/// Find throws its place away, because the next thing a searcher does is press "Find next". Here
/// the user is proofreading, and editing what they hear is not an edge case, it is the workflow:
/// throwing the walk away on every keystroke would end the read-through every time it did its job.
/// So the sentences are cut again and the place is carried to the same words in the new text, the
/// text now being read comes out of the document as it stands, and
/// <see cref="TakeChangeNotice"/> hands the shell a plain sentence whenever the place moved or the
/// wording changed. Before this the sentence text was copied once at
/// <c>For(document)</c>: edit a paragraph mid-walk and the tool read the pre-fix wording back to a
/// committee member while the canvas banded the wrong characters, with no exception and no clue.</para>
/// </summary>
public sealed class ReadAloudSession : IDisposable
{
    private readonly DocumentSession? _live;
    private IReadOnlyList<Sentence> _sentences;
    private int _at = -1;
    private string? _notice;

    public ReadAloudSession(IReadOnlyList<Sentence> sentences) =>
        _sentences = sentences ?? throw new ArgumentNullException(nameof(sentences));

    private ReadAloudSession(DocumentSession live)
    {
        _live = live ?? throw new ArgumentNullException(nameof(live));
        _sentences = Sentences.In(live.Document);
        _live.Changed += OnDocumentChanged;
    }

    /// <summary>
    /// Raised when the newsletter changed underneath the walk and the walk has caught up. The shell
    /// draws itself again and reads <see cref="TakeChangeNotice"/>.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Builds one over a whole newsletter, frozen as it stands. Used by tests and tools.</summary>
    public static ReadAloudSession For(Document document) => new(Sentences.In(document));

    /// <summary>
    /// Builds one that keeps up with the newsletter as it is edited. This is what the app uses.
    /// </summary>
    public static ReadAloudSession For(DocumentSession session) => new(session);

    /// <summary>How many sentences there are to go through.</summary>
    public int Count => _sentences.Count;

    /// <summary>Which one we are on, counting from 1, or 0 before the first.</summary>
    public int Position => _at + 1;

    /// <summary>The sentence being read, or null before the start and after the end.</summary>
    public Sentence? Current => _at >= 0 && _at < _sentences.Count ? _sentences[_at] : null;

    /// <summary>True once the last sentence has been passed.</summary>
    public bool Finished => _at >= _sentences.Count;

    /// <summary>Nothing to read — an empty newsletter, or one with only pictures in it.</summary>
    public bool IsEmpty => _sentences.Count == 0;

    /// <summary>
    /// Where we are, for the screen and for the screen reader: "Sentence 4 of 112".
    /// </summary>
    public string ProgressText => IsEmpty
        ? "There is nothing to read yet."
        : Finished
            ? $"That is all {_sentences.Count} of them."
            : $"Sentence {Math.Max(1, Position)} of {_sentences.Count}";

    /// <summary>
    /// The plain-language sentence about the last edit — what changed under the reader and where
    /// they are now — or null when the edit did not move them or change the words they are on.
    /// Reading it clears it: it belongs to one edit, and saying it twice would be a second lie.
    /// </summary>
    public string? TakeChangeNotice()
    {
        string? notice = _notice;
        _notice = null;
        return notice;
    }

    /// <summary>Moves to the next sentence and hands it back, or null at the end.</summary>
    public Sentence? Next()
    {
        if (_at < _sentences.Count)
        {
            _at++;
        }

        return Current;
    }

    /// <summary>
    /// Back one. From the end this lands on the last sentence rather than one past it, because
    /// "back" pressed at the end means "say that again"; from the start it stays put.
    ///
    /// <para>Plain subtraction is enough for both. An earlier version special-cased the finished
    /// state, and writing the break-it-first test showed the special case was dead code —
    /// <see cref="Next"/> stops at <c>Count</c>, so stepping back from there already lands on the
    /// last sentence. The clamp at zero is the only guard that does anything.</para>
    /// </summary>
    public Sentence? Back()
    {
        _at = Math.Max(0, _at - 1);
        return Current;
    }

    /// <summary>Start again from the top.</summary>
    public void Restart() => _at = -1;

    /// <summary>Stops following the newsletter. The shell calls this when its window closes.</summary>
    public void Dispose()
    {
        if (_live is not null)
        {
            _live.Changed -= OnDocumentChanged;
        }
    }

    /// <summary>
    /// The newsletter was edited while the walk was open. Cut it into sentences again, carry the
    /// place across, and work out what to tell the reader.
    /// </summary>
    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        Sentence? was = Current;
        int wasPosition = Position;
        _sentences = Sentences.In(_live!.Document);

        if (was is null)
        {
            // Not started, or already past the end: there is no place to keep. Only the count can
            // have changed, and the progress line says that without needing a sentence about it.
            _at = Math.Min(_at, _sentences.Count);
            _notice = null;
            Raise();
            return;
        }

        (int landed, bool anchored) = WhereThatSentenceIsNow(was);
        _at = landed;

        // M74 (e): an unanchored landing is a guess, and it must not be narrated as continuity.
        _notice = anchored ? WhatToSay(was, wasPosition) : TheWritingIsGone();
        Raise();
    }

    /// <summary>
    /// What is said when the piece of writing the reader was in has gone out of the newsletter
    /// altogether — a whole story deleted, or the frame holding it removed. There is no honest
    /// "that sentence as it reads now" to offer, because the sentence now under the reader belongs
    /// to somebody else's story; all that can be said truthfully is that their place is gone and
    /// where they have been put instead.
    /// </summary>
    private string TheWritingIsGone() =>
        "The writing you were on is not in the newsletter any more. TrestleBoard has moved you to "
        + $"sentence {Math.Max(1, Position)} of {_sentences.Count}.";

    /// <summary>
    /// The place, carried across a cut that may have moved everything.
    ///
    /// <para><b>The same words beat the same offset</b>, and that ordering is the whole of the
    /// design. Type a new sentence in front of the one being read and the old offset now belongs to
    /// the new writing — following the offset would silently hand the reader somebody else's
    /// sentence, which is the defect this is here to close rather than a smaller version of it.
    /// After that: the same offset (the words themselves were edited), then the nearest sentence
    /// after it in the same paragraph, then the end of that paragraph, then the start of the same
    /// story. A reader would far rather be a line out than back at the beginning.</para>
    ///
    /// <para><b>M74 (e): and it says whether it found the place or guessed at it.</b> All five
    /// anchors are inside the story being read, so deleting that story outright misses every one of
    /// them and the last clamp lands on an arbitrary sentence of somebody else's article. That is a
    /// reasonable place to stand and an indefensible thing to narrate — the caller was telling the
    /// reader "this is that sentence as it reads now" about writing that had been deleted. The
    /// second half of the answer is what stops it.</para>
    /// </summary>
    /// <returns>
    /// Where to stand, and whether that place is really the one the reader was on
    /// (<c>Anchored</c>) or the fallback clamp with no relation to it.
    /// </returns>
    private (int At, bool Anchored) WhereThatSentenceIsNow(Sentence was)
    {
        if (_sentences.Count == 0)
        {
            return (0, true);
        }

        int sameWords = -1;
        int sameOffset = -1;
        int firstAfter = -1;
        int lastInParagraph = -1;
        int firstInStory = -1;

        for (int i = 0; i < _sentences.Count; i++)
        {
            Sentence at = _sentences[i];
            if (!string.Equals(at.StoryId, was.StoryId, StringComparison.Ordinal))
            {
                continue;
            }

            if (firstInStory < 0)
            {
                firstInStory = i;
            }

            if (at.ParagraphIndex != was.ParagraphIndex)
            {
                continue;
            }

            lastInParagraph = i;

            if (string.Equals(at.Text, was.Text, StringComparison.Ordinal)
                && (sameWords < 0
                    || Math.Abs(at.Offset - was.Offset)
                        < Math.Abs(_sentences[sameWords].Offset - was.Offset)))
            {
                sameWords = i;
            }

            if (at.Offset == was.Offset)
            {
                sameOffset = i;
            }
            else if (firstAfter < 0 && at.Offset > was.Offset)
            {
                firstAfter = i;
            }
        }

        foreach (int candidate in new[] { sameWords, sameOffset, firstAfter, lastInParagraph, firstInStory })
        {
            if (candidate >= 0)
            {
                return (candidate, true);
            }
        }

        return (Math.Clamp(_at, 0, _sentences.Count - 1), false);
    }

    /// <summary>
    /// What the reader is told, and only what actually happened to them: the words under them
    /// changed, or their place moved, or both, or — the common case, an edit somewhere else in the
    /// newsletter — nothing worth interrupting a read-through for.
    /// </summary>
    private string? WhatToSay(Sentence was, int wasPosition)
    {
        if (_sentences.Count == 0)
        {
            return "There is no writing left in the newsletter, so there is nothing more to read.";
        }

        Sentence? now = Current;
        if (now is null)
        {
            return "The writing you were on is not in the newsletter any more. "
                + $"{ProgressText}.";
        }

        bool wordsChanged = !string.Equals(now.Text, was.Text, StringComparison.Ordinal);
        bool placeMoved = Position != wasPosition;

        if (!wordsChanged && !placeMoved)
        {
            return null;
        }

        string words = wordsChanged
            ? "The newsletter changed while you were reading, so this is that sentence as it reads "
              + "now."
            : "The newsletter changed while you were reading.";

        return placeMoved
            ? $"{words} You are on sentence {Math.Max(1, Position)} of {_sentences.Count} now."
            : words;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
