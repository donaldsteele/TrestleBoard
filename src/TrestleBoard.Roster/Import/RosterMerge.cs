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

    /// <summary>
    /// A row for somebody's wife rather than for a member (M88).
    ///
    /// <para>The lodge's own export puts spouses in the same sheet, marked "Spouse of &lt;name&gt;
    /// #&lt;number&gt;". They are never added as people — a spouse is fields on her husband's card —
    /// and they are never silently dropped either: the review screen counts them out loud, and where
    /// the row names a member number we already have, her details land on his card.</para>
    /// </summary>
    Spouse,
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

    /// <summary>Rows that were somebody's wife rather than a member (M88).</summary>
    public int SpouseCount => Rows.Count(r => r.Outcome == RowOutcome.Spouse);

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

        if (SpouseCount > 0)
        {
            lines.Add(SpouseCount == 1
                ? "1 row is a spouse, not a member. We'll put her details on her husband's card."
                : $"{SpouseCount} rows are spouses, not members. We'll put their details on their "
                    + "husbands' cards.");
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

        // Rows that are somebody's wife or somebody's child, kept until every member has been read
        // (M89). See the note where they are collected.
        var heldBack = new List<(int Row, string Name, RowKind Kind)>();

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

            // M88. A row that is not a member is never matched against one: her name would find her
            // husband by surname and overwrite him, or fail to and put her on the lodge's roll.
            //
            // M89: the wives are held back and applied AFTER every member row, because a wife
            // whose husband sits lower down the sheet cannot be filed against a man the book does
            // not have yet. Ten of the lodge's fifteen wives failed exactly that way.
            if (KindOfRow(sheet, row, mapping) is { } kind)
            {
                heldBack.Add((row, name, kind));
                continue;
            }

            Member? match = FindMatch(result, sheet, row, headerRow, mapping, name);
            if (match is null)
            {
                Member? near = FindNearMatch(
                    result, name, Read(sheet, row, mapping, RosterField.MemberNumber));
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

        // The wives and the children, now that every member the file carries is in the book (M89).
        foreach ((int row, string name, RowKind kind) in heldBack)
        {
            if (kind == RowKind.Dependant)
            {
                rows.Add(new PlannedRow(
                    row + 1,
                    RowOutcome.Unusable,
                    new Member { DisplayName = name },
                    null,
                    $"\"{name}\" is recorded as somebody's child. The address book holds the lodge's "
                    + "members and their wives, so this row was not added."));
                continue;
            }

            (RosterBook afterSpouse, string note) = ApplySpouse(result, sheet, row, mapping, name);
            bool landed = afterSpouse != result;
            result = afterSpouse;
            rows.Add(new PlannedRow(
                row + 1,
                landed ? RowOutcome.Spouse : RowOutcome.Unusable,
                new Member { DisplayName = name },
                null,
                note));
        }

        // Back into the order they sit in the file, so the review screen reads down the sheet.
        rows.Sort((a, b) => a.RowNumber.CompareTo(b.RowNumber));

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

        // M88: the lodge's own member number, second only to our own id.
        //
        // It is above email deliberately. An email address can be shared — a husband and wife on one
        // account, a father and son on the family address — while a member number is issued once,
        // to one man. It only matches when exactly one member carries it: a book where two people
        // somehow hold the same number falls through to the slower keys rather than picking one.
        string number = Read(sheet, row, mapping, RosterField.MemberNumber).Trim();
        if (number.Length > 0)
        {
            List<Member> byNumber = [.. book.Members.Where(
                m => string.Equals(m.MemberNumber?.Trim(), number, StringComparison.OrdinalIgnoreCase))];
            if (byNumber.Count == 1)
            {
                return byNumber[0];
            }
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

        // The name key, with M88's guard on it.
        //
        // NameMatching.Normalise strips Jr, Sr, II and III — right for deciding that "Placeholder,
        // A." and "A. Placeholder" are one man, and catastrophic for a father and a son who share
        // every other part of their name. Two such pairs sit in this lodge's own list: before the
        // guard, the son's row matched the father exactly and overwrote him, and the book came out
        // two men short with nothing said. The member number settles it — when both sides carry one
        // and they differ, these are two people, whatever the names do.
        string normalised = NameMatching.Normalise(name);
        return book.Members.FirstOrDefault(m =>
            NameMatching.Normalise(m.DisplayName) == normalised && !NumbersDisagree(m, number));
    }

    private static Member? FindNearMatch(RosterBook book, string name, string incomingNumber) =>
        book.Members.FirstOrDefault(m =>
            NameMatching.CouldBeTheSamePerson(m.DisplayName, name) && !NumbersDisagree(m, incomingNumber));

    /// <summary>
    /// Two non-empty member numbers that are not the same number (M88) — the one fact that can say
    /// "these are different men" about two people whose names cannot be told apart. Silent when
    /// either side has no number, which is every lodge that has not imported one yet.
    /// </summary>
    private static bool NumbersDisagree(Member member, string incomingNumber)
    {
        string mine = member.MemberNumber?.Trim() ?? string.Empty;
        string theirs = incomingNumber.Trim();
        return mine.Length > 0
            && theirs.Length > 0
            && !string.Equals(mine, theirs, StringComparison.OrdinalIgnoreCase);
    }

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

        // ---- M55 -------------------------------------------------------------------------------
        string stillAMember = Read(sheet, row, mapping, RosterField.StillAMember);
        if (stillAMember.Length > 0 && FieldValues.ReadYesNo(stillAMember) is { } active)
        {
            result = result with { IsActive = active };
        }

        string passedOn = Read(sheet, row, mapping, RosterField.PassedOn);
        if (passedOn.Length > 0)
        {
            result = result with { PassedOn = FieldValues.ReadDate(passedOn) };
        }

        string groups = Read(sheet, row, mapping, RosterField.Groups);
        if (groups.Length > 0)
        {
            result = result with { Groups = FieldValues.ReadGroups(groups) };
        }

        // ---- M88 -------------------------------------------------------------------------------
        // Every one of these follows the same rule as the fields above it: a column that was not
        // mapped, and a mapped column whose cell is empty, leave what the book already holds alone.
        result = Text(result, sheet, row, mapping, RosterField.MemberNumber, (m, v) => m with { MemberNumber = v });
        result = Text(result, sheet, row, mapping, RosterField.MasonicTitle, (m, v) => m with { MasonicTitle = v });
        result = Text(result, sheet, row, mapping, RosterField.AddressLine1, (m, v) => m with { AddressLine1 = v });
        result = Text(result, sheet, row, mapping, RosterField.AddressLine2, (m, v) => m with { AddressLine2 = v });
        result = Text(result, sheet, row, mapping, RosterField.City, (m, v) => m with { City = v });
        result = Text(result, sheet, row, mapping, RosterField.State, (m, v) => m with { State = v });
        result = Text(result, sheet, row, mapping, RosterField.Zip, (m, v) => m with { Zip = v });
        result = Text(result, sheet, row, mapping, RosterField.SpouseName, (m, v) => m with { SpouseName = v });
        result = Text(result, sheet, row, mapping, RosterField.SpouseEmail, (m, v) => m with { SpouseEmail = v });
        result = Text(result, sheet, row, mapping, RosterField.Notes, (m, v) => m with { Notes = v });

        result = Phone(result, sheet, row, mapping, RosterField.HomePhone, (m, v) => m with { HomePhone = v });
        result = Phone(result, sheet, row, mapping, RosterField.MobilePhone, (m, v) => m with { MobilePhone = v });
        result = Phone(result, sheet, row, mapping, RosterField.WorkPhone, (m, v) => m with { WorkPhone = v });
        result = Phone(result, sheet, row, mapping, RosterField.SpousePhone, (m, v) => m with { SpousePhone = v });

        string degree = Read(sheet, row, mapping, RosterField.Degree);
        if (degree.Length > 0 && FieldValues.ReadDegree(degree) is { } held)
        {
            result = result with { Degree = held };
        }

        string undeliverable = Read(sheet, row, mapping, RosterField.AddressUndeliverable);
        if (undeliverable.Length > 0 && FieldValues.ReadYesNo(undeliverable) is { } comesBack)
        {
            result = result with { AddressUndeliverable = comesBack };
        }

        // The year, taken from the birthday column itself rather than asked for separately (M88).
        if (birthday.Length > 0
            && FieldValues.TryReadBirthday(birthday, out _, out _, out int birthYear)
            && birthYear > 0)
        {
            result = result with { BirthYear = birthYear };
        }

        // A year in a column of its own wins over one read out of the birthday cell: the file said
        // it twice, and the column that exists to hold it is the one that meant to.
        string year = Read(sheet, row, mapping, RosterField.BirthYear);
        if (year.Length > 0
            && int.TryParse(
                year.Trim(),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int typedYear))
        {
            result = result with { BirthYear = typedYear };
        }

        // The printed telephone number, when the lodge's file has no column for it (M88).
        //
        // The secretary's export has Home, Mobile and Work and no plain "Phone" at all, so without
        // this every one of 112 members would import with nothing to print in the officers table.
        // It fills an EMPTY Phone only — a number already in the book is never overwritten by this
        // rule — and Describe() names it, so the review screen counts it out loud rather than the
        // app quietly deciding which of a man's numbers is public.
        if (string.IsNullOrWhiteSpace(result.Phone))
        {
            string? carried = result.MobilePhone ?? result.HomePhone;
            if (!string.IsNullOrWhiteSpace(carried))
            {
                result = result with { Phone = carried };
            }
        }

        return result.Normalised();
    }

    /// <summary>One mapped text column onto one field, or nothing at all when it is empty (M88).</summary>
    private static Member Text(
        Member member,
        TableSheet sheet,
        int row,
        IReadOnlyDictionary<RosterField, int> mapping,
        RosterField field,
        Func<Member, string, Member> set)
    {
        string value = Read(sheet, row, mapping, field);
        return value.Length > 0 ? set(member, value.Trim()) : member;
    }

    /// <summary>As <see cref="Text"/>, through the telephone reader — which is what rescues a number
    /// a spreadsheet turned into 8.03555E+09.</summary>
    private static Member Phone(
        Member member,
        TableSheet sheet,
        int row,
        IReadOnlyDictionary<RosterField, int> mapping,
        RosterField field,
        Func<Member, string, Member> set)
    {
        string value = Read(sheet, row, mapping, field);
        return value.Length > 0 && FieldValues.ReadPhone(value) is { } number
            ? set(member, number)
            : member;
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

        // M55. Without these three the review screen would say "Changes nothing" beside a row that
        // is about to record a brother as having died, which is the worst possible place for the
        // app to be quiet.
        if (before.IsActive != after.IsActive)
        {
            changes.Add(after.IsActive ? "him back to being a member" : "him to no longer a member");
        }

        if (before.PassedOn != after.PassedOn)
        {
            changes.Add(after.HasPassed ? "him to passed to the Celestial Lodge" : "the date he passed");
        }

        if (!before.Groups.SequenceEqual(after.Groups, StringComparer.Ordinal))
        {
            changes.Add("groups");
        }

        // ---- M88. Every new field says its own name, in the words the form uses ----------------
        Say(before.MemberNumber, after.MemberNumber, "member number");
        Say(before.Degree, after.Degree, "highest degree");
        Say(before.MasonicTitle, after.MasonicTitle, "letters after his name");
        Say(before.HomePhone, after.HomePhone, "home telephone");
        Say(before.MobilePhone, after.MobilePhone, "mobile telephone");
        Say(before.WorkPhone, after.WorkPhone, "work telephone");
        Say(before.SpouseName, after.SpouseName, "spouse's name");
        Say(before.SpouseEmail, after.SpouseEmail, "spouse's email");
        Say(before.SpousePhone, after.SpousePhone, "spouse's telephone");
        Say(before.Notes, after.Notes, "notes");

        if (before.BirthYear != after.BirthYear)
        {
            changes.Add("year he was born");
        }

        // The five address fields are one fact to the reader, and five lines about a house move
        // would bury the one line that says somebody died.
        if (before.AddressLine1 != after.AddressLine1
            || before.AddressLine2 != after.AddressLine2
            || before.City != after.City
            || before.State != after.State
            || before.Zip != after.Zip)
        {
            changes.Add("postal address");
        }

        if (before.AddressUndeliverable != after.AddressUndeliverable)
        {
            changes.Add(after.AddressUndeliverable
                ? "him to post that comes back undelivered"
                : "him back to post that arrives");
        }

        return changes.Count == 0 ? string.Empty : "Changes the " + string.Join(", ", changes) + ".";

        void Say(string? before2, string? after2, string word)
        {
            if (before2 != after2)
            {
                changes.Add(word);
            }
        }
    }

    /// <summary>
    /// Is this row somebody's wife rather than a member? (M88)
    ///
    /// <para>Read from whichever column the user pointed at as "which says which a row is" — in the
    /// lodge's own export, a cell reading "Spouse of Placeholder, A. #98506". No column mapped means
    /// every row is a member, which is what every ordinary lodge list is.</para>
    /// </summary>
    /// <summary>Who a row is about, when it is not about a member (M88, widened at M89).</summary>
    private enum RowKind
    {
        /// <summary>Somebody's wife. Her details belong on his card.</summary>
        Spouse,

        /// <summary>
        /// Somebody's child (M89). The lodge's export carries one, and before this it imported as a
        /// member of the lodge. The address book has nowhere to put a child and does not invent one:
        /// the row is reported in a sentence rather than added, and rather than silently dropped.
        /// </summary>
        Dependant,
    }

    /// <summary>
    /// What this row is, or null for an ordinary member.
    ///
    /// <para>Read from whichever column the user pointed at as "which says which a row is" — in the
    /// lodge's export, a cell reading "Spouse of Placeholder, A. #98506". No column mapped means
    /// every row is a member, which is what every ordinary lodge list is.</para>
    /// </summary>
    private static RowKind? KindOfRow(
        TableSheet sheet, int row, IReadOnlyDictionary<RosterField, int> mapping)
    {
        string kind = Read(sheet, row, mapping, RosterField.RowKind).Trim();
        if (kind.Length == 0)
        {
            return null;
        }

        if (Says("spouse") || Says("wife") || Says("husband") || Says("widow") || Says("partner"))
        {
            return RowKind.Spouse;
        }

        return Says("child") || Says("son of") || Says("daughter") || Says("dependent") || Says("dependant")
            ? RowKind.Dependant
            : null;

        bool Says(string word) => kind.Contains(word, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Puts a spouse row's details onto her husband's card, and says what happened (M88).
    ///
    /// <para>She is found by the member number in her own row — the lodge's export writes "Spouse of
    /// &lt;his name&gt; #&lt;his number&gt;" — or by her own member-number column where the file has
    /// one. If neither names a member we hold, nothing is written and the row is reported as one we
    /// could not use, with a sentence saying why: a wife filed against the wrong husband would be a
    /// worse outcome than a row the secretary has to look at.</para>
    /// </summary>
    private static (RosterBook Book, string Note) ApplySpouse(
        RosterBook book,
        TableSheet sheet,
        int row,
        IReadOnlyDictionary<RosterField, int> mapping,
        string name)
    {
        string kindCell = Read(sheet, row, mapping, RosterField.RowKind);
        string number = HusbandsNumber(kindCell, Read(sheet, row, mapping, RosterField.MemberNumber));

        if (number.Length == 0)
        {
            return (book, $"\"{name}\" is a spouse, and this row does not say whose. Nothing was "
                + "changed for her.");
        }

        List<Member> husbands = [.. book.Members.Where(
            m => string.Equals(m.MemberNumber?.Trim(), number, StringComparison.OrdinalIgnoreCase))];
        if (husbands.Count != 1)
        {
            return (book, $"\"{name}\" is a spouse of member number {number}, who is not in your "
                + "list. Nothing was changed for her.");
        }

        Member husband = husbands[0];
        Member updated = husband with
        {
            SpouseName = name.Trim(),
            SpouseEmail = Blank(Read(sheet, row, mapping, RosterField.Email)) ?? husband.SpouseEmail,
            SpousePhone = Blank(Read(sheet, row, mapping, RosterField.MobilePhone))
                ?? Blank(Read(sheet, row, mapping, RosterField.HomePhone))
                ?? Blank(Read(sheet, row, mapping, RosterField.Phone))
                ?? husband.SpousePhone,
        };

        return (book.With(updated.Normalised()),
            $"\"{name}\" is {husband.DisplayName}'s spouse. Her details go on his card.");
    }

    /// <summary>
    /// The member number a spouse row points at: the "#98506" the lodge writes after her husband's
    /// name, or failing that whatever her own member-number column holds.
    /// </summary>
    private static string HusbandsNumber(string kindCell, string ownNumber)
    {
        int hash = kindCell.LastIndexOf('#');
        if (hash >= 0)
        {
            string tail = new([.. kindCell[(hash + 1)..].TakeWhile(char.IsAsciiLetterOrDigit)]);
            if (tail.Length > 0)
            {
                return tail;
            }
        }

        return ownNumber.Trim();
    }

    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
