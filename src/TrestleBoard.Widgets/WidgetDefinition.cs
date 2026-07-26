using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Widgets;

/// <summary>
/// Typed base for every built-in widget: it owns the codec plumbing, the migration loop and the
/// single <c>object</c>-to-<typeparamref name="TData"/> cast, so a concrete widget is nothing but
/// its data shape, its wizard and its layouter (docs/M7-spec.md §1.1).
/// </summary>
public abstract class WidgetDefinition<TData> : IWidgetDefinition
    where TData : class, new()
{
    private WizardDefinition? _wizard;

    public abstract string TypeId { get; }

    public abstract string DisplayName { get; }

    public abstract string Description { get; }

    public abstract string IconKey { get; }

    public abstract SizePt DefaultSizePt { get; }

    public abstract IWidgetLayouter Layouter { get; }

    public Type DataType => typeof(TData);

    public virtual int CurrentDataVersion => 1;

    public virtual WidgetStyleDefaults StyleDefaults => WidgetStyleDefaults.Standard;

    /// <summary>Built once; a wizard definition is immutable and shared across sessions.</summary>
    public WizardDefinition Wizard => _wizard ??= BuildWizard();

    protected abstract JsonTypeInfo<TData> TypeInfo { get; }

    protected abstract WizardDefinition BuildWizard();

    /// <summary>Fresh, EMPTY data (PLAN.md §0 — never pre-populated with people).</summary>
    public abstract TData CreateEmpty(WidgetSeed seed);

    object IWidgetDefinition.CreateEmptyData(WidgetSeed seed) => CreateEmpty(seed);

    public bool TryReadData(JsonElement? data, int dataVersion, out object typedData)
    {
        typedData = null!;
        if (dataVersion > CurrentDataVersion)
        {
            // Never a best-effort read of a future shape: a silently dropped column is worse than a
            // grey box (docs/M7-spec.md §3.3).
            return false;
        }

        if (data is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        JsonElement current = element;
        int version = dataVersion;
        while (version < CurrentDataVersion)
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(current.GetRawText());
            }
            catch (JsonException)
            {
                return false;
            }

            if (node is null || !TryMigrateStep(node, version, out JsonNode upgraded, out int next) || next <= version)
            {
                return false;
            }

            current = JsonSerializer.SerializeToElement(upgraded, WidgetData.Options);
            version = next;
        }

        TData? value = WidgetData.Read(current, TypeInfo);
        if (value is null)
        {
            return false;
        }

        typedData = value;
        return true;
    }

    public JsonElement WriteData(object typedData)
    {
        ArgumentNullException.ThrowIfNull(typedData);
        return WidgetData.Write((TData)typedData, TypeInfo);
    }

    /// <summary>No version history yet; overridden the first time a data shape changes.</summary>
    public virtual bool TryMigrateStep(JsonNode data, int fromVersion, out JsonNode upgraded, out int toVersion)
    {
        upgraded = data;
        toVersion = fromVersion;
        return false;
    }
}
