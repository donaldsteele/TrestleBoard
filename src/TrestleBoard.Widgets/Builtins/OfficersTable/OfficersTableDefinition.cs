using System.Text.Json.Serialization.Metadata;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Widgets.Builtins.OfficersTable;

/// <summary>
/// The every-issue officers table. Inserted with the twelve office names already in place and every
/// person blank — positions are structure, people are not (PLAN.md §0, docs/M7-spec.md §8.3).
/// </summary>
public sealed class OfficersTableDefinition : WidgetDefinition<OfficersTableData>
{
    public override string TypeId => "officersTable";

    public override string DisplayName => "Lodge officers";

    public override string Description =>
        "The table of this year's officers, their names and phone numbers.";

    public override string IconKey => "officers";

    public override SizePt DefaultSizePt => new(240f, 190f);

    public override IWidgetLayouter Layouter { get; } = new OfficersTableLayouter();

    protected override JsonTypeInfo<OfficersTableData> TypeInfo =>
        OfficersTableJsonContext.Default.OfficersTableData;

    public override OfficersTableData CreateEmpty(WidgetSeed seed)
    {
        var data = new OfficersTableData();
        foreach (string position in OfficersTableData.StandardPositions)
        {
            data.Officers.Add(new OfficerEntry { Position = position });
        }

        return data;
    }

    protected override WizardDefinition BuildWizard() => new(
        DisplayName,
        "We will go through the officers one at a time. Leave an office blank if nobody holds it.",
        [
            new FieldsStep<OfficersTableData>(
                "What should this table be called?",
                "This heading prints above the table.",
                [
                    (new WizardField(
                            "heading",
                            "Heading",
                            ExampleText: "Lodge Officers"),
                        new WizardFieldBinding<OfficersTableData>(
                            "heading", d => d.Heading, (d, v) => d.Heading = v)),
                ]),
            new RecordListStep<OfficersTableData, OfficerEntry>(
                "Officers",
                "Type the brother's name and phone number. Leave both blank if the office is vacant.",
                d => d.Officers,
                () => new OfficerEntry(),
                [
                    (new WizardField(
                            "name",
                            "Name",
                            IsOptional: true,
                            HelpText: "Leave this blank if nobody holds this office.",
                            ExampleText: "A. Placeholder"),
                        new WizardFieldBinding<OfficerEntry>(
                            "name", r => r.Name, (r, v) => r.Name = v)),
                    (new WizardField(
                            "phone",
                            "Phone number",
                            WizardFieldKind.Phone,
                            IsOptional: true,
                            ExampleText: "555-0100"),
                        new WizardFieldBinding<OfficerEntry>(
                            "phone",
                            r => r.Phone ?? "",
                            (r, v) => r.Phone = string.IsNullOrWhiteSpace(v) ? null : v)),
                ],
                fixedRows: OfficersTableData.StandardPositions,
                pagination: WizardListPagination.OneRowPerScreen),
        ]);
}
