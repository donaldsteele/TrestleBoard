namespace TrestleBoard.Roster.Import;

/// <summary>Which lodge field a column of the user's spreadsheet holds.</summary>
public enum RosterField
{
    Name,
    Birthday,
    Phone,
    Email,
    Office,

    /// <summary>
    /// Which ceremony <see cref="DegreeDate"/> records — raised or initiated (M12).
    ///
    /// <para>M88 added <see cref="Degree"/> beside it for the different question of how far a man
    /// has come. This one is kept because a lodge's older spreadsheet, and TrestleBoard's own export
    /// before M88, both have a column headed exactly this.</para>
    /// </summary>
    DegreeKind,
    DegreeDate,

    /// <summary>M55: still a member of the lodge. Exported since M12 and, until M55, never read back.</summary>
    StillAMember,

    /// <summary>M55: the date a brother was called to the Celestial Lodge.</summary>
    PassedOn,

    /// <summary>M55: which mailing lists this person is on, separated by semicolons.</summary>
    Groups,

    // ---- M88: what the lodge's own member system holds -----------------------------------------

    /// <summary>The number the lodge gave him. Also the merge's second match key.</summary>
    MemberNumber,

    /// <summary>How far he has come: Entered Apprentice, Fellowcraft or Master Mason.</summary>
    Degree,

    /// <summary>The letters after his name — "PM".</summary>
    MasonicTitle,

    AddressLine1,
    AddressLine2,
    City,
    State,
    Zip,
    AddressUndeliverable,
    HomePhone,
    MobilePhone,
    WorkPhone,
    SpouseName,
    SpouseEmail,
    SpousePhone,

    /// <summary>
    /// Which column says whether a row is a member or somebody's wife (M88).
    ///
    /// <para>The lodge's export puts spouses in the same sheet as members, one row each, marked
    /// "Spouse of &lt;name&gt; #&lt;number&gt;". Without this the import would add sixteen women to the
    /// lodge's roll.</para>
    /// </summary>
    RowKind,

    /// <summary>
    /// The year, where the file keeps it in a column of its own rather than inside the birthday
    /// (M88) — which is exactly what TrestleBoard's own export does.
    ///
    /// <para>A birthday cell carrying "1957-02-02" still needs no second question: the birthday
    /// parser hands the year back with the month and day. This exists for the other shape, and the
    /// round-trip test is what found it — the year was written to the file and had no way home.</para>
    /// </summary>
    BirthYear,

    /// <summary>
    /// Whatever the committee wrote about this person (M88).
    ///
    /// <para>Stored since M12 and, until now, neither exported nor importable nor shown anywhere —
    /// a field the user could not reach. M88 gives it a box on the form, a column in the export and
    /// this row on the mapping screen, which is what it takes for a field to be real.</para>
    /// </summary>
    Notes,
}

/// <summary>Which half of the mapping screen — and of the People window — a field belongs to (M88).</summary>
public enum RosterFieldSection
{
    /// <summary>What somebody looking up a phone number came for.</summary>
    TheBasics,

