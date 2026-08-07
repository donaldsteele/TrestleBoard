namespace TrestleBoard.Roster.Import;

/// <summary>Which lodge field a column of the user's spreadsheet holds.</summary>
public enum RosterField
{
    Name,
    Birthday,
    Phone,
    Email,
    Office,
    DegreeKind,
    DegreeDate,
}

/// <summary>
/// The seven questions the mapping screen asks, in the order it asks them (PLAN.md §11 M12,
/// screen 4).
///
/// <b>The screen is inverted from the usual import grid.</b> Instead of showing the file's columns
/// and asking what each one is, it asks one question per lodge field — <em>"Name — which column has
/// it?"</em> — because that is a question about the user's own list, phrased in their words. Only
/// <see cref="RosterField.Name"/> is required; every other row offers "Not in this file".
/// </summary>
public sealed record RosterFieldInfo(
    RosterField Field,
    string Question,
    string PlainName,
    bool Required,
    string[] HeaderHints)
{
    public static IReadOnlyList<RosterFieldInfo> All { get; } =
    [
        // "member" and "number" are NOT hints, and that is the point of this list being explicit.
        // Both are words that appear in headers naming something else entirely — "Member Number" is
        // a membership number, not a name and not a telephone — and a bare hint for either claimed
        // that column for the wrong field (review §14.2). Word-boundary matching does not help:
        // "member" really is a whole word there. The hint has to be the thing itself.
        new(RosterField.Name, "Name — which column has it?", "Name", true,
            ["name", "member name", "brother", "person", "full name"]),
        new(RosterField.Birthday, "Birthday — which column has it?", "Birthday", false,
            ["birthday", "birth", "dob", "bday", "born"]),
        new(RosterField.Phone, "Telephone number — which column has it?", "Telephone", false,
            ["phone", "tel", "cell", "mobile", "phone number", "telephone number"]),
        new(RosterField.Email, "Email address — which column has it?", "Email", false,
            ["email", "e-mail", "mail"]),
        new(RosterField.Office, "Lodge office — which column has it?", "Office", false,
            ["office", "title", "position", "station", "rank"]),
        // "raised or initiated" first, and deliberately: it is what our own export writes as a
        // header, and without it that column matches the DegreeDate hint "raised" instead — which
        // would store the word "Raised" as somebody's degree date on a re-import of our own file.
        new(RosterField.DegreeKind, "Raised or initiated — which column says which?", "Raised or initiated", false,
            ["raised or initiated", "degree", "status", "kind"]),
        new(RosterField.DegreeDate, "The date he was raised or initiated — which column has it?", "Date", false,
            ["raised", "initiated", "degree date", "date"]),
    ];

    public static RosterFieldInfo For(RosterField field) => All.First(f => f.Field == field);
}
