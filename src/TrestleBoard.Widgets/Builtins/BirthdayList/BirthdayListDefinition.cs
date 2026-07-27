using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Widgets.Builtins.BirthdayList;

/// <summary>
/// The every-issue birthday column. Inserted with zero rows — a wizard never starts pre-populated
/// with people (PLAN.md §0, docs/M7-spec.md §8.3).
/// </summary>
public sealed class BirthdayListDefinition : WidgetDefinition<BirthdayListData>
{
    public override string TypeId => "birthdayList";

    public override string DisplayName => "Birthdays";

    public override string Description => "This month's member birthdays, as a narrow column.";

    public override string IconKey => "birthdays";

    public override SizePt DefaultSizePt => new(150f, 220f);

    /// <summary>
    /// A narrow column holding up to twenty short rows needs a denser body size and single (not the
    /// house 1.15×) line spacing to stay inside the default frame — the Standard body size and
    /// spacing alone measure past 300pt for twenty rows (docs/M7-spec.md §9.2's 20-row case).
    /// </summary>
    /// <summary>
    /// A touch smaller and tighter than the house body face: this is the signature NARROW column
    /// that body text flows beside, and the printed issues set it that way. It stops well short of
    /// unreadable — the readers are elderly (PLAN.md §6) — so a long list overflows the default box
    /// and is fixed by growing the box, never by shrinking the type.
    /// </summary>
    public override WidgetStyleDefaults StyleDefaults { get; } = WidgetStyleDefaults.Standard with
    {
        Body = WidgetStyleDefaults.Standard.Body with { SizePt = 9f },
        LineSpacing = 1.05f,
    };

    public override IWidgetLayouter Layouter { get; } = new BirthdayListLayouter();

    /// <summary>
    /// 2 since M13: the list gained where-it-came-from fields, purely additive. A v2 payload opened
    /// in a pre-M13 build is move/resize/delete-only, which is designed behaviour rather than an
    /// accident (PLAN.md §11 M13, "rollout note").
    /// </summary>
    public override int CurrentDataVersion => 2;

    protected override JsonTypeInfo<BirthdayListData> TypeInfo =>
        BirthdayListJsonContext.Default.BirthdayListData;

    /// <summary>
    /// Empty, and — this is the part PLAN.md §5's projection rule protects — empty with no reference
    /// to the address book. The roster reaches this widget only through
    /// <see cref="Roster.BirthdayRosterProjection"/>, which takes the member list as a parameter and
    /// is invoked by a user-confirmed action. Never from here, and never from <see cref="WidgetSeed"/>.
    /// </summary>
    public override BirthdayListData CreateEmpty(WidgetSeed seed) => new();

    /// <summary>
    /// v1 → v2. Everything new is optional, so the whole upgrade is "say out loud that this list was
    /// typed by hand", which is what every v1 list was. Entries need nothing: a row with no
    /// <c>memberId</c> is a typed row by definition, and that is exactly what the projection leaves
    /// alone (PLAN.md §11 M13).
    /// </summary>
    public override bool TryMigrateStep(JsonNode data, int fromVersion, out JsonNode upgraded, out int toVersion)
    {
        ArgumentNullException.ThrowIfNull(data);
        upgraded = data;
        toVersion = fromVersion;
        if (fromVersion != 1)
        {
            return false;
        }

        data["source"] = BirthdayListSource.Manual;
        toVersion = 2;
        return true;
    }

