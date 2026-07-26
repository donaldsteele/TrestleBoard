using System.Text.Json.Serialization.Metadata;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Widgets.Builtins.EventCard;

/// <summary>
/// A boxed announcement for one event. Inserted with every field blank — an event box that survives
/// to print with a placeholder date or place is worse than a visibly empty one (PLAN.md §0,
/// docs/M7-spec.md §8.3).
/// </summary>
public sealed class EventCardDefinition : WidgetDefinition<EventCardData>
{
    public override string TypeId => "eventCard";

    public override string DisplayName => "Announcement box";

    public override string Description =>
        "A boxed announcement for one event — a picnic, a potluck, a special meeting.";

    public override string IconKey => "announcement";

    // Roughly the footprint of a small newspaper announcement: wide enough for a short heading and a
    // two- or three-sentence notice without wrapping every word, short enough that it reads as a box
    // beside body text rather than a full-width banner (docs/M7-spec.md §9.5 suggests ~240 × 120 pt).
    public override SizePt DefaultSizePt => new(240f, 120f);

    public override IWidgetLayouter Layouter { get; } = new EventCardLayouter();

    protected override JsonTypeInfo<EventCardData> TypeInfo =>
        EventCardJsonContext.Default.EventCardData;

    public override EventCardData CreateEmpty(WidgetSeed seed) => new();

    protected override WizardDefinition BuildWizard() => new(
        DisplayName,
        "We will fill in the title, then when and where, then what it says.",
        [
            new FieldsStep<EventCardData>(
                "What is this announcement for?",
                "This prints as the card's title.",
                [
                    (new WizardField(
                            "title",
                            "Title",
                            ExampleText: "Placeholder Lodge Picnic"),
                        new WizardFieldBinding<EventCardData>(
                            "title", d => d.Title, (d, v) => d.Title = v)),
                ]),
            new FieldsStep<EventCardData>(
                "When and where is it?",
                "Leave either blank if it is not settled yet.",
                [
                    (new WizardField(
                            "when",
                            "When",
                            IsOptional: true,
                            HelpText: "Leave this blank if the date is not settled yet.",
                            ExampleText: "Saturday, 6:00 pm"),
                        new WizardFieldBinding<EventCardData>(
                            "when",
                            d => d.WhenText ?? "",
                            (d, v) => d.WhenText = string.IsNullOrWhiteSpace(v) ? null : v)),
                    (new WizardField(
                            "where",
                            "Where",
                            IsOptional: true,
                            HelpText: "Leave this blank if the place is not settled yet.",
                            ExampleText: "Sample Lodge 000 hall"),
                        new WizardFieldBinding<EventCardData>(
                            "where",
                            d => d.WhereText ?? "",
                            (d, v) => d.WhereText = string.IsNullOrWhiteSpace(v) ? null : v)),
                ]),
            new TextStep<EventCardData>(
                "What should the announcement say?",
                "Write it the way it should be read in the newsletter.",
                new WizardField("body", "Announcement", WizardFieldKind.MultiLineText),
                new WizardFieldBinding<EventCardData>(
                    "body", d => d.BodyText, (d, v) => d.BodyText = v)),
        ]);
}
