using TrestleBoard.Roster.Tables;

namespace TrestleBoard.Roster.Import;

/// <summary>One column of the user's file, described the way the mapping screen shows it.</summary>
/// <param name="Index">Zero-based position in the sheet.</param>
/// <param name="Letter">"A", "B" … as the spreadsheet names it.</param>
/// <param name="Header">What the header row calls it, or an empty string.</param>
/// <param name="Sample">The first couple of values, so the user recognises the data.</param>
public sealed record TableColumn(int Index, string Letter, string Header, IReadOnlyList<string> Sample)
{
    /// <summary>"B — Birthday (7/4, 12/25)". One line, and enough to choose by without decoding a header.</summary>
    public string Describe()
    {
        string head = string.IsNullOrWhiteSpace(Header) ? "no title" : Header;
        return Sample.Count == 0
            ? $"{Letter} — {head}"
            : $"{Letter} — {head} ({string.Join(", ", Sample)})";
    }
}

/// <summary>
/// The guesses the import flow starts from (PLAN.md §11 M12, screens 3 and 4). Everything here is a
/// suggestion the user can overrule — the screens say "We guessed these. Change any that are wrong."
/// </summary>
public static class ColumnGuesser
{
    /// <summary>How many rows the "which row has the column titles?" screen shows.</summary>
    public const int HeaderCandidateRows = 8;

    /// <summary>
    /// Which row holds the column titles, or -1 for "there are no column titles". Scored on how
    /// text-like a row is and how well it matches the headers we know about — a title row is
    /// usually words where the rows below it are dates and numbers.
    /// </summary>
    public static int GuessHeaderRow(TableSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        int best = -1;
        int bestScore = 0;
        for (int row = 0; row < Math.Min(HeaderCandidateRows, sheet.RowCount); row++)
        {
            int score = 0;
            for (int column = 0; column < sheet.ColumnCount; column++)
            {
                string value = sheet.Cell(row, column);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                score++;
                if (RosterFieldInfo.All.Any(f => Matches(value, f)))
                {
                    score += 3;
                }

                if (FieldValues.TryReadBirthday(value, out _, out _))
                {
                    // A row of dates is data, not titles.
                    score -= 2;
                }
            }

            if (score > bestScore)
            {
                best = row;
                bestScore = score;
            }
        }

        // A header row has to look like titles rather than merely be first. A file with no titles at
        // all is common enough — a pasted column of names — and guessing one loses the first person.
        return bestScore >= 4 ? best : -1;
    }

    /// <summary>The columns as the mapping screen lists them.</summary>
    public static IReadOnlyList<TableColumn> Columns(TableSheet sheet, int headerRow)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var columns = new List<TableColumn>();
        for (int index = 0; index < sheet.ColumnCount; index++)
        {
            columns.Add(new TableColumn(
                index,
                TableWorkbook.ColumnLetter(index),
                headerRow >= 0 ? sheet.Cell(headerRow, index) : string.Empty,
                sheet.Sample(index, headerRow + 1, 2)));
        }

        return columns;
    }

    /// <summary>
    /// Which column each lodge field probably lives in. Header text first, and — when there are no
    /// headers at all — the shape of the data, because a column of "7/4" values is a birthday
    /// column whatever it is called.
    /// </summary>
    public static Dictionary<RosterField, int> GuessMapping(TableSheet sheet, int headerRow)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        IReadOnlyList<TableColumn> columns = Columns(sheet, headerRow);
        var mapping = new Dictionary<RosterField, int>();
        var taken = new HashSet<int>();

        foreach (RosterFieldInfo field in RosterFieldInfo.All)
        {
            foreach (TableColumn column in columns)
            {
                if (taken.Contains(column.Index) || !Matches(column.Header, field))
                {
                    continue;
                }

                mapping[field.Field] = column.Index;
                taken.Add(column.Index);
                break;
            }
        }

        foreach (TableColumn column in columns)
        {
            if (taken.Contains(column.Index) || column.Sample.Count == 0)
            {
                continue;
            }

            RosterField? guessed = GuessFromValues(column.Sample);
            if (guessed is { } field && !mapping.ContainsKey(field))
            {
                mapping[field] = column.Index;
                taken.Add(column.Index);
            }
        }

        return mapping;
    }

    /// <summary>
    /// Does this header name this field? Substring, so "Member Name" is a name column — but the
    /// substring has to sit on WORD boundaries.
    ///
    /// <para>A bare <c>Contains</c> matched inside other words, and the misfires were all in the
    /// same direction: "Member Number" scored as Name (hint "member"), "Mailing address" as Email
    /// (hint "mail"), "Member No." as Phone (hint "number"). The mapping screen presents its guesses
    /// as "We guessed these. Change any that are wrong.", so a wrong guess is correctable — but only
    /// by somebody who notices, and the whole point of guessing is that they should not have to
    /// check every row (review §14.2).</para>
    ///
    /// <para>Word boundary here means "not flanked by another letter or digit", which lets
    /// "Member Name", "e-mail" and "Phone#" all still match while "Member Number" no longer claims
    /// to be a name.</para>
    /// </summary>
    private static bool Matches(string header, RosterFieldInfo field)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        string value = header.Trim().ToLowerInvariant();
        return field.HeaderHints.Any(hint => ContainsWord(value, hint));
    }

    private static bool ContainsWord(string haystack, string needle)
    {
        if (needle.Length == 0)
        {
            return false;
        }

        int at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            bool startsClean = at == 0 || !char.IsLetterOrDigit(haystack[at - 1]);
            int after = at + needle.Length;
            bool endsClean = after >= haystack.Length || !char.IsLetterOrDigit(haystack[after]);
            if (startsClean && endsClean)
            {
                return true;
            }

            at = after;
        }

        return false;
    }

    private static RosterField? GuessFromValues(IReadOnlyList<string> sample)
    {
        if (sample.All(v => v.Contains('@', StringComparison.Ordinal)))
        {
            return RosterField.Email;
        }

        if (sample.All(v => FieldValues.TryReadBirthday(v, out _, out _)))
        {
            return RosterField.Birthday;
        }

        if (sample.All(LooksLikeAPhoneNumber))
        {
            return RosterField.Phone;
        }

        // A column of ordinary words could be anything; a wrong guess here is worse than none,
        // because the review screen would show the user a plausible-looking mistake.
        return sample.All(v => v.Split(' ').Length >= 2 && v.Any(char.IsLetter))
            ? RosterField.Name
            : null;
    }

    private static bool LooksLikeAPhoneNumber(string value)
    {
        int digits = value.Count(char.IsDigit);
        return digits >= 7 && value.All(c => char.IsDigit(c) || c is '-' or '(' or ')' or ' ' or '+' or '.');
    }
}
