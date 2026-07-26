using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Widgets;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// Shared, FICTIONAL test scaffolding (PLAN.md §0 — no real member ever appears in the repo).
/// Frozen during the parallel widget phase: each widget builds its own data in its own test file
/// and only borrows the shaper, the style context and these placeholder people from here.
/// </summary>
public static class WidgetTestData
{
    /// <summary>Placeholder people. Names are obviously fake; numbers are all in the 555-01xx block.</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "A. Placeholder", "B. Sample", "C. Example", "D. Fictional", "E. Stand-in", "F. Nobody",
        "G. Placeholder", "H. Sample", "I. Example", "J. Fictional", "K. Stand-in", "L. Nobody",
    ];

    public static readonly IReadOnlyList<string> Phones =
    [
        "555-0100", "555-0101", "555-0102", "555-0103", "555-0104", "555-0105",
        "555-0106", "555-0107", "555-0108", "555-0109", "555-0110", "555-0111",
    ];

    public const string LodgeName = "Placeholder Lodge No. 000";

    public const string SampleLodge = "Sample Lodge 000";

    public static WidgetSeed Seed { get; } = new(LodgeName, 7, 2026, "1st Tuesday");

    /// <summary>One store per process; the shaper is stateless apart from a metrics cache.</summary>
    public static FontStore Fonts { get; } = BundledFonts.CreateDefaultStore();

    public static WidgetTextShaper CreateShaper() => new(Fonts);

    /// <summary>
    /// The widget's own defaults promoted straight to a style context — the "no document styles"
    /// baseline that the golden draw-list dumps are measured against.
    /// </summary>
    public static WidgetStyleContext CreateStyle(WidgetStyleDefaults? defaults = null)
    {
        WidgetStyleDefaults d = defaults ?? WidgetStyleDefaults.Standard;
        return new WidgetStyleContext(
            d.Display, d.Heading, d.Body, d.Emphasis, d.Small,
            d.LineSpacing, d.RuleArgb, d.RuleWidthPt,
            d.FillArgb, d.StrokeArgb, d.StrokeWidthPt, d.PaddingPt,
            new Dictionary<string, uint>(StringComparer.Ordinal));
    }

    /// <summary>Lays a widget out at a width, using its own style defaults.</summary>
    public static WidgetDrawList LayOut(IWidgetDefinition definition, object data, float widthPt) =>
        (definition ?? throw new ArgumentNullException(nameof(definition))).Layouter.Layout(
            new WidgetLayoutContext(data, widthPt, CreateStyle(definition.StyleDefaults), CreateShaper()));
}
