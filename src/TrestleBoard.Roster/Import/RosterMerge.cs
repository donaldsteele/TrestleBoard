using TrestleBoard.Roster.Tables;

namespace TrestleBoard.Roster.Import;

/// <summary>What is to be done with one row of the user's file.</summary>
public enum RowOutcome
{
    /// <summary>Nobody in the book matches: this person will be added.</summary>
    New,

    /// <summary>Matched somebody, and at least one mapped field differs.</summary>
    Updated,

    /// <summary>Matched somebody and changes nothing. The row an idempotent re-import is made of.</summary>
    Unchanged,

    /// <summary>Matched somebody, but the user asked for existing people to be left alone.</summary>
    LeftAlone,

    /// <summary>Could not be used — no name in it. Never silently dropped; see <see cref="MergePlan.Unusable"/>.</summary>
    Unusable,

    /// <summary>Close to somebody already in the book. A question, never an action.</summary>
    Question,
}

/// <summary>One row of the file and what the import will do with it.</summary>
public sealed record PlannedRow(
    int RowNumber,
    RowOutcome Outcome,
    Member Result,
    string? MatchedMemberId,
    string? Note);

/// <summary>
/// A row close enough to an existing person to be worth asking about — never merged on its own
/// (PLAN.md §11 M12, merge policy).
/// </summary>
public sealed record DuplicateQuestion(int RowNumber, string IncomingName, string ExistingMemberId, string ExistingName)
{
    public string Question => $"Are these the same person? \"{IncomingName}\" and \"{ExistingName}\".";
}

/// <summary>
/// The whole of what an import would do, before anything is written (PLAN.md §11 M12, screen 5).
/// The review screen is this object read aloud in plain counts.
/// </summary>
public sealed record MergePlan(
    RosterBook Result,
    IReadOnlyList<PlannedRow> Rows,
    IReadOnlyList<DuplicateQuestion> Questions)
{
    public int NewCount => Rows.Count(r => r.Outcome == RowOutcome.New);

    public int UpdatedCount => Rows.Count(r => r.Outcome == RowOutcome.Updated);

    public int UnchangedCount => Rows.Count(r => r.Outcome == RowOutcome.Unchanged);

    public int LeftAloneCount => Rows.Count(r => r.Outcome == RowOutcome.LeftAlone);

    public IReadOnlyList<PlannedRow> Unusable => Rows.Where(r => r.Outcome == RowOutcome.Unusable).ToList();

    /// <summary>Would committing this change anything at all? The idempotence property, as a question.</summary>
    public bool ChangesAnything => NewCount > 0 || UpdatedCount > 0;

    /// <summary>
    /// The counts as the review screen says them: plain sentences, no jargon, and the awkward ones
    /// said out loud rather than hidden (PLAN.md §6).
    /// </summary>
    public IReadOnlyList<string> Summary()
    {
        var lines = new List<string>();
        lines.Add(NewCount switch
        {
            0 => "Nobody in this file is new.",
            1 => "1 person is new.",
            _ => $"{NewCount} people are new.",
        });

        if (UpdatedCount > 0)
        {
            lines.Add(UpdatedCount == 1
                ? "1 is already in your list — we'll update their details."
                : $"{UpdatedCount} are already in your list — we'll update their details.");
        }

        if (UnchangedCount > 0)
        {
            lines.Add(UnchangedCount == 1
                ? "1 is already in your list and unchanged."
                : $"{UnchangedCount} are already in your list and unchanged.");
        }

        if (LeftAloneCount > 0)
        {
            lines.Add(LeftAloneCount == 1
                ? "1 is already in your list and will be left alone."
                : $"{LeftAloneCount} are already in your list and will be left alone.");
        }

        if (Unusable.Count > 0)
        {
            lines.Add(Unusable.Count == 1
                ? "1 row we couldn't use."
                : $"{Unusable.Count} rows we couldn't use.");
        }

        if (Questions.Count > 0)
        {
            lines.Add(Questions.Count == 1
                ? "1 row looks like somebody you already have. We'll ask you about it."
                : $"{Questions.Count} rows look like people you already have. We'll ask you about them.");
        }

        return lines;
    }
}

/// <summary>How the user answered "are these the same person?" — and the default, which is neither.</summary>
public enum DuplicateAnswer
{
    Unanswered,
    SamePerson,
    DifferentPerson,
}

/// <summary>Choices the review screen offers.</summary>
public sealed record MergeOptions
{
    /// <summary>
    /// Off means "leave them alone instead": people already in the book keep every detail they have,
    /// and only genuinely new people are added.
    /// </summary>
    public bool UpdateExisting { get; init; } = true;

    public IReadOnlyDictionary<int, DuplicateAnswer> Answers { get; init; } =
        new Dictionary<int, DuplicateAnswer>();
}

