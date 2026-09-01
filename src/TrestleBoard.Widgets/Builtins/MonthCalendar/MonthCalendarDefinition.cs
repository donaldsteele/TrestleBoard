using System;
using System.Text.Json.Serialization.Metadata;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Widgets.Builtins.MonthCalendar;

/// <summary>
/// This month, on a grid (PLAN.md §11 M84) — the eighth widget.
///
/// <para>The existing district calendar is a table, which is the right shape for six lodges and the
/// wrong one for one month. What a trestle board wants is the month laid out the way the calendar on
/// the lodge-hall wall is laid out, so that the stated meeting and the fish fry can be seen to fall
/// in the same week.</para>
///
/// <para><b>The month is not asked for.</b> It comes from the issue, like the cover banner's date,
/// so a newsletter carried forward gets next month's grid with next month's Tuesday marked and
/// nobody re-answers anything. It also means the grid can never disagree with the cover about which
/// month this is.</para>
/// </summary>
public sealed class MonthCalendarDefinition : WidgetDefinition<MonthCalendarData>
{
    public override string TypeId => "monthCalendar";

    public override string DisplayName => "This month's calendar";

    public override string Description =>
        "The month on a grid, with the stated meeting already marked and room for what else is on.";

    public override string IconKey => "month-calendar";

    /// <summary>
    /// Page-wide by default. A seven-column grid in a narrow frame gives cells too small to read,
    /// and the committee should meet this widget at a size that works rather than have to discover
    /// the one it needs.
    /// </summary>
    public override SizePt DefaultSizePt => new(468f, 300f);

    public override IWidgetLayouter Layouter { get; } = new MonthCalendarLayouter();

    protected override JsonTypeInfo<MonthCalendarData> TypeInfo =>
        MonthCalendarJsonContext.Default.MonthCalendarData;

    /// <summary>
    /// Structure only, and no entries (PLAN.md §0, docs/M7-spec.md §8.3). The month, year and
    /// meeting rule are the OPEN DOCUMENT'S OWN — non-personal facts about the issue, which is what
    /// §8.3 allows a seed to carry.
    /// </summary>
    public override MonthCalendarData CreateEmpty(WidgetSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        return new MonthCalendarData
        {
            Month = seed.IssueMonth,
            Year = seed.IssueYear,
            MeetingRule = seed.MeetingRule,
        };
    }

    /// <summary>
    /// M75's hook, and this widget needs it as much as the cover banner does: the month lives in
    /// the document rather than in the payload, so a re-edit has to pick it up again. Without this,
    /// a calendar made in August and edited after the issue moved to September would keep drawing
    /// August — the exact disagreement the design exists to prevent.
    /// </summary>
    public override void SeedFromDocument(MonthCalendarData data, WidgetSeed seed)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(seed);

        data.Month = seed.IssueMonth;
        data.Year = seed.IssueYear;
        data.MeetingRule = seed.MeetingRule;
    }

    /// <summary>
    /// "14", not "the 14th" and not "Tuesday". The sentence names what to type rather than what was
    /// wrong with what they typed, which is the M42 standard for every message in a wizard.
    /// </summary>
    private static string? DayOfMonth(string value) =>
        int.TryParse(
            value,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int day) && day is >= 1 and <= 31
            ? null
            : "Type the day of the month as a number, such as 14.";

    protected override WizardDefinition BuildWizard() => new(
        DisplayName,
        "TrestleBoard already knows the month and marks the stated meeting. "
            + "We will add anything else that is on, one at a time.",
        [
            new FieldsStep<MonthCalendarData>(
                "What is this calendar called?",
                "The name prints above the grid. Leave it blank to print no heading at all.",
                [
                    (new WizardField(
                            "heading",
                            "Name of the calendar",
                            IsOptional: true,
                            ExampleText: "This month at the lodge"),
                        new WizardFieldBinding<MonthCalendarData>(
                            "heading", d => d.Heading, (d, v) => d.Heading = v)),
                ]),

            new RecordListStep<MonthCalendarData, CalendarEntry>(
                "What else is on",
                "One thing at a time. The stated meeting is already marked, so it does not need "
                    + "adding here.",
                d => d.Entries,
                () => new CalendarEntry(),
                [
                    // Free text with a validator rather than a number box: there is no whole-number
                    // field kind, and inventing one for a single question would be a new control
                    // for this audience to meet. The validator says what is wrong in a sentence.
                    (new WizardField(
                            "day",
                            "Which day of the month",
                            ExampleText: "14",
                            MaxLength: 2,
                            Validator: DayOfMonth),
                        new WizardFieldBinding<CalendarEntry>(
                            "day",
                            r => r.Day > 0 ? r.Day.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
                            (r, v) => r.Day = int.TryParse(
                                v, System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture, out int day) ? day : 0)),
                    (new WizardField(
                            "what",
                            "What is on",
                            HelpText: "Keep it short — it prints inside a small box.",
                            ExampleText: "Fish fry, 6pm"),
                        new WizardFieldBinding<CalendarEntry>(
                            "what", r => r.What, (r, v) => r.What = v)),
                ],
                allowReorder: false,
                rowLabel: r => r.IsBlank
                    ? "Nothing yet"
                    : $"{r.Day}: {r.What}"),
        ]);
}
