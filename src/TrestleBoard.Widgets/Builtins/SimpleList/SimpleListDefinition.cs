using System.Text.Json.Serialization.Metadata;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Widgets.Builtins.SimpleList;

/// <summary>
/// A list of the user's own (PLAN.md §11 M60) — the seventh widget, and the only one that does not
/// know what it is for.
///
/// <para>Content that is table-shaped but is not one of the six lists TrestleBoard fills in —
/// Eastern Star news, a degree schedule, a dinner menu — was being hand-typed into free text frames
/// with tabs and spaces, losing re-edit, carry-forward and alignment. Hand-aligning a table with
/// spaces is exactly the fine-motor, spatial task §6 exists to avoid.</para>
///
/// <para>The officers wizard already proved this audience can build a table one question at a time.
/// This generalises that proof to a table nobody wrote a wizard for.</para>
/// </summary>
public sealed class SimpleListDefinition : WidgetDefinition<SimpleListData>
{
    public override string TypeId => "simpleList";

    public override string DisplayName => "A list of my own";

    public override string Description =>
        "Any list of your own with up to three columns, such as a degree schedule or a dinner menu.";

    public override string IconKey => "simple-list";

    public override SizePt DefaultSizePt => new(330f, 160f);

    public override IWidgetLayouter Layouter { get; } = new SimpleListLayouter();

    protected override JsonTypeInfo<SimpleListData> TypeInfo =>
        SimpleListJsonContext.Default.SimpleListData;

    /// <summary>
    /// Structure only, and not one row (PLAN.md §0, docs/M7-spec.md §8.3). An inserted list is
    /// empty and says so on screen until somebody fills it in.
    /// </summary>
    public override SimpleListData CreateEmpty(WidgetSeed seed) => new();

    protected override WizardDefinition BuildWizard() => new(
        DisplayName,
        "We will name the list, name its columns, and then add the rows one at a time.",
        [
            new FieldsStep<SimpleListData>(
                "What is this list called?",
                "The name prints above the list. Leave it blank to print no heading at all.",
                [
                    (new WizardField(
                            "heading",
                            "Name of the list",
                            IsOptional: true,
                            ExampleText: "Degree schedule"),
                        new WizardFieldBinding<SimpleListData>(
                            "heading", d => d.Heading, (d, v) => d.Heading = v)),
                ]),

            // Three boxes and no way to ask for a fourth: the cap is on the question rather than
            // on the answer, which is what keeps "one question at a time" true for the rows below.
            new FieldsStep<SimpleListData>(
                "What are the columns called?",
                "A list needs at least one column. Fill in the second and third only if you want "
                    + "them — two columns is usually plenty.",
                [
                    (new WizardField(
                            "firstColumn",
                            "First column",
                            ExampleText: "Date"),
                        new WizardFieldBinding<SimpleListData>(
                            "firstColumn", d => d.FirstColumn, (d, v) => d.FirstColumn = v)),
                    (new WizardField(
                            "secondColumn",
                            "Second column",
                            IsOptional: true,
                            HelpText: "Leave blank for a list with one column.",
                            ExampleText: "Degree"),
                        new WizardFieldBinding<SimpleListData>(
                            "secondColumn",
                            d => d.SecondColumn ?? "",
                            (d, v) => d.SecondColumn = Blank(v))),
                    (new WizardField(
                            "thirdColumn",
                            "Third column",
                            IsOptional: true,
                            HelpText: "Leave blank for a list with two columns.",
                            ExampleText: "Who is working it"),
                        new WizardFieldBinding<SimpleListData>(
                            "thirdColumn",
                            d => d.ThirdColumn ?? "",
                            (d, v) => d.ThirdColumn = Blank(v))),
                ]),

            new RecordListStep<SimpleListData, SimpleListRow>(
                "The rows",
                "One row at a time. Leave a box empty if that row has nothing for that column.",
                d => d.Rows,
                () => new SimpleListRow(),
                [
                    (new WizardField("first", "First column", ExampleText: "14 September"),
                        new WizardFieldBinding<SimpleListRow>(
                            "first", r => r.First, (r, v) => r.First = v)),
                    (new WizardField("second", "Second column", IsOptional: true, ExampleText: "Entered Apprentice"),
                        new WizardFieldBinding<SimpleListRow>(
                            "second", r => r.Second ?? "", (r, v) => r.Second = Blank(v))),
                    (new WizardField("third", "Third column", IsOptional: true, ExampleText: "The degree team"),
                        new WizardFieldBinding<SimpleListRow>(
                            "third", r => r.Third ?? "", (r, v) => r.Third = Blank(v))),
                ],
                allowReorder: true,
                rowLabel: r => r.First.Length == 0 ? "Row" : r.First),
        ]);

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