/// <summary>
/// The merge policy — the most important rule set in this milestone (PLAN.md §11 M12).
///
/// <list type="number">
/// <item><description><b>Match in strict order:</b> exact id, then exact normalised email, then
/// exact normalised name. Anything else is a new person.</description></item>
/// <item><description><b>Update by match; never replace the file; never delete.</b> An import has no
/// way of removing anybody. Deletion is a deliberate per-person act in the People window, because a
/// list that is missing this year's new members is a normal thing to import and must not empty the
/// book.</description></item>
/// <item><description><b>Mapped columns overwrite; unmapped fields are left alone.</b> So importing
/// a phone-only list cannot wipe everyone's birthdays. An <em>empty cell</em> in a mapped column also
/// leaves the value alone, for the same reason one column of blanks should not clear a field for the
/// whole lodge.</description></item>
/// <item><description><b>Auto-merge on exact match only.</b> Everything fuzzy is a question, never
/// an action.</description></item>
/// </list>
/// </summary>
public static class RosterMerge
{
    /// <summary>
    /// Works out what an import would do, without doing any of it. Every screen after this reads
    /// this object; nothing is written until the user presses the button on the last one.
    /// </summary>
    /// <param name="current">The address book as it stands.</param>
    /// <param name="sheet">The user's file.</param>
    /// <param name="headerRow">Which row holds the column titles, or -1 for none.</param>
    /// <param name="mapping">Which column holds each lodge field.</param>
    /// <param name="options">The review screen's choices.</param>
    public static MergePlan Plan(
        RosterBook current,
        TableSheet sheet,
        int headerRow,
        IReadOnlyDictionary<RosterField, int> mapping,
        MergeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(mapping);
        options ??= new MergeOptions();

        RosterBook result = current;
        var rows = new List<PlannedRow>();
        var questions = new List<DuplicateQuestion>();

        for (int row = headerRow + 1; row < sheet.RowCount; row++)
        {
            string name = Read(sheet, row, mapping, RosterField.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                if (RowIsEmpty(sheet, row, mapping))
                {
                    continue;
                }

                rows.Add(new PlannedRow(
                    row + 1,
                    RowOutcome.Unusable,
                    new Member(),
                    null,
                    "There is no name in this row, so there is nobody to add."));
                continue;
            }

            Member? match = FindMatch(result, sheet, row, headerRow, mapping, name);
            if (match is null)
            {
                Member? near = FindNearMatch(result, name);
                DuplicateAnswer answer = near is null
                    ? DuplicateAnswer.Unanswered
                    : options.Answers.GetValueOrDefault(row + 1, DuplicateAnswer.Unanswered);

                if (near is not null && answer == DuplicateAnswer.Unanswered)
                {
                    questions.Add(new DuplicateQuestion(row + 1, name, near.Id, near.DisplayName));
                    rows.Add(new PlannedRow(
                        row + 1,
                        RowOutcome.Question,
                        Apply(new Member { DisplayName = name }, sheet, row, headerRow, mapping),
                        near.Id,
                        $"This looks like {near.DisplayName}, who is already in your list."));
                    continue;
                }

                if (near is not null && answer == DuplicateAnswer.SamePerson)
                {
                    match = near;
                }
            }

            if (match is null)
            {
                Member added = Apply(
                    new Member { Id = MemberIds.Next(result), DisplayName = name },
                    sheet,
                    row,
                    headerRow,
                    mapping);
                result = result.With(added);
                rows.Add(new PlannedRow(row + 1, RowOutcome.New, added, null, null));
                continue;
            }

            if (!options.UpdateExisting)
            {
                rows.Add(new PlannedRow(row + 1, RowOutcome.LeftAlone, match, match.Id, null));
                continue;
            }

            Member updated = Apply(match, sheet, row, headerRow, mapping);
            bool changed = updated != match;
            result = changed ? result.With(updated) : result;
            rows.Add(new PlannedRow(
                row + 1,
                changed ? RowOutcome.Updated : RowOutcome.Unchanged,
                updated,
                match.Id,
                changed ? Describe(match, updated) : null));
        }

        return new MergePlan(result, rows, questions);
    }

    /// <summary>
    /// Exact id, then exact email, then exact name. Nothing fuzzy — that is
    /// <see cref="FindNearMatch"/>'s job, and its answers are questions.
    /// </summary>
    private static Member? FindMatch(
        RosterBook book,
        TableSheet sheet,
        int row,
        int headerRow,
        IReadOnlyDictionary<RosterField, int> mapping,
        string name)
    {
        // The id column is what our own export writes, and what makes export → edit → re-import
        // lossless even when somebody's name changed in between.
        string id = ReadIdColumn(sheet, row, headerRow);
        if (id.Length > 0 && book.Find(id) is { } byId)
        {
            return byId;
        }

        string email = NameMatching.NormaliseEmail(Read(sheet, row, mapping, RosterField.Email));
        if (email.Length > 0)
        {
            Member? byEmail = book.Members.FirstOrDefault(
                m => NameMatching.NormaliseEmail(m.Email) == email);
            if (byEmail is not null)
            {
                return byEmail;
            }
        }

        string normalised = NameMatching.Normalise(name);
        return book.Members.FirstOrDefault(m => NameMatching.Normalise(m.DisplayName) == normalised);
    }

