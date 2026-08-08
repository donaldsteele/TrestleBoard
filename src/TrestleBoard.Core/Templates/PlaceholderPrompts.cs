using System;
using System.Collections.Generic;
using System.Linq;

namespace TrestleBoard.Core.Templates;

/// <summary>
/// Every sentence a template or a carry-forward puts in a story to say "your words go here"
/// (PLAN.md §11 M51).
///
/// <para>
/// These strings existed before M51, but each template owned its own copy and each copy was
/// <c>internal</c>. Only one of the seven — <see cref="Article"/>, which is
/// <c>CarryForward.DefaultArticlePrompt</c> — was matched by anything, so
/// <c>ActionContextFactory.HoldsAPrompt</c> answered "no prompts left" for a newsletter whose cover
/// still said "Write the Worshipful Master's message here…". A checklist that walks the newsletter
/// looking for unreplaced prompts cannot be built on a set that has six holes in it, so the set
/// moved here and became one thing.
/// </para>
///
/// <para>
/// <b>Adding a prompt anywhere else is a bug.</b> A template that invents its own sentence is a
/// prompt the review checklist will never find, and the user will discover it in the PDF.
/// <c>PlaceholderPromptTests</c> fails if a template string is not one of these.
/// </para>
/// </summary>
public static class PlaceholderPrompts
{
    /// <summary>All three templates open with the Master's message.</summary>
    public const string CoverEssay = "Write the Worshipful Master's message here…";

    public const string News = "Write this month's news here…";

    public const string MoreNews = "Write more news here…";

    public const string AboutThePhoto = "Write about this photo here…";

    public const string ClosingNote = "Write a closing note here…";

    /// <summary>
    /// The odd one out: this is a prompt in <c>ImageFrame.AltText</c>, not in a story. It matters to
    /// the checklist because "has a description" cannot be
    /// <c>!string.IsNullOrWhiteSpace(AltText)</c> — the six-page template ships this text in the
    /// field, so a picture nobody has described looks described.
    /// </summary>
    public const string PhotoDescription = "Write a description of this photo here…";

    /// <summary>What carry-forward resets every article to, so last month's words cannot go out
    /// again under this month's date.</summary>
    public const string Article = "Write this month's article here…";

    /// <summary>The prompts that appear in prose, in no particular order.</summary>
    public static readonly IReadOnlyList<string> InProse =
    [
        CoverEssay,
        News,
        MoreNews,
        AboutThePhoto,
        ClosingNote,
        Article,
    ];

    /// <summary>Every prompt, prose and picture description alike.</summary>
    public static readonly IReadOnlyList<string> All = [.. InProse, PhotoDescription];

    /// <summary>
    /// True when the text is nothing but a prompt — the frame is untouched. Whitespace either side
    /// is ignored, because a stray newline is not somebody's writing.
    /// </summary>
    public static bool IsUntouched(string? text) =>
        text is not null && All.Any(p => string.Equals(text.Trim(), p, StringComparison.Ordinal));

    /// <summary>
    /// True when a prompt is still somewhere in the text. Weaker than <see cref="IsUntouched"/> and
    /// deliberately so: somebody who typed a paragraph above the prompt and left it below has still
    /// left it, and it will still print.
    /// </summary>
    public static bool LurksIn(string? text) =>
        text is not null && All.Any(p => text.Contains(p, StringComparison.Ordinal));

    /// <summary>The prompt found in the text, for a message that can quote it back.</summary>
    public static string? FirstIn(string? text) =>
        text is null ? null : All.FirstOrDefault(p => text.Contains(p, StringComparison.Ordinal));
}
