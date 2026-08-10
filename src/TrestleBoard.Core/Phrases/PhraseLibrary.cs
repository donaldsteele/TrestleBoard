using System;
using System.Collections.Generic;
using System.Linq;

namespace TrestleBoard.Core.Phrases;

/// <summary>
/// One blank in a phrase — a question, asked in the wizard voice (PLAN.md §11 M54).
/// </summary>
/// <param name="Token">What to replace in the text, e.g. <c>{name}</c>.</param>
/// <param name="Question">What the user is asked, one to a screen.</param>
/// <param name="Hint">An example of the shape of answer wanted. Never a real person.</param>
/// <param name="Default">
/// An answer this blank already has, or null if it is a question somebody must be asked.
///
/// <para>A blank with a default is <b>not asked</b> — it arrives filled in, and the wizard shows it
/// on the read-back screen as something the user may change for this one insert. That is the whole
/// point: M54's design is that blanks are asked one at a time at an emotionally hard moment, and a
/// question everybody would answer the same way every time makes the feature worse. The office a
/// lodge sends sickness enquiries to is such a question — it varies by lodge, but not by month.</para>
///
/// <para>It is also the last line against a broken sentence. Whoever supplies the answer — a
/// setting, a wizard box — may supply nothing, and "please speak to the , who is keeping in touch"
/// is not a sentence to print in a newsletter. <see cref="Phrase.Fill"/> falls back here first and
/// only then to underscores.</para>
/// </param>
public sealed record PhraseBlank(string Token, string Question, string Hint, string? Default = null);

/// <summary>
/// A ready-made paragraph for a moment that is hard to write (PLAN.md §11 M54).
/// </summary>
/// <param name="Id">Stable id, so a saved shelf survives a reword.</param>
/// <param name="Title">What it is called in the list.</param>
/// <param name="WhenToUse">One line saying when a person would reach for it.</param>
/// <param name="Text">The paragraph, with <see cref="Blanks"/>' tokens still in it.</param>
/// <param name="Blanks">The questions to ask before inserting it.</param>
/// <param name="IsMine">True for a paragraph the user saved themselves.</param>
public sealed record Phrase(
    string Id,
    string Title,
    string WhenToUse,
    string Text,
    IReadOnlyList<PhraseBlank> Blanks,
    bool IsMine = false)
{
    /// <summary>
    /// The paragraph with the answers put in. An unanswered blank keeps a plain line of
    /// underscores rather than the token: the user is about to see this text in their newsletter,
    /// and <c>{name}</c> would read as a fault in the program rather than as something to fill in.
    ///
    /// <para>A blank carrying a <see cref="PhraseBlank.Default"/> falls back to that first, so a
    /// setting somebody emptied cannot leave a hole in the middle of a sentence.</para>
    /// </summary>
    public string Fill(IReadOnlyDictionary<string, string>? answers)
    {
        string filled = Text;
        foreach (PhraseBlank blank in Blanks)
        {
            string value =
                answers is not null
                && answers.TryGetValue(blank.Token, out string? given)
                && !string.IsNullOrWhiteSpace(given)
                    ? given.Trim()
                    : !string.IsNullOrWhiteSpace(blank.Default)
                        ? blank.Default.Trim()
                        : "__________";
            filled = filled.Replace(blank.Token, value, StringComparison.Ordinal);
        }

        return filled;
    }
}

/// <summary>
/// The shelf of hard-moment paragraphs (PLAN.md §11 M54).
///
/// <para>The committee re-drafts a memorial every time one is needed, or digs through old issues to
/// copy one. Blank-page paralysis is worst under grief, and a dignified starting text with two
/// blanks to fill is dramatically easier than an empty frame — which is the whole of this
/// milestone.</para>
///
/// <para><b>This is lodge voice, not app voice</b>, and PLAN.md §11 M54 requires the owner to
/// review the tone before it ships. Everything below is a starting point a committee is expected to
/// edit: the text arrives as ordinary editable words in their newsletter, not as a locked block.</para>
///
/// <para><b>§0 rule 2.</b> Every hint is a blank or a fictional placeholder. Not one real name,
/// date or lodge beyond this app's own appears here, and none ever may — the user's own saved
/// phrases will contain real names, and those live in AppData (§0 rule 7).</para>
/// </summary>
public static class PhraseLibrary
{
    private static readonly PhraseBlank Name =
        new("{name}", "What is the brother's name?", "For example: John Smith");

