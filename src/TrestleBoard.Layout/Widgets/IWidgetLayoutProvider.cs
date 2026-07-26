using System.Text.Json;

namespace TrestleBoard.Layout.Widgets;

/// <summary>Everything a layouter needs, with the payload still untyped (docs/M7-spec.md §4.1).</summary>
public sealed record WidgetLayoutRequest(
    string WidgetTypeId,
    int DataVersion,
    JsonElement? Data,
    float WidthPt,
    WidgetStyleContext Style,
    WidgetTextShaper Shaper);

/// <summary>
/// The seam that keeps <c>TrestleBoard.Widgets</c> a leaf: Rendering is handed one of these at
/// construction instead of referencing the widget layer (docs/M7-spec.md §0.1).
/// </summary>
public interface IWidgetLayoutProvider
{
    /// <summary>
    /// False means "unknown widget type, or its dataVersion is newer than this build" — the caller
    /// then draws the neutral placeholder. Must NEVER throw: a damaged widget cannot be allowed to
    /// take the newsletter down with it.
    /// </summary>
    bool TryLayout(WidgetLayoutRequest request, out WidgetDrawList drawList);

    /// <summary>
    /// The widget's own fallback appearance, which the caller layers the document's styles over.
    /// Rendering cannot see <c>WidgetStyleDefaults</c> (that type lives in the leaf project), so the
    /// defaults arrive already shaped as a context (docs/M7-spec.md §9).
    /// </summary>
    bool TryGetStyleDefaults(string widgetTypeId, out WidgetStyleContext defaults);
}
