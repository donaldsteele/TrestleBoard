using System.Text.Json.Serialization.Metadata;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Widgets.Builtins.DistrictCalendar;

/// <summary>
/// The district trestle board: the recurring meeting days of the other lodges in the district, the
/// upcoming dated events, or both — one widget, chosen by mode, not two (docs/M7-spec.md §9.4).
/// Inserted with the heading in place and zero lodges, zero events (PLAN.md §0, §8.3).
/// </summary>
public sealed class DistrictCalendarDefinition : WidgetDefinition<DistrictCalendarData>
{
    private const string ModeMeetingDays = "meetingDays";
    private const string ModeEvents = "events";
    private const string ModeBoth = "both";

    public override string TypeId => "districtCalendar";

    public override string DisplayName => "District calendar";

    public override string Description =>
        "The other lodges' meeting days, upcoming district events, or both.";

    public override string IconKey => "districtCalendar";

    // Suits a two-column table (lodge name + meeting day) at a handful of rows, with room to grow
    // for the events list when Mode = Both (docs/M7-spec.md §9.4 leaves the exact figure open).
    public override SizePt DefaultSizePt => new(240f, 200f);

    public override IWidgetLayouter Layouter { get; } = new DistrictCalendarLayouter();

    protected override JsonTypeInfo<DistrictCalendarData> TypeInfo =>
        DistrictCalendarJsonContext.Default.DistrictCalendarData;

    public override DistrictCalendarData CreateEmpty(WidgetSeed seed) => new();

    protected override WizardDefinition BuildWizard() => new(
        DisplayName,
        "We will set up the district's meeting days, its upcoming events, or both.",
        [
            new FieldsStep<DistrictCalendarData>(
                "What should this be called, and what should it show?",
                "Pick the table, the events, or both — you can change this later.",
                [
                    (new WizardField(
                            "heading",
                            "Heading",
                            ExampleText: "22nd District"),
                        new WizardFieldBinding<DistrictCalendarData>(
                            "heading", d => d.Heading, (d, v) => d.Heading = v)),
                    (new WizardField(
                            "mode",
                            "What should this show?",
                            WizardFieldKind.Choice,
                            Choices:
                            [
                                new WizardChoice(ModeMeetingDays, "Just the meeting-days table"),
                                new WizardChoice(ModeEvents, "Just the dated events"),
                                new WizardChoice(ModeBoth, "Both"),
                            ]),
                        new WizardFieldBinding<DistrictCalendarData>(
                            "mode", d => ModeToValue(d.Mode), (d, v) => d.Mode = ValueToMode(v))),
                ]),
            new RecordListStep<DistrictCalendarData, DistrictLodgeEntry>(
                "Other lodges in the district",
                "Type each lodge's name and the day of the month it meets, like \"1st Tuesday\".",
                d => d.Lodges,
                () => new DistrictLodgeEntry(),
                [
                    (new WizardField(
                            "lodgeName",
                            "Lodge name",
                            ExampleText: "Sample Lodge 000"),
                        new WizardFieldBinding<DistrictLodgeEntry>(
                            "lodgeName", r => r.LodgeName, (r, v) => r.LodgeName = v)),
                    (new WizardField(
                            "meetingRule",
                            "Meeting day",
                            WizardFieldKind.DayOfMonthRule,
                            ExampleText: "1st Tuesday"),
                        new WizardFieldBinding<DistrictLodgeEntry>(
                            "meetingRule", r => r.MeetingRule, (r, v) => r.MeetingRule = v)),
                ],
                isActive: d => d.Mode is DistrictCalendarMode.MeetingDays or DistrictCalendarMode.Both),
            new RecordListStep<DistrictCalendarData, DistrictEventEntry>(
                "Coming up",
                "Type the date exactly the way you want it printed, like \"July 12\".",
                d => d.Events,
                () => new DistrictEventEntry(),
                [
                    (new WizardField(
                            "dateText",
                            "Date",
                            ExampleText: "July 12"),
                        new WizardFieldBinding<DistrictEventEntry>(
                            "dateText", r => r.DateText, (r, v) => r.DateText = v)),
                    (new WizardField(
                            "hostText",
                            "Hosted by",
                            IsOptional: true,
                            ExampleText: "Sample Lodge 000"),
                        new WizardFieldBinding<DistrictEventEntry>(
                            "hostText",
                            r => r.HostText ?? "",
                            (r, v) => r.HostText = string.IsNullOrWhiteSpace(v) ? null : v)),
                    (new WizardField(
                            "description",
                            "What is happening",
                            ExampleText: "Masters and Wardens meeting"),
                        new WizardFieldBinding<DistrictEventEntry>(
                            "description", r => r.Description, (r, v) => r.Description = v)),
                    (new WizardField(
                            "timesText",
                            "Times",
                            IsOptional: true,
                            ExampleText: "Meal 6:30, work 7:30"),
                        new WizardFieldBinding<DistrictEventEntry>(
                            "timesText",
                            r => r.TimesText ?? "",
                            (r, v) => r.TimesText = string.IsNullOrWhiteSpace(v) ? null : v)),
                ],
                allowReorder: true,
                isActive: d => d.Mode is DistrictCalendarMode.Events or DistrictCalendarMode.Both),
        ]);

    private static string ModeToValue(DistrictCalendarMode mode) => mode switch
    {
        DistrictCalendarMode.MeetingDays => ModeMeetingDays,
        DistrictCalendarMode.Events => ModeEvents,
        _ => ModeBoth,
    };

    private static DistrictCalendarMode ValueToMode(string value) => value switch
    {
        ModeEvents => DistrictCalendarMode.Events,
        ModeBoth => DistrictCalendarMode.Both,
        _ => DistrictCalendarMode.MeetingDays,
    };
}