    /// <summary>Everything the lodge's own system holds beside it.</summary>
    AddressAndMore,
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
    string[] HeaderHints,
    RosterFieldSection Section = RosterFieldSection.TheBasics)
{
    public static IReadOnlyList<RosterFieldInfo> All { get; } =
    [
        // "member" and "number" are NOT hints, and that is the point of this list being explicit.
        // Both are words that appear in headers naming something else entirely — "Member Number" is
        // a membership number, not a name and not a telephone — and a bare hint for either claimed
        // that column for the wrong field (review §14.2). Word-boundary matching does not help:
        // "member" really is a whole word there. The hint has to be the thing itself.
        // "fullname" with no space is here because that is what the lodge member system writes, and
        // word-boundary matching means the hint "name" does NOT find it — the N is flanked by a
        // letter. Without this the column headed "First Name" won the field and a hundred and
        // twelve brethren imported under their first names alone (M89).
        new(RosterField.Name, "Name — which column has it?", "Name", true,
            ["name", "fullname", "member name", "brother", "person", "full name"]),
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
        // M55. Three columns our own export writes, so a spreadsheet edited in Excel and brought
        // back keeps them. "Active" was written from M12 and read by nothing, which meant somebody
        // could un-tick a brother in Excel, re-import, and watch the change vanish without a word.
        //
        // Note what is NOT a hint here: "status". It has belonged to "Raised or initiated" since
        // M12 (see the list above), and claiming it now would quietly re-point every existing
        // lodge's status column at a different field on their next import.
        new(RosterField.StillAMember, "Still a member — which column says so?", "Still a member", false,
            ["still a member", "active", "current member", "on the rolls"]),
        new(RosterField.PassedOn, "Passed to the Celestial Lodge — which column has the date?", "Passed on", false,
            ["passed on", "passed", "deceased", "died", "date of death", "celestial lodge"]),
        new(RosterField.Groups, "Groups — which column lists them?", "Groups", false,
            ["groups", "group", "mailing list", "lists", "distribution"]),

        // ---- M88 ---------------------------------------------------------------------------
        // Every hint below is either multi-word or a word no existing field claims, because the
        // guesser now prefers the LONGEST matching hint: "Mobile Phone" is a mobile number rather
        // than the printed telephone, "Address Undeliverable" is the flag rather than the street,
        // and "Highest Degree Date" is a date rather than a degree. The three collisions M12 and
        // M25 recorded stay shut — "member", "number", "mail" and "status" are still not hints of
        // anybody's.
        new(RosterField.Degree, "Highest degree — which column says it?", "Highest degree", false,
            ["highest degree", "degree held", "current degree"]),
        new(RosterField.MemberNumber, "Lodge member number — which column has it?", "Member number", false,
            ["member number", "membership number", "member no", "member #", "lodge number"],
            RosterFieldSection.AddressAndMore),
        new(RosterField.MasonicTitle, "Letters after his name, like PM — which column has them?",
            "Letters after his name", false,
            ["masonic suffix", "masonic title", "post nominal", "letters after"],
            RosterFieldSection.AddressAndMore),
        new(RosterField.AddressLine1, "Street address — which column has it?", "Street address", false,
            ["address", "street", "mailing address", "address 1", "address line 1", "street address"],
            RosterFieldSection.AddressAndMore),
        new(RosterField.AddressLine2, "Flat, unit or second address line — which column has it?",
            "Address line 2", false,
            ["address 2", "address2", "address line 2", "apartment", "unit", "suite"],
            RosterFieldSection.AddressAndMore),
        new(RosterField.City, "Town or city — which column has it?", "City", false,
            ["city", "town"], RosterFieldSection.AddressAndMore),
        new(RosterField.State, "State — which column has it?", "State", false,
            ["state", "province"], RosterFieldSection.AddressAndMore),
        new(RosterField.Zip, "ZIP code — which column has it?", "ZIP code", false,
            ["zip", "zip code", "postcode", "postal code"], RosterFieldSection.AddressAndMore),
        new(RosterField.AddressUndeliverable, "Post that comes back — which column says so?",
            "Post comes back undelivered", false,
            ["undeliverable", "address undeliverable", "comes back undelivered", "undelivered",
             "bad address", "returned mail", "no mail"],
            RosterFieldSection.AddressAndMore),
        // Not "office phone": "office" has belonged to the lodge office since M12, and a hint that
        // long would win the column headed "Office" itself away from it.
        new(RosterField.HomePhone, "Home telephone — which column has it?", "Home telephone", false,
            ["home phone", "home telephone", "house phone", "home number"],
            RosterFieldSection.AddressAndMore),
        new(RosterField.MobilePhone, "Mobile telephone — which column has it?", "Mobile telephone", false,
            // "mobile telephone" is here because it is what our OWN export writes. Without it that
            // header matched the printed telephone's older hint "mobile" — six letters — and the
            // lodge's real Phone column was left unmapped on a re-import of our own file. The same
            // shape of bug M12 recorded for "Raised or initiated", found the same way: a round trip.
            ["mobile telephone", "mobile phone", "cell telephone", "cell phone", "cellular",
             "mobile number", "cell number"],
            RosterFieldSection.AddressAndMore),
        new(RosterField.WorkPhone, "Work telephone — which column has it?", "Work telephone", false,
            ["work phone", "work telephone", "business phone", "work number"],
            RosterFieldSection.AddressAndMore),
        new(RosterField.SpouseName, "His wife's name — which column has it?", "Spouse's name", false,
            ["spouse's name", "spouse name", "spouse", "wife", "husband", "partner"],
            RosterFieldSection.AddressAndMore),
        new(RosterField.SpouseEmail, "His wife's email — which column has it?", "Spouse's email", false,
            ["spouse's email", "spouse email", "wife email", "partner email"],
            RosterFieldSection.AddressAndMore),
        new(RosterField.SpousePhone, "His wife's telephone — which column has it?", "Spouse's telephone", false,
            ["spouse's telephone", "spouse's phone", "spouse telephone", "spouse phone",
             "wife phone"],
            RosterFieldSection.AddressAndMore),
        new(RosterField.BirthYear, "The year he was born — which column has it?", "Birth year", false,
            // "birth year" beats Birthday's "birth" on length, which is what keeps a file with both
            // columns from putting the year where the birthday goes.
            ["birth year", "year of birth", "year born", "birth yr"]),
        new(RosterField.Notes, "Notes — which column has them?", "Notes", false,
            ["notes", "note", "remarks", "comments"],
            RosterFieldSection.AddressAndMore),
        new(RosterField.RowKind, "Members and spouses — which column says which a row is?",
            "Member or spouse", false,
            // NOT "item type": the lodge export has a column of that name holding a sentence about
            // a birthday, beside the column headed "Type" that actually says member or spouse.
            ["type", "record type", "row type", "member or spouse"],
            RosterFieldSection.AddressAndMore),
    ];

    public static RosterFieldInfo For(RosterField field) => All.First(f => f.Field == field);
}