    protected override WizardDefinition BuildWizard() => new(
        DisplayName,
        "We will go through the birthdays one at a time. We only need the month and day.",
        [
            new FieldsStep<BirthdayListData>(
                "What should this list be called?",
                "This heading prints above the list.",
                [
                    (new WizardField(
                            "heading",
                            "Heading",
                            ExampleText: "Birthdays"),
                        new WizardFieldBinding<BirthdayListData>(
                            "heading", d => d.Heading, (d, v) => d.Heading = v)),
                ]),
            new RecordListStep<BirthdayListData, BirthdayEntry>(
                "Birthdays",
                "Type the brother's name and the month and day of his birthday. No year, no age.",
                d => d.Entries,
                () => new BirthdayEntry(),
                [
                    (new WizardField(
                            "name",
                            "Name",
                            ExampleText: "A. Placeholder"),
                        new WizardFieldBinding<BirthdayEntry>(
                            "name", r => r.Name, SetName)),
                    (new WizardField(
                            "date",
                            "Birthday (month and day)",
                            WizardFieldKind.MonthDay,
                            HelpText: "Just the month and day, like 1/1 or 12/25.",
                            ExampleText: "7/4"),
                        new WizardFieldBinding<BirthdayEntry>(
                            "date",
                            r => WizardValidators.FormatMonthDay(r.Month, r.Day),
                            SetDate)),
                ],
                pagination: WizardListPagination.AllRows,
                // Reordering rows would not change anything printed — the layouter always sorts by
                // date — so offering "move up/down" here would just puzzle an elderly user
                // (docs/M7-spec.md §9.2).
                allowReorder: false,
                onRemoveRow: RememberRemoval),
            new FieldsStep<BirthdayListData>(
                "Anything else?",
                "Optional. Prints after the list.",
                [
                    (new WizardField(
                            "closingNote",
                            "Closing note",
                            IsOptional: true,
                            ExampleText: "Miss anyone? Let us know."),
                        new WizardFieldBinding<BirthdayListData>(
                            "closingNote",
                            d => d.ClosingNote ?? "",
                            (d, v) => d.ClosingNote = string.IsNullOrWhiteSpace(v) ? null : v)),
                ]),
        ]);

    /// <summary>
    /// Taking a generated row off the list is a decision, and it has to be remembered: without this
    /// the next "Bring in birthdays from the address book" would put the brother the user just
    /// deleted straight back (PLAN.md §11 M13). A typed row has no id and needs nothing remembered.
    /// </summary>
    private static void RememberRemoval(BirthdayListData data, BirthdayEntry row)
    {
        if (row.MemberId is { Length: > 0 } id && !data.RemovedMemberIds.Contains(id, StringComparer.Ordinal))
        {
            data.RemovedMemberIds.Add(id);
        }
    }

    /// <summary>
    /// Provenance is captured here, in the setter both editors already share (PLAN.md §11 M13): the
    /// step wizard and <c>WidgetGridWindow</c> drive the same field bindings, so one line per bound
    /// field marks a row as the user's without either window learning anything new.
    ///
    /// The equality guard is load-bearing, not tidiness. Both windows write the box's value back on
    /// LostFocus, so tabbing through a generated list without typing a character would otherwise mark
    /// every row manual and freeze it against every future re-sync.
    /// </summary>
    private static void SetName(BirthdayEntry row, string value)
    {
        if (!string.Equals(row.Name, value, StringComparison.Ordinal))
        {
            row.Name = value;
            row.IsManual = true;
        }
    }

    /// <summary>
    /// Splits into two numbers the same way <see cref="WizardValidators.TryParseMonthDay"/> does, but
    /// without its 1–12/1–31 range check, so an out-of-range date (e.g. "13/45") is still stored and
    /// therefore still round-trips through <see cref="WizardFieldBinding{T}.Get"/> as a non-blank,
    /// still-invalid string. That is what lets the framework's Get-based revalidation (§6.2 — it
    /// revalidates the bound value, not the raw keystroke) reach <see cref="WizardValidators.MonthDay"/>
    /// and report its sentence, instead of falling back to "please fill this in" for a value the user
    /// did type. A value that is not two numbers at all leaves the row unchanged.
    /// </summary>
    private static void SetDate(BirthdayEntry row, string value)
    {
        string[] parts = value.Split(['/', '-'], StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int month)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int day)
            && (row.Month != month || row.Day != day))
        {
            row.Month = month;
            row.Day = day;
            row.IsManual = true;
        }
    }
}
