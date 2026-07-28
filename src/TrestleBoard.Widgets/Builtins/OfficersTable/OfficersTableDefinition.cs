using System.Text.Json.Nodes;
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

    /// <summary>
    /// 2 since M19: the table gained where-it-came-from fields, purely additive. A v2 payload opened
    /// in a pre-M19 build is move/resize/delete-only, which is designed behaviour rather than an
    /// accident — the same rollout note M13 wrote for the birthday list.
    /// </summary>
    public override int CurrentDataVersion => 2;

    protected override JsonTypeInfo<OfficersTableData> TypeInfo =>
        OfficersTableJsonContext.Default.OfficersTableData;

    /// <summary>
    /// Twelve offices and nobody in them — and, as PLAN.md §5's projection rule requires, empty with
    /// no reference to the address book. The roster reaches this widget only through
    /// <see cref="Roster.OfficersRosterProjection"/>, which takes the member list as a parameter and
    /// is invoked by a user-confirmed action. Never from here, and never from <see cref="WidgetSeed"/>.
    /// </summary>
    public override OfficersTableData CreateEmpty(WidgetSeed seed)
    {
        var data = new OfficersTableData();
        foreach (string position in OfficersTableData.StandardPositions)
        {
            data.Officers.Add(new OfficerEntry { Position = position });
        }

        return data;
    }

    /// <summary>
    /// v1 → v2. Everything new is optional, so the whole upgrade is "say out loud that this table
    /// was filled in by hand", which is what every v1 table was. Rows need nothing: a row with no
    /// <c>memberId</c> is a typed row by definition, and that is exactly what the projection leaves
    /// alone (PLAN.md §11 M19).
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

        data["source"] = OfficersTableSource.Manual;
        toVersion = 2;
        return true;
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
                    // M13: a pick rather than a typing job. The wizard stays fourteen screens — that
                    // was a deliberate M7 design — but each screen becomes "choose him", and free
                    // typing still works for a brother who is not in the address book.
                    (new WizardField(
                            "name",
                            "Name",
                            WizardFieldKind.Person,
                            IsOptional: true,
                            HelpText: "Start typing, and names from your address book appear. "
                                + "You can type a name that is not in it.",
                            ExampleText: "A. Placeholder"),
                        new WizardFieldBinding<OfficerEntry>(
                            "name", r => r.Name, SetName)),
                    (new WizardField(
                            "phone",
                            "Phone number",
                            WizardFieldKind.Phone,
                            IsOptional: true,
                            ExampleText: "555-0100"),
                        new WizardFieldBinding<OfficerEntry>(
                            "phone",
                            r => r.Phone ?? "",
                            SetPhone)),
                ],
                fixedRows: OfficersTableData.StandardPositions,
                pagination: WizardListPagination.OneRowPerScreen),
        ]);

    /// <summary>
    /// Provenance is captured here, in the setter both editors already share (M13's pattern, M19's
    /// use of it): the step wizard and <c>WidgetGridWindow</c> drive the same field bindings, so one
    /// line per bound field marks a row as the user's without either window learning anything new.
    ///
    /// The equality guard is load-bearing, not tidiness. Both windows write the box's value back on
    /// LostFocus, so tabbing through a filled-in table without typing a character would otherwise
    /// mark every row manual and freeze the whole table against every future re-sync.
    /// </summary>
    private static void SetName(OfficerEntry row, string value)
    {
        if (!string.Equals(row.Name, value, StringComparison.Ordinal))
        {
            row.Name = value;
            row.IsManual = true;
        }
    }

    private static void SetPhone(OfficerEntry row, string value)
    {
        string? phone = string.IsNullOrWhiteSpace(value) ? null : value;
        if (!string.Equals(row.Phone, phone, StringComparison.Ordinal))
        {
            row.Phone = phone;
            row.IsManual = true;
        }
    }
}
