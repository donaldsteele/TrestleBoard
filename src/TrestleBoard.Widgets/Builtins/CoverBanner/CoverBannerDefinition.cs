using System.Text.Json.Serialization.Metadata;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Widgets.Builtins.CoverBanner;

/// <summary>
/// The front-page banner. The one widget that reads the seed at insert (docs/M7-spec.md §8.3): the
/// lodge's own name and its own meeting rule are not "a person's data" in the PLAN.md §0 sense — they
/// are the document's own metadata, already on file, so copying them here saves a re-typing that would
/// otherwise fall on an elderly volunteer every single month.
/// </summary>
public sealed class CoverBannerDefinition : WidgetDefinition<CoverBannerData>
{
    public override string TypeId => "coverBanner";

    public override string DisplayName => "Cover heading";

    public override string Description =>
        "The banner at the top of the front page: lodge name, heading and this month's meeting date.";

    public override string IconKey => "coverBanner";

    public override SizePt DefaultSizePt => new(504f, 130f);

    public override IWidgetLayouter Layouter { get; } = new CoverBannerLayouter();

    protected override JsonTypeInfo<CoverBannerData> TypeInfo =>
        CoverBannerJsonContext.Default.CoverBannerData;

    public override CoverBannerData CreateEmpty(WidgetSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        // LodgeName and MeetingRule come from the OPEN DOCUMENT'S OWN metadata (the seed), never a
        // shipped default — that is what makes this a scaffolding copy, not a pre-populated fake
        // person (docs/M7-spec.md §8.3). The date and both times stay blank; nobody knows them yet.
        return new CoverBannerData
        {
            LodgeName = seed.LodgeName,
            MeetingRule = seed.MeetingRule,
        };
    }

    protected override WizardDefinition BuildWizard() => new(
        DisplayName,
        "A few lines for the top of the front page. Leave anything blank that does not apply this month.",
        [
            new FieldsStep<CoverBannerData>(
                "What's this banner for?",
                "This prints at the very top of the newsletter, above everything else.",
                [
                    (new WizardField(
                            "lodgeName",
                            "Lodge name",
                            ExampleText: "Placeholder Lodge No. 000"),
                        new WizardFieldBinding<CoverBannerData>(
                            "lodgeName", d => d.LodgeName, (d, v) => d.LodgeName = v)),
                    (new WizardField(
                            "headingText",
                            "Heading",
                            HelpText: "This prints exactly as you type it, capital letters and all.",
                            ExampleText: "STATED COMMUNICATION"),
                        new WizardFieldBinding<CoverBannerData>(
                            "headingText", d => d.HeadingText, (d, v) => d.HeadingText = v)),
                ]),
            new FieldsStep<CoverBannerData>(
                "When is this meeting?",
                "The rule below is only used to work out next month's date automatically; the date is what actually prints.",
                [
                    (new WizardField(
                            "meetingRule",
                            "How often does the lodge meet?",
                            WizardFieldKind.DayOfMonthRule,
                            IsOptional: true,
                            ExampleText: "1st Tuesday"),
                        new WizardFieldBinding<CoverBannerData>(
                            "meetingRule", d => d.MeetingRule, (d, v) => d.MeetingRule = v)),
                    (new WizardField(
                            "meetingDateText",
                            "This month's meeting date",
                            HelpText: "Type it exactly the way you want it printed.",
                            ExampleText: "July 7th"),
                        new WizardFieldBinding<CoverBannerData>(
                            "meetingDateText", d => d.MeetingDateText, (d, v) => d.MeetingDateText = v)),
                ]),
            new FieldsStep<CoverBannerData>(
                "Dinner and lodge times",
                "Leave either blank if it does not apply this month.",
                [
                    (new WizardField(
                            "dinnerTimeText",
                            "Dinner time",
                            WizardFieldKind.Time,
                            IsOptional: true,
                            ExampleText: "6:30"),
                        new WizardFieldBinding<CoverBannerData>(
                            "dinnerTimeText",
                            d => d.DinnerTimeText ?? "",
                            (d, v) => d.DinnerTimeText = string.IsNullOrWhiteSpace(v) ? null : v)),
                    (new WizardField(
                            "workTimeText",
                            "Lodge opens",
                            WizardFieldKind.Time,
                            IsOptional: true,
                            ExampleText: "7:30"),
                        new WizardFieldBinding<CoverBannerData>(
                            "workTimeText",
                            d => d.WorkTimeText ?? "",
                            (d, v) => d.WorkTimeText = string.IsNullOrWhiteSpace(v) ? null : v)),
                ]),
        ]);
}