    private static Member? FindNearMatch(RosterBook book, string name) =>
        book.Members.FirstOrDefault(m => NameMatching.CouldBeTheSamePerson(m.DisplayName, name));

    /// <summary>
    /// Writes the mapped columns onto a member. Unmapped fields, and mapped columns whose cell is
    /// empty, are left exactly as they were — this is the rule that stops a phone-only list wiping
    /// the lodge's birthdays.
    /// </summary>
    private static Member Apply(
        Member member,
        TableSheet sheet,
        int row,
        int headerRow,
        IReadOnlyDictionary<RosterField, int> mapping)
    {
        Member result = member;

        string name = Read(sheet, row, mapping, RosterField.Name);
        if (name.Length > 0)
        {
            result = result with { DisplayName = name.Trim() };
        }

        string birthday = Read(sheet, row, mapping, RosterField.Birthday);
        if (birthday.Length > 0 && FieldValues.TryReadBirthday(birthday, out int month, out int day))
        {
            result = result with { BirthMonth = month, BirthDay = day };
        }

        string phone = Read(sheet, row, mapping, RosterField.Phone);
        if (phone.Length > 0)
        {
            result = result with { Phone = FieldValues.ReadPhone(phone) };
        }

        string email = Read(sheet, row, mapping, RosterField.Email);
        if (email.Length > 0)
        {
            result = result with { Email = FieldValues.ReadEmail(email) };
        }

        string office = Read(sheet, row, mapping, RosterField.Office);
        if (office.Length > 0)
        {
            result = result with { Office = office.Trim() };
        }

        string degreeKind = Read(sheet, row, mapping, RosterField.DegreeKind);
        if (degreeKind.Length > 0 && FieldValues.ReadDegreeKind(degreeKind) is { } kind)
        {
            result = result with { DegreeKind = kind };
        }

        string degreeDate = Read(sheet, row, mapping, RosterField.DegreeDate);
        if (degreeDate.Length > 0)
        {
            result = result with { DegreeDate = FieldValues.ReadDate(degreeDate) };

            // A column headed "Raised" says both when and which; taking the kind from the header is
            // free and saves the user a mapping they would otherwise have to think about.
            if (result.DegreeKind is null
                && FieldValues.ReadDegreeKind(HeaderOf(sheet, headerRow, mapping, RosterField.DegreeDate)) is { } implied)
            {
                result = result with { DegreeKind = implied };
            }
        }

        return result.Normalised();
    }

    private static string Describe(Member before, Member after)
    {
        var changes = new List<string>();
        if (before.DisplayName != after.DisplayName)
        {
            changes.Add("name");
        }

        if (before.BirthMonth != after.BirthMonth || before.BirthDay != after.BirthDay)
        {
            changes.Add("birthday");
        }

        if (before.Phone != after.Phone)
        {
            changes.Add("telephone number");
        }

        if (before.Email != after.Email)
        {
            changes.Add("email address");
        }

        if (before.Office != after.Office)
        {
            changes.Add("office");
        }

        if (before.DegreeDate != after.DegreeDate || before.DegreeKind != after.DegreeKind)
        {
            changes.Add("degree date");
        }

        return changes.Count == 0 ? string.Empty : "Changes the " + string.Join(", ", changes) + ".";
    }

    private static string Read(
        TableSheet sheet,
        int row,
        IReadOnlyDictionary<RosterField, int> mapping,
        RosterField field) =>
        mapping.TryGetValue(field, out int column) ? sheet.Cell(row, column).Trim() : string.Empty;

    private static string HeaderOf(
        TableSheet sheet,
        int headerRow,
        IReadOnlyDictionary<RosterField, int> mapping,
        RosterField field) =>
        headerRow >= 0 && mapping.TryGetValue(field, out int column)
            ? sheet.Cell(headerRow, column)
            : string.Empty;

    /// <summary>
    /// The "TrestleBoard ID" column our own export writes. Recognised by header text wherever it
    /// sits, and never offered on the mapping screen — it is machinery, not one of the user's fields.
    /// </summary>
    private static string ReadIdColumn(TableSheet sheet, int row, int headerRow)
    {
        if (headerRow < 0)
        {
            return string.Empty;
        }

        for (int column = 0; column < sheet.ColumnCount; column++)
        {
            string header = sheet.Cell(headerRow, column);
            if (header.Contains("TrestleBoard ID", StringComparison.OrdinalIgnoreCase))
            {
                return sheet.Cell(row, column).Trim();
            }
        }

        return string.Empty;
    }

    private static bool RowIsEmpty(TableSheet sheet, int row, IReadOnlyDictionary<RosterField, int> mapping)
    {
        foreach (int column in mapping.Values)
        {
            if (!string.IsNullOrWhiteSpace(sheet.Cell(row, column)))
            {
                return false;
            }
        }

        for (int column = 0; column < sheet.ColumnCount; column++)
        {
            if (!string.IsNullOrWhiteSpace(sheet.Cell(row, column)))
            {
                return false;
            }
        }

        return true;
    }
}
