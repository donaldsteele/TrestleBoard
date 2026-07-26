using System.Text.Json;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;

namespace TrestleBoard.Widgets;

/// <summary>
/// The registry seen through the two Layout-declared seams (docs/M7-spec.md §0.1), so Rendering and
/// Editing get widgets without either project referencing this one.
/// </summary>
public sealed class WidgetLayoutProvider : IWidgetLayoutProvider, IWidgetCatalog
{
    private readonly WidgetRegistry _registry;

    public WidgetLayoutProvider(WidgetRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public static WidgetLayoutProvider CreateDefault() => new(WidgetRegistry.CreateDefault());

    public WidgetRegistry Registry => _registry;

    /// <summary>
    /// Never throws. An unknown type, a newer dataVersion, malformed data or a layouter that faults
    /// all degrade to false, and the caller paints the neutral placeholder (§2.1/§3.3).
    /// </summary>
    public bool TryLayout(WidgetLayoutRequest request, out WidgetDrawList drawList)
    {
        ArgumentNullException.ThrowIfNull(request);
        drawList = null!;
        if (!_registry.TryGet(request.WidgetTypeId, out IWidgetDefinition? definition))
        {
            return false;
        }

        if (!definition.TryReadData(request.Data, request.DataVersion, out object data))
        {
            return false;
        }

        try
        {
            drawList = definition.Layouter.Layout(
                new WidgetLayoutContext(data, request.WidthPt, request.Style, request.Shaper));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            return false;
        }

        return true;
    }

    public bool TryGetStyleDefaults(string widgetTypeId, out WidgetStyleContext defaults)
    {
        if (!_registry.TryGet(widgetTypeId, out IWidgetDefinition? definition))
        {
            defaults = null!;
            return false;
        }

        defaults = ToContext(definition.StyleDefaults);
        return true;
    }

    /// <summary>A widget's defaults promoted to a context, with no document styles applied yet.</summary>
    public static WidgetStyleContext ToContext(WidgetStyleDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        return new WidgetStyleContext(
            defaults.Display,
            defaults.Heading,
            defaults.Body,
            defaults.Emphasis,
            defaults.Small,
            defaults.LineSpacing,
            defaults.RuleArgb,
            defaults.RuleWidthPt,
            defaults.FillArgb,
            defaults.StrokeArgb,
            defaults.StrokeWidthPt,
            defaults.PaddingPt,
            new Dictionary<string, uint>(StringComparer.Ordinal));
    }

    public bool TryGetInfo(string typeId, out WidgetInfo info)
    {
        if (!_registry.TryGet(typeId, out IWidgetDefinition? definition))
        {
            info = default;
            return false;
        }

        info = new WidgetInfo(
            definition.TypeId,
            definition.DisplayName,
            definition.IconKey,
            definition.CurrentDataVersion,
            definition.DefaultSizePt);
        return true;
    }

    public JsonElement CreateEmptyData(string typeId, WidgetSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        if (!_registry.TryGet(typeId, out IWidgetDefinition? definition))
        {
            throw new KeyNotFoundException($"Unknown widget type: {typeId}");
        }

        return definition.WriteData(definition.CreateEmptyData(seed));
    }

    /// <summary>Menu/gallery entries, in registration order.</summary>
    public IReadOnlyList<WidgetInfo> ListWidgets()
    {
        var list = new List<WidgetInfo>(_registry.All.Count);
        foreach (IWidgetDefinition definition in _registry.All)
        {
            list.Add(new WidgetInfo(
                definition.TypeId,
                definition.DisplayName,
                definition.IconKey,
                definition.CurrentDataVersion,
                definition.DefaultSizePt));
        }

        return list;
    }

    /// <summary>Seed built from the open document's own facts — never a shipped default (§8.3).</summary>
    public static WidgetSeed SeedFrom(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new WidgetSeed(
            document.Metadata.LodgeName,
            document.Metadata.IssueMonth,
            document.Metadata.IssueYear,
            document.Metadata.MeetingRule);
    }
}
