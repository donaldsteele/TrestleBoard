using System.Text.Json;
using System.Text.Json.Nodes;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Widgets;

/// <summary>
/// Everything the app knows about one kind of widget (PLAN.md §5, docs/M7-spec.md §1.1): its id,
/// how it is described to the user, its data type and codec, its wizard, and its layouter.
/// </summary>
public interface IWidgetDefinition
{
    /// <summary>Stable camelCase id written into <c>WidgetBlock.WidgetType</c>. Never renamed.</summary>
    string TypeId { get; }

    /// <summary>Plain-language menu/gallery label, e.g. "Lodge officers".</summary>
    string DisplayName { get; }

    /// <summary>One sentence for the gallery and the wizard's first screen.</summary>
    string Description { get; }

    /// <summary>Key into the App's icon set; icons are NEVER the only label (PLAN.md §6).</summary>
    string IconKey { get; }

    Type DataType { get; }

    /// <summary>Highest dataVersion this build writes; blocks carrying more are read-only (§3).</summary>
    int CurrentDataVersion { get; }

    /// <summary>Frame size used at insert, before fit-to-contents (§8).</summary>
    SizePt DefaultSizePt { get; }

    WidgetStyleDefaults StyleDefaults { get; }

    WizardDefinition Wizard { get; }

    IWidgetLayouter Layouter { get; }

    /// <summary>
    /// Fresh, EMPTY data. PLAN.md §0 is absolute: never a person's name, phone number or date.
    /// Non-personal document facts may be copied from <paramref name="seed"/> (§8.3).
    /// </summary>
    object CreateEmptyData(WidgetSeed seed);

    /// <summary>
    /// M75: fills in the answers a wizard collects that do NOT live in the widget's saved payload.
    ///
    /// <para>Only the cover banner has any: the issue's month and year belong to
    /// <c>DocumentMetadata</c>, not to the banner, but the owner's ruling is that the ask lives in
    /// the wizard the user already meets rather than in a second properties dialog nobody finds.
    /// <see cref="CreateEmptyData"/> covers a brand-new widget; this covers the re-edit path, where
    /// the payload is read back from the block and the seed would otherwise be dropped on the
    /// floor. A no-op for every other widget.</para>
    /// </summary>
    void SeedAnswersFromDocument(object typedData, WidgetSeed seed);

    /// <summary>
    /// Raw JSON to typed POCO, migrating first (§3). False for malformed data or a dataVersion this
    /// build does not understand — a damaged widget degrades to a placeholder, never to a crash.
    /// </summary>
    bool TryReadData(JsonElement? data, int dataVersion, out object typedData);

    JsonElement WriteData(object typedData);

    /// <summary>
    /// Upgrades raw JSON one version step. Operates on <see cref="JsonNode"/>, not the POCO, so an
    /// old shape never needs a live CLR type (the same rule Core's migrations follow).
    /// </summary>
    bool TryMigrateStep(JsonNode data, int fromVersion, out JsonNode upgraded, out int toVersion);
}
