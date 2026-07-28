using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// The sibling trap (PLAN.md M14). CharacterStyleResolver.TryResolve falls back to an attribute
/// scan matching same family + same size + same colour. Change one sibling and not the others
/// and Ctrl+B silently stops finding its pair and starts minting duplicates.
/// </summary>
public sealed class StyleFontTests
{
    [Fact]
    public void ChangingAFamilyCarriesTheBoldSiblingWithIt()
    {
        // The regression this whole command exists for. The fixture already has "body" AND
        // "body-bold" at the same family and size, so the sweep is genuinely exercised.
        Document doc = Fixtures.BuildDocument();

        new SetCharacterStyleFontCommand("body", "Lora", null).Apply(doc);

        Assert.True(CharacterStyleResolver.TryResolve(
            doc.StyleSheet, "body", FontWeightToken.Bold, FontSlantToken.Normal,
            out CharacterStyleDef bold));
        Assert.Equal("body-bold", bold.Name);
        Assert.Equal("Lora", bold.FontFamily);
        Assert.Equal("Lora", doc.StyleSheet.GetCharacterStyle("body").FontFamily);
    }

    [Fact]
    public void ChangingASizeCarriesTheBoldSiblingWithIt()
    {
        Document doc = Fixtures.BuildDocument();

        new SetCharacterStyleFontCommand("body", null, 14f).Apply(doc);

        Assert.True(CharacterStyleResolver.TryResolve(
            doc.StyleSheet, "body", FontWeightToken.Bold, FontSlantToken.Normal,
            out CharacterStyleDef bold));
        Assert.Equal("body-bold", bold.Name);
        Assert.Equal(14f, bold.SizePt);
        Assert.Equal("Source Serif 4", bold.FontFamily);
    }

    [Fact]
    public void OneUndoStepPutsEverySiblingBack()
    {
        Document doc = Fixtures.BuildDocument();
        var command = new SetCharacterStyleFontCommand("body", "Lora", 14f);

        command.Apply(doc);
        command.Revert(doc);

        foreach (string name in new[] { "body", "body-bold" })
        {
            CharacterStyleDef style = doc.StyleSheet.GetCharacterStyle(name);
            Assert.Equal("Source Serif 4", style.FontFamily);
            Assert.Equal(12f, style.SizePt);
        }
    }

    [Fact]
    public void AnOverriddenSpanSurvivesABaseStyleChange()
    {
        Document doc = Fixtures.BuildDocument();
        CharacterStyleDef body = doc.StyleSheet.GetCharacterStyle("body");
        string overrideName = StyleOverrides.NameFor("body", "EB Garamond", body.SizePt, body.SizePt);
        doc.StyleSheet.CharacterStyles.Add(
            CharacterStyleResolver.Derive(body, overrideName, FontWeightToken.Regular, FontSlantToken.Normal));
        doc.StyleSheet.GetCharacterStyle(overrideName).FontFamily = "EB Garamond";

        new SetCharacterStyleFontCommand("body", "Lora", null).Apply(doc);

        // Group membership is by name, and "body~ebgaramond" bases to itself, not to "body".
        Assert.Equal("EB Garamond", doc.StyleSheet.GetCharacterStyle(overrideName).FontFamily);
        Assert.Equal("Lora", doc.StyleSheet.GetCharacterStyle("body").FontFamily);
    }

