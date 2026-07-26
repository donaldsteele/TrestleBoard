using System.Text.Json.Serialization.Metadata;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Widgets.Builtins.CommitteeList;

/// <summary>
/// The standing-committees list. Inserted with the default heading and notice text and ZERO
/// committees — structure only, never a placeholder person (PLAN.md §0, docs/M7-spec.md §8.3).
/// </summary>
public sealed class CommitteeListDefinition : WidgetDefinition<CommitteeListData>
{
    public override string TypeId => "committeeList";

    public override string DisplayName => "Committees";

    public override string Description =>
        "The lodge's standing committees and who serves on each.";

    public override string IconKey => "committees";

    public override SizePt DefaultSizePt => new(330f, 150f);

    public override IWidgetLayouter Layouter { get; } = new CommitteeListLayouter();

    protected override JsonTypeInfo<CommitteeListData> TypeInfo =>
        CommitteeListJsonContext.Default.CommitteeListData;

    public override CommitteeListData CreateEmpty(WidgetSeed seed) => new();

    protected override WizardDefinition BuildWizard() => new(
        DisplayName,
        "We will list each committee and who is on it, one at a time.",
        [
            new FieldsStep<CommitteeListData>(
                "What should this section be called?",
                "The heading prints above the list; the notice prints just below it.",
                [
                    (new WizardField(
                            "heading",
                            "Heading",
                            ExampleText: "Committees"),
                        new WizardFieldBinding<CommitteeListData>(
                            "heading", d => d.Heading, (d, v) => d.Heading = v)),
                    (new WizardField(
                            "noticeText",
                            "Notice",
                            IsOptional: true,
                            HelpText: "Leave this blank to print no notice at all.",
                            ExampleText: "Please notify the Worshipful Master to be added or removed."),
                        new WizardFieldBinding<CommitteeListData>(
                            "noticeText",
                            d => d.NoticeText ?? "",
                            (d, v) => d.NoticeText = string.IsNullOrWhiteSpace(v) ? null : v)),
                ]),
            new RecordListStep<CommitteeListData, CommitteeEntry>(
                "Committees",
                "Type the committee's name, then its members — one name per line.",
                d => d.Committees,
                () => new CommitteeEntry(),
                [
                    (new WizardField(
                            "name",
                            "Committee name",
                            ExampleText: "Building"),
                        new WizardFieldBinding<CommitteeEntry>(
                            "name", r => r.Name, (r, v) => r.Name = v)),
                    (new WizardField(
                            "members",
                            "Members",
                            WizardFieldKind.MultiLineText,
                            IsOptional: true,
                            HelpText: "One name per line. Leave this blank if the committee has no members yet.",
                            ExampleText: "A. Placeholder"),
                        new WizardFieldBinding<CommitteeEntry>(
                            "members",
                            r => string.Join('\n', r.Members),
                            (r, v) => r.Members = SplitMembers(v))),
                    (new WizardField(
                            "note",
                            "Note",
                            IsOptional: true,
                            HelpText: "A short free-text tail, like \"and team\".",
                            ExampleText: "and team"),
                        new WizardFieldBinding<CommitteeEntry>(
                            "note",
                            r => r.Note ?? "",
                            (r, v) => r.Note = string.IsNullOrWhiteSpace(v) ? null : v)),
                ],
                allowReorder: true,
                rowLabel: r => r.Name.Length == 0 ? "Committee" : r.Name),
        ]);

    /// <summary>
    /// One member per line; blank lines are dropped so a stray Enter never becomes a nameless
    /// member (docs/M7-spec.md §9.3 — members are entered one per line, never comma-separated).
    /// </summary>
    private static List<string> SplitMembers(string value) =>
        [.. value.Split('\n').Select(line => line.Trim('\r', ' ', '\t')).Where(line => line.Length > 0)];
}
