using System.Globalization;
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

    protected override JsonTypeInfo<BirthdayListData> TypeInfo =>
        BirthdayListJsonContext.Default.BirthdayListData;

    public override BirthdayListData CreateEmpty(WidgetSeed seed) => new();

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
                            "name", r => r.Name, (r, v) => r.Name = v)),
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
                allowReorder: false),
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
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int day))
        {
            row.Month = month;
            row.Day = day;
        }
    }
}