    [Fact]
    public void BoldInsideAnOverrideStillFindsItsPair()
    {
        // Why the separator is "~" and not "-": BaseName strips only -bold/-italic, so the
        // sibling machinery keeps working inside an override.
        Document doc = Fixtures.BuildDocument();
        CharacterStyleDef body = doc.StyleSheet.GetCharacterStyle("body");
        string overrideName = StyleOverrides.NameFor("body", "EB Garamond", body.SizePt, body.SizePt);
        doc.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = overrideName,
            FontFamily = "EB Garamond",
            SizePt = body.SizePt,
        });
        doc.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = overrideName + "-bold",
            FontFamily = "EB Garamond",
            Weight = FontWeightToken.Bold,
            SizePt = body.SizePt,
        });

        Assert.Equal("body~ebgaramond", overrideName);
        Assert.True(CharacterStyleResolver.TryResolve(
            doc.StyleSheet, overrideName, FontWeightToken.Bold, FontSlantToken.Normal,
            out CharacterStyleDef bold));
        Assert.Equal("body~ebgaramond-bold", bold.Name);

        // ...and changing the override's font sweeps its own bold, not the base one's.
        new SetCharacterStyleFontCommand(overrideName, "Lato", null).Apply(doc);
        Assert.Equal("Lato", doc.StyleSheet.GetCharacterStyle(overrideName + "-bold").FontFamily);
        Assert.Equal("Source Serif 4", doc.StyleSheet.GetCharacterStyle("body-bold").FontFamily);
    }

    [Fact]
    public void AnOverrideNameCarriesTheSizeOnlyWhenTheSizeDiffers()
    {
        Assert.Equal("body~lora", StyleOverrides.NameFor("body", "Lora", 11f, 11f));
        Assert.Equal("body~lora-14", StyleOverrides.NameFor("body", "Lora", 14f, 11f));
        Assert.Equal("body~lora-85", StyleOverrides.NameFor("body", "Lora", 8.5f, 11f));
        Assert.Equal("body~lora", StyleOverrides.NameFor("body-bold-italic", "Lora", 11f, 11f));
    }

    [Fact]
    public void OverrideNamesReportTheRoleTheyDeriveFrom()
    {
        Assert.True(StyleOverrides.IsOverride("body~lora-bold"));
        Assert.False(StyleOverrides.IsOverride("body-bold"));
        Assert.Equal("body", StyleOverrides.RoleOf("body~lora-bold"));
        Assert.Equal("body", StyleOverrides.RoleOf("body-italic"));
        Assert.Equal("table-header", StyleOverrides.RoleOf("table-header"));
    }

    [Fact]
    public void RolesAreLabelledInPlainLanguage()
    {
        Assert.Equal("Body text", StyleLabels.Describe("body"));
        Assert.Equal("Body text", StyleLabels.Describe("body-bold-italic"));
        Assert.Equal("Tables", StyleLabels.Describe("table"));
        Assert.Equal("Table headings", StyleLabels.Describe("table-header"));
        Assert.Equal("Style: sidebar", StyleLabels.Describe("sidebar-italic"));
        Assert.False(StyleLabels.IsKnown("sidebar"));
    }

    /// <summary>
    /// M20 (h): the font window lists roles in the order a page is read in, and that order lives
    /// here rather than in the window, so it is testable without a UI thread.
    /// </summary>
    [Fact]
    public void RolesCarryADeclaredSemanticOrderRatherThanTheAlphabetOfTheirRawNames()
    {
        Assert.Equal(
            ["display", "heading", "subheading", "body", "quote", "caption", "table-header", "table"],
            StyleLabels.DeclaredOrder);

        Assert.True(StyleLabels.OrderOf("display") < StyleLabels.OrderOf("heading"));
        Assert.True(StyleLabels.OrderOf("heading") < StyleLabels.OrderOf("body"));
        Assert.True(StyleLabels.OrderOf("body") < StyleLabels.OrderOf("caption"));

        // A variant sorts with its base, and a role this build has never heard of sorts last
        // rather than being dropped or guessed at.
        Assert.Equal(StyleLabels.OrderOf("body"), StyleLabels.OrderOf("body-bold-italic"));
        Assert.Equal(StyleLabels.DeclaredOrder.Count, StyleLabels.OrderOf("sidebar"));

        // Every label this build knows is in the order, and nothing is in it twice.
        Assert.Equal(StyleLabels.KnownRoles.Count, StyleLabels.DeclaredOrder.Distinct().Count());
        Assert.All(StyleLabels.KnownRoles, role => Assert.Contains(role, StyleLabels.DeclaredOrder));
    }

    [Fact]
    public void AnOverrideDescribesItselfAgainstItsRole()
    {
        var role = new CharacterStyleDef { Name = "body", FontFamily = "Source Serif 4", SizePt = 11f };
        var family = new CharacterStyleDef { Name = "body~lora", FontFamily = "Lora", SizePt = 11f };
        var size = new CharacterStyleDef { Name = "body~sourceserif4-14", FontFamily = "Source Serif 4", SizePt = 14f };
        var both = new CharacterStyleDef { Name = "body~lora-14", FontFamily = "Lora", SizePt = 14f };

        Assert.Equal("This text uses Lora instead of the Body text font.",
            StyleOverrides.Describe(family, role));
        Assert.Equal("This text is 14 pt instead of the Body text size.",
            StyleOverrides.Describe(size, role));
        Assert.Equal("This text uses Lora at 14 pt instead of the Body text font.",
            StyleOverrides.Describe(both, role));
    }

    [Fact]
    public void AnAuditFindsOnlyTheFamiliesTheBuildLacks()
    {
        Document doc = Fixtures.BuildDocument();
        doc.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "fancy",
            FontFamily = "Woodcut Antiqua",
            SizePt = 18f,
        });

        IReadOnlyList<UnknownFontUse> unknown =
            DocumentFontAudit.FindUnknownFamilies(doc, ["Source Serif 4", "Source Sans 3"]);

        UnknownFontUse use = Assert.Single(unknown);
        Assert.Equal("Woodcut Antiqua", use.FontFamily);
        Assert.Equal(["fancy"], use.StyleNames);
    }

    [Fact]
    public void ADocumentTheBuildCanDrawProducesNoWarning()
    {
        Document doc = Fixtures.BuildDocument();

        IReadOnlyList<UnknownFontUse> unknown =
            DocumentFontAudit.FindUnknownFamilies(doc, ["Source Serif 4", "Source Sans 3"]);

        Assert.Empty(unknown);
        Assert.Null(DocumentFontAudit.DescribeWarning(unknown, "Source Serif 4"));
    }

    [Fact]
    public void TheWarningSaysWhatHappensAndThatNothingIsLost()
    {
        var unknown = new UnknownFontUse[]
        {
            new("Woodcut Antiqua", ["fancy"]),
            new("Zither Sans", ["sidebar"]),
        };

        string warning = DocumentFontAudit.DescribeWarning(unknown, "Source Serif 4")!;

        Assert.Contains("Woodcut Antiqua and Zither Sans", warning, StringComparison.Ordinal);
        Assert.Contains("shown in Source Serif 4 instead", warning, StringComparison.Ordinal);
        Assert.Contains("not changed", warning, StringComparison.Ordinal);
        Assert.Contains("newer version", warning, StringComparison.Ordinal);
    }
}
