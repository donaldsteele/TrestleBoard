using System.Text.Json;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// The document's own styles must WIN over a widget's defaults (docs/M7-spec.md §9), and a widget
/// must actually paint with what it is handed. An earlier version of these tests only ever exercised
/// widgets with no style refs, so they would have passed against a resolver that returned its input.
/// </summary>
public sealed class WidgetStyleResolverTests
{
    private static readonly WidgetLayoutProvider Widgets = WidgetLayoutProvider.CreateDefault();

    private const uint DocumentRuleArgb = 0xFF1166CC;
    private const float DocumentRuleWidthPt = 2.5f;

    [Fact]
    public void ADocumentTableStyleOverridesTheWidgetsOwnDefaults()
    {
        (Document document, WidgetBlock widget) = StyledDocument();
        Assert.True(Widgets.TryGetStyleDefaults(widget.WidgetType, out WidgetStyleContext defaults));

        WidgetStyleContext resolved = WidgetStyleResolver.Resolve(document, widget, defaults);

        // The values the document names, not the ones the widget ships with.
        Assert.NotEqual(defaults.RuleArgb, resolved.RuleArgb);
        Assert.Equal(DocumentRuleArgb, resolved.RuleArgb);
        Assert.Equal(DocumentRuleWidthPt, resolved.RuleWidthPt);
        Assert.Equal(BundledFonts.SansFamily, resolved.Heading.FontFamily);
        Assert.Equal(15f, resolved.Heading.SizePt);
        Assert.Equal(BundledFonts.DisplayFamily, resolved.Body.FontFamily);

        // Emphasis tracks the resolved body face rather than staying on the widget's default.
        Assert.Equal(BundledFonts.DisplayFamily, resolved.Emphasis.FontFamily);
        Assert.Equal(FontWeight.Bold, resolved.Emphasis.Weight);

        // Anything the document does not name keeps the widget's own value.
        Assert.Equal(defaults.Small, resolved.Small);
        Assert.Equal(document.Theme.ColorTokens, resolved.ColorTokens);
    }

    [Fact]
    public void TheResolvedStyleIsWhatEndsUpInTheDrawList()
    {
        (Document document, WidgetBlock widget) = StyledDocument();
        using DocumentRenderSource source = DocumentRenderSource.Create(
            document, new Dictionary<string, byte[]>(), SnapshotInfra.Store.Value, options: null, widgets: Widgets);

        Assert.True(source.TryGetWidgetDrawList(widget.Id, out WidgetDrawList? drawList));
        var rules = drawList!.Items.OfType<WidgetRuleItem>().ToList();

        Assert.NotEmpty(rules);
        Assert.All(rules, r => Assert.Equal(DocumentRuleArgb, r.ColorArgb));
        Assert.All(rules, r => Assert.Equal(DocumentRuleWidthPt, r.WidthPt));

        // And the heading really is set in the document's face, not the widget's.
        PositionedGlyphRun heading = drawList.Items.OfType<WidgetTextItem>()
            .SelectMany(i => i.Runs)
            .OrderBy(r => r.BaselineY)
            .First();
        Assert.Equal(BundledFonts.SansFamily, heading.Font.Key.Family);
        Assert.Equal(15f, heading.SizePt);
    }

    private static (Document Document, WidgetBlock Widget) StyledDocument()
    {
        var document = new Document();
        document.PageMasters.Add(new PageMaster { Id = "master-1" });
        document.Theme.ColorTokens["widgetAccent"] = 0xFFEEDDCC;
        document.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "table-heading",
            FontFamily = BundledFonts.SansFamily,
            Weight = FontWeightToken.Bold,
            SizePt = 15f,
            ColorArgb = 0xFF223344,
        });
        document.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "table-body",
            FontFamily = BundledFonts.DisplayFamily,
            SizePt = 11f,
        });
        document.StyleSheet.TableStyles.Add(new TableStyleDef
        {
            Name = "lodge-table",
            HeaderCharacterStyleRef = "table-heading",
            BodyCharacterStyleRef = "table-body",
            RuleArgb = DocumentRuleArgb,
            RuleWidthPt = DocumentRuleWidthPt,
        });

        var widget = new WidgetBlock
        {
            Id = "w-officers",
            WidgetType = "officersTable",
            DataVersion = 1,
            TableStyleRef = "lodge-table",
            Data = Officers(),
            FrameRect = new RectPt(54f, 54f, 300f, 260f),
            ZOrder = 1,
            WrapMode = WrapMode.Rectangle,
            WrapMarginPt = 6f,
        };

        var page = new Page { Id = "page-1", MasterRef = "master-1" };
        page.Blocks.Add(widget);
        document.Pages.Add(page);
        return (document, widget);
    }

    private static JsonElement Officers()
    {
        var definition = new Widgets.Builtins.OfficersTable.OfficersTableDefinition();
        Widgets.Builtins.OfficersTable.OfficersTableData data =
            definition.CreateEmpty(new WidgetSeed("", 1, 2026, ""));
        for (int i = 0; i < data.Officers.Count; i++)
        {
            data.Officers[i].Name = $"{(char)('A' + i)}. Placeholder";
        }

        return definition.WriteData(data);
    }
}
