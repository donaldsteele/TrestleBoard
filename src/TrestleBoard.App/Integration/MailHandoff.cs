using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TrestleBoard.App.Integration;

/// <summary>What the app could manage when asked to hand the newsletter to a mail program (M56).</summary>
internal enum MailOutcome
{
    /// <summary>The mail program opened with the addresses and subject already in it.</summary>
    Opened,

    /// <summary>Too many addresses for one link. They are on the clipboard instead, in batches.</summary>
    TooManyForOneLink,

    /// <summary>Nothing answered. The addresses are on the clipboard.</summary>
    NothingAnswered,
}

/// <summary>
/// "Now send it" (PLAN.md §11 M56): the finished PDF, the email group, and the user's own mail
/// program.
///
/// <para><b>No SMTP, no accounts, no network code of our own.</b> This app has no business holding
/// a committee member's email password, and a lodge that changes its mail provider must not have to
/// change its newsletter program. What an offline app can honestly do is fill in the message and
/// let the program they already use send it.</para>
///
/// <para><b>BCC is not a default here, it is the design.</b> Sixty brothers' addresses in a To:
/// line is a disclosure to sixty people who did not agree to it, and it is the kind of mistake that
/// cannot be taken back — PLAN.md §0 rule 7 says so in as many words. There is no code path in this
/// class that puts a member's address anywhere but BCC.</para>
/// </summary>
internal static class MailHandoff
{
    /// <summary>
    /// A conservative ceiling for the whole <c>mailto:</c> URI.
    ///
    /// <para>There is no standard limit. Windows' shell has historically refused somewhere above
    /// 2 KB, and various mail clients truncate silently — which is the failure that matters, since
    /// a truncated BCC list sends the newsletter to some of the lodge and tells nobody. Well under
    /// any known limit is the right side to be wrong on: the fallback is a clipboard paste, which
    /// works every time.</para>
    /// </summary>
    internal const int LongestUri = 1800;

    /// <summary>How many addresses go in one clipboard batch when the link is too long.</summary>
    internal const int BatchSize = 25;

    /// <summary>
    /// The subject line, built from the issue: "Indian Land Lodge 414 Trestle Board — September 2026".
    /// </summary>
    internal static string Subject(string lodgeName, string title, int year, int month)
    {
        string when = MonthName(month) + " " + year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string what = string.IsNullOrWhiteSpace(lodgeName)
            ? (string.IsNullOrWhiteSpace(title) ? "Trestle Board" : title.Trim())
            : $"{lodgeName.Trim()} {(string.IsNullOrWhiteSpace(title) ? "Trestle Board" : title.Trim())}";
        return $"{what} — {when}";
    }

    /// <summary>
    /// The body: what the person receiving it needs, and what the sender must not forget.
    ///
    /// <para><c>mailto:</c> cannot attach a file — no mail program accepts one, for the obvious
    /// reason that a link on a web page should not be able to post your documents. So the message
    /// says which file to attach, by name, because the alternative is sixty people receiving an
    /// email about a newsletter that is not there.</para>
    /// </summary>
    internal static string Body(string pdfFileName) =>
        "Brethren,\n\n"
        + "This month's trestle board is attached.\n\n"
        + "Fraternally,\n"
        + "The Trestle Board Committee\n\n"
        + "-- \n"
        + $"Before you send this: attach the file named {pdfFileName}.\n";

    /// <summary>
    /// The <c>mailto:</c> link, with every address in BCC. Returns null when it would be longer
    /// than <see cref="LongestUri"/> — the caller then falls back to the clipboard rather than
    /// handing a mail program a link it may silently truncate.
    /// </summary>
    internal static string? BuildUri(IReadOnlyList<string> addresses, string subject, string body)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        var uri = new StringBuilder("mailto:?");
        uri.Append("subject=").Append(Uri.EscapeDataString(subject ?? string.Empty));
        uri.Append("&body=").Append(Uri.EscapeDataString(body ?? string.Empty));

        if (addresses.Count > 0)
        {
            uri.Append("&bcc=").Append(Uri.EscapeDataString(string.Join(",", addresses)));
        }

        return uri.Length > LongestUri ? null : uri.ToString();
    }

    /// <summary>
    /// The addresses in batches a person can paste one at a time, each on its own line with a
    /// heading saying which batch it is. Sixty addresses in one unbroken run is a wall nobody can
    /// check; twenty-five is a screenful.
    /// </summary>
    internal static string ClipboardBatches(IReadOnlyList<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        if (addresses.Count == 0)
        {
            return string.Empty;
        }

        if (addresses.Count <= BatchSize)
        {
            return string.Join(", ", addresses);
        }

        var text = new StringBuilder();
        int batches = (addresses.Count + BatchSize - 1) / BatchSize;
        for (int i = 0; i < batches; i++)
        {
            IEnumerable<string> batch = addresses.Skip(i * BatchSize).Take(BatchSize);
            text.Append("Batch ").Append(i + 1).Append(" of ").Append(batches).Append(":\n");
            text.Append(string.Join(", ", batch)).Append("\n\n");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// The email addresses of everybody in a group who should still be receiving the newsletter,
    /// each once, in list order. A member with no address is skipped rather than counted.
    /// </summary>
    internal static IReadOnlyList<string> AddressesIn(
        IEnumerable<Roster.Member> members,
        string group)
    {
        ArgumentNullException.ThrowIfNull(members);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addresses = new List<string>();
        foreach (Roster.Member member in Roster.MemberGroups.Members([.. members], group))
        {
            string address = (member.Email ?? string.Empty).Trim();
            if (address.Length > 0 && seen.Add(address))
            {
                addresses.Add(address);
            }
        }

        return addresses;
    }

    private static string MonthName(int month) =>
        month is >= 1 and <= 12
            ? System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)
            : string.Empty;
}