    private static readonly PhraseBlank Date =
        new("{date}", "What was the date?", "For example: 14 September 2026");

    /// <summary>
    /// Which officer a member should speak to about sickness and distress. The owner's ruling of
    /// 2026-08-09 (docs/M54-spec.md §3, choice 2): at Indian Land 414 it is the <b>Secretary</b>,
    /// and it must be settable, because the office varies by lodge and can change year to year.
    ///
    /// <para>It carries a <see cref="PhraseBlank.Default"/>, so it is pre-filled rather than asked
    /// — see that property for why. The app hands the user's setting in through
    /// <see cref="Phrase.Fill"/>, the same seam <c>{name}</c> and <c>{date}</c> come through;
    /// Core has no idea a settings file exists.</para>
    /// </summary>
    public const string DefaultOffice = "Secretary";

    private static readonly PhraseBlank Office =
        new(
            "{office}",
            "Who should members speak to about sickness and distress?",
            "The sentence reads \"speak to the …\", so put in Secretary, or Almoner, or "
            + "Chaplain — whichever officer your lodge asks people to go to.",
            DefaultOffice);

    /// <summary>The paragraphs that ship with the app.</summary>
    public static readonly IReadOnlyList<Phrase> Bundled =
    [
        new(
            "memorial",
            "A memorial notice",
            "When a brother has passed away.",
            // The craft register here is the owner's, given on 2026-08-09, replacing the drafted
            // "was called to the Celestial Lodge above". The date leads the sentence because the
            // longer phrasing pushes it too far from the verb otherwise. docs/M54-spec.md §3.
            "It is with sorrow that we record that on {date}, Brother {name} laid down his working "
            + "tools and joined that Celestial Lodge above, where the Supreme Architect presides. "
            + "He gave many years of faithful service to this lodge and to the craft, and his "
            + "place among us will not easily be filled. The brethren extend their heartfelt "
            + "sympathy to his family, and he will be remembered with affection whenever we meet.",
            [Name, Date]),

        new(
            "sickness-and-distress",
            "Sickness and distress",
            "When a brother is unwell and the lodge should know.",
            // The office is the owner's, given on 2026-08-09, replacing the drafted "the Almoner":
            // at Indian Land 414 sickness enquiries go to the Secretary. It is a blank rather than
            // a word in the sentence because the office varies by lodge and can change year to
            // year — and a pre-filled blank, not a question, because nobody should be asked it
            // twice. docs/M54-spec.md §3.
            "Brother {name} is unwell at present, and the lodge holds him in its thoughts. Cards "
            + "and visits would be welcome. If you would like to know how best to help, please "
            + "speak to the {office}, who is keeping in touch with the family.",
            [Name, Office]),

        new(
            "get-well",
            "A get-well message",
            "A short line wishing a brother a good recovery.",
            "The brethren send their warmest wishes to Brother {name} for a full and swift "
            + "recovery. We look forward to welcoming him back to lodge before long.",
            [Name]),

        new(
            "newly-raised",
            "Welcome to a newly raised brother",
            "When a brother has been raised to the Third Degree.",
            "The lodge is pleased to welcome Brother {name}, who was raised to the Sublime Degree "
            + "of Master Mason on {date}. We congratulate him on his progress through the degrees, "
            + "and we look forward to his company at our meetings for many years to come.",
            [Name, Date]),

        new(
            "thank-the-degree-team",
            "Thank you to a degree team",
            "After a degree has been conferred.",
            "The Worshipful Master extends the thanks of the lodge to the degree team who worked "
            + "on {date}. Their preparation and their care did credit to the craft, and the "
            + "evening will be remembered by all who were present.",
            [Date]),
    ];

    /// <summary>The bundled paragraph with this id, or null.</summary>
    public static Phrase? Find(string id) =>
        Bundled.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Turns a paragraph somebody wrote themselves into a shelf entry. Their own text has no
    /// blanks in it — asking a user to invent a token syntax would be asking them to program.
    /// </summary>
    public static Phrase Mine(string id, string title, string text) =>
        new(id, title, "One of your own.", text, [], IsMine: true);
}
