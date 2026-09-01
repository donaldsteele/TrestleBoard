using System.Text.RegularExpressions;

namespace TrestleBoard.Core.Text;

/// <summary>What kind of thing was found, which decides the scheme the reader's device is handed.</summary>
public enum LinkKind
{
    /// <summary>An email address. Opens the reader's mail program.</summary>
    Email,

    /// <summary>A web address. Opens the reader's browser.</summary>
    Web,

    /// <summary>A telephone number. Dials, on a phone.</summary>
    Telephone,
}

/// <summary>
/// One stretch of a paragraph that a reader's device can act on.
/// </summary>
/// <param name="ParagraphIndex">Which paragraph of the story it is in.</param>
/// <param name="Start">Where it begins, as a character offset into that paragraph.</param>
/// <param name="End">One past the last character.</param>
/// <param name="Kind">What sort of thing it is.</param>
/// <param name="Uri">The address to hand the reader's device.</param>
public readonly record struct DetectedLink(
    int ParagraphIndex,
    int Start,
    int End,
    LinkKind Kind,
    string Uri);

/// <summary>
/// Finds the email addresses, web addresses and telephone numbers already written in the newsletter
/// (PLAN.md §11 M78).
///
/// <para><b>There is no user interface at all, and that is the design.</b> Half the lodge reads the
/// PDF on a phone. A telephone number that dials and an email address that opens the mail app are
/// real value to that reader and cost the committee nothing to learn — there is no markup, no link
/// dialog and no new command. The newsletter is written exactly as it always was.</para>
///
/// <para><b>Nothing rendered changes.</b> No underline, no blue, not a pixel. The reader's PDF app
/// owns the affordance; blue underlines on paper are the one modern habit this audience finds
/// ugly, and a lodge newsletter is still printed and handed out. What M78 adds is an annotation
/// rectangle over glyphs that were already there.</para>
///
/// <para><b>The telephone rule is deliberately narrow.</b> A pattern loose enough to catch every
/// way a number can be written also catches "Lodge No. 414", a date, a year and a room number —
/// and a false link is worse than a missing one, because it is a promise the newsletter makes to a
/// reader who then dials a lodge number that is not a telephone. So this requires the shape
/// American lodges actually print: ten digits in three groups, optionally with a country code, and
/// separated by spaces, dots or hyphens, or wrapped in the familiar parentheses. Seven-digit local
/// numbers are NOT matched: they are indistinguishable from a year followed by a number, and the
/// examples in this project's own archive all print the area code.</para>
/// </summary>
public static partial class LinkDetector
{
    /// <summary>
    /// An email address. Deliberately simpler than the specification allows: the aim is the
    /// addresses a lodge secretary types, not everything a mail server would accept.
    /// </summary>
    [GeneratedRegex(
        @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern { get; }

    /// <summary>
    /// A web address, with or without the scheme. <c>www.</c> is required when the scheme is absent,
    /// because otherwise every sentence containing "lodge414.org" competes with every abbreviation
    /// that happens to end in a two-letter word.
    /// </summary>
    [GeneratedRegex(
        @"\b(?:https?://[^\s<>""]+|www\.[A-Za-z0-9\-]+\.[A-Za-z]{2,}(?:/[^\s<>""]*)?)",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)]
    private static partial Regex WebPattern { get; }

    /// <summary>
    /// Ten digits in the shape a lodge prints them. See the class remarks for why this is narrow
    /// rather than generous.
    ///
    /// <para>The trailing guard is <c>(?!\d|[-.]\d)</c> rather than <c>(?![\d\-.])</c>, and the
    /// difference is a real defect the tests caught: the looser form refused
    /// <c>(803) 555-0100.</c> at the end of a sentence, because the full stop that ends the
    /// sentence looked like the start of more digits. What has to be rejected is a digit, or a
    /// separator that is itself followed by one — not punctuation that ends a sentence.</para>
    /// </summary>
    [GeneratedRegex(
        @"(?<![\d\-.])(?:\+?1[ \-.])?(?:\(\d{3}\)[ ]?|\d{3}[ \-.])\d{3}[ \-.]\d{4}(?!\d|[-.]\d)",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)]
    private static partial Regex TelephonePattern { get; }

    /// <summary>
    /// Every link in one paragraph, in the order they appear and never overlapping.
    ///
    /// <para>Email is matched first and the stretches it claims are refused to the other two. An
    /// address like <c>secretary@lodge414.org</c> contains something a loose web pattern would
    /// happily call a domain, and two annotations over the same glyphs is a reader tapping one
    /// thing and getting the other.</para>
    /// </summary>
    public static IReadOnlyList<DetectedLink> FindInParagraph(string? text, int paragraphIndex)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var found = new List<DetectedLink>();
        var claimed = new List<(int Start, int End)>();

        Collect(EmailPattern, LinkKind.Email);
        Collect(WebPattern, LinkKind.Web);
        Collect(TelephonePattern, LinkKind.Telephone);

        found.Sort((a, b) => a.Start.CompareTo(b.Start));
        return found;

        void Collect(Regex pattern, LinkKind kind)
        {
            foreach (Match match in pattern.Matches(text!))
            {
                int start = match.Index;
                int end = match.Index + match.Length;

                // Trailing punctuation belongs to the sentence, not to the address. "…see
                // www.lodge414.org." must not hand the browser a full stop.
                while (end > start && IsSentencePunctuation(text![end - 1]))
                {
                    end--;
                }

                if (end <= start || claimed.Any(c => start < c.End && end > c.Start))
                {
                    continue;
                }

                claimed.Add((start, end));
                found.Add(new DetectedLink(
                    paragraphIndex, start, end, kind, ToUri(kind, text!.AsSpan(start, end - start).ToString())));
            }
        }
    }

    /// <summary>Every link in a whole story's paragraphs, in reading order.</summary>
    public static IReadOnlyList<DetectedLink> FindInParagraphs(IReadOnlyList<string> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);

        var all = new List<DetectedLink>();
        for (int i = 0; i < paragraphs.Count; i++)
        {
            all.AddRange(FindInParagraph(paragraphs[i], i));
        }

        return all;
    }

    private static bool IsSentencePunctuation(char c) =>
        c is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '"' or '\'' or '”' or '’';

    /// <summary>
    /// The address the reader's device is handed. The three schemes are the three every phone and
    /// every desktop PDF reader already knows what to do with.
    /// </summary>
    private static string ToUri(LinkKind kind, string text) => kind switch
    {
        LinkKind.Email => "mailto:" + text,
        LinkKind.Telephone => "tel:+" + Digits(text, assumeNorthAmerica: true),
        _ => text.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? text : "https://" + text,
    };

    /// <summary>
    /// A telephone number reduced to digits, with the country code put back if it was left out.
    /// A <c>tel:</c> without one dials nothing on a phone roaming abroad, which is exactly the
    /// reader most likely to be tapping instead of typing.
    /// </summary>
    private static string Digits(string text, bool assumeNorthAmerica)
    {
        string digits = string.Concat(text.Where(char.IsAsciiDigit));
        return assumeNorthAmerica && digits.Length == 10 ? "1" + digits : digits;
    }
}
