using System;
using System.Collections.Generic;
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
/// </summary>
public sealed class ReadAloudSession
{
    private readonly IReadOnlyList<Sentence> _sentences;
    private int _at = -1;

    public ReadAloudSession(IReadOnlyList<Sentence> sentences) =>
        _sentences = sentences ?? throw new ArgumentNullException(nameof(sentences));

    /// <summary>Builds one over a whole newsletter.</summary>
    public static ReadAloudSession For(Document document) => new(Sentences.In(document));

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
}
