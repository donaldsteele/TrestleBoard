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

        // The longest matching hint wins (M88), and the order of RosterFieldInfo.All breaks ties.
        //
        // Until M88 this was first-field-wins, which was right while there were ten fields and no
        // two of them could plausibly claim the same header. It stopped being right the moment the
        // lodge's own export arrived: "Mobile Phone" matched the telephone's hint "mobile" before
        // the mobile field was ever reached, "Address Undeliverable" matched "address", and — a
        // misfire that predates M88 entirely — "Highest Degree Date" matched "degree" and became
        // the degree KIND. Scoring by how much of the header the hint actually accounts for settles
        // all three without reordering the list into something nobody can read.
        List<(int Score, int Order, RosterField Field, int Column)> candidates = [];
        for (int order = 0; order < RosterFieldInfo.All.Count; order++)
        {
            RosterFieldInfo field = RosterFieldInfo.All[order];
            foreach (TableColumn column in columns)
            {
                int score = BestHintLength(column.Header, field);
                if (score > 0)
                {
                    candidates.Add((score, order, field.Field, column.Index));
                }
            }
        }

        foreach ((int _, int _, RosterField field, int column) in candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Order))
        {
            if (!mapping.ContainsKey(field) && taken.Add(column))
            {
                mapping[field] = column;
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
    private static bool Matches(string header, RosterFieldInfo field) =>
        BestHintLength(header, field) > 0;

    /// <summary>
    /// How much of this header the field's best hint accounts for, or 0 for no match (M88). This is
    /// the score <see cref="GuessMapping"/> sorts on: a header two fields both recognise belongs to
    /// the one that recognises more of it.
    /// </summary>
    private static int BestHintLength(string header, RosterFieldInfo field)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return 0;
        }

        string value = header.Trim().ToLowerInvariant();
        int best = 0;
        foreach (string hint in field.HeaderHints)
        {
            if (hint.Length > best && ContainsWord(value, hint))
            {
                best = hint.Length;
            }
        }

        return best;
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
