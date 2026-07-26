using TrestleBoard.Layout.Widgets;

namespace TrestleBoard.Widgets;

/// <summary>Typed payload plus everything needed to lay it out (docs/M7-spec.md §4.2).</summary>
public sealed record WidgetLayoutContext(
    object Data,
    float WidthPt,
    WidgetStyleContext Style,
    WidgetTextShaper Shaper);

public interface IWidgetLayouter
{
    /// <summary>
    /// Lays the widget out at the given width and reports its natural height. Pure: no I/O, no
    /// clock, no culture, no statics — that purity is the cross-OS determinism guarantee (§4.5).
    /// </summary>
    WidgetDrawList Layout(WidgetLayoutContext context);
}
