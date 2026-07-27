using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using TrestleBoard.Layout;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// "Use a different font just here" (PLAN.md M14). The override mints a derived style and applies
/// it BY REFERENCE — runs never carry direct formatting in v1, and this must not become the
/// exception that breaks that rule.
/// </summary>
public sealed class FontOverrideTests
{
    private const string Prose =
        "The Placeholder Lodge convenes on the appointed evening of every month for fellowship.";

    [Fact]
    public void AnOverrideMintsADerivedStyleAndNoDirectFormatting()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.SelectAll();

        h.Controller.UseFontJustHere("Lora", null);

        Assert.Contains(h.Session.Document.StyleSheet.CharacterStyles, s => s.Name == "body~lora");
        CharacterStyleDef derived = h.Session.Document.StyleSheet.GetCharacterStyle("body~lora");
        Assert.Equal("Lora", derived.FontFamily);
        Assert.Equal(12f, derived.SizePt);

        // By reference: the run names the style, and carries nothing of its own.
        Assert.All(h.Story.Paragraphs[0].Runs, run => Assert.Equal("body~lora", run.CharacterStyleRef));
    }

    [Fact]
    public void AnOverrideWithADifferentSizeCarriesTheSizeInItsName()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.SelectAll();

        h.Controller.UseFontJustHere("Lora", 18f);

        CharacterStyleDef derived = h.Session.Document.StyleSheet.GetCharacterStyle("body~lora-18");
        Assert.Equal(18f, derived.SizePt);
    }

    [Fact]
    public void BoldInsideAnOverrideStaysInsideTheOverride()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.SelectAll();
        h.Controller.UseFontJustHere("Lora", null);

        h.Controller.ToggleBold();

        CharacterStyleDef bold = h.Session.Document.StyleSheet.GetCharacterStyle("body~lora-bold");
        Assert.Equal("Lora", bold.FontFamily);
        Assert.Equal(FontWeightToken.Bold, bold.Weight);
        Assert.True(h.Controller.IsBoldActive);
    }

    [Fact]
    public void PuttingItBackReturnsToTheRolesOwnStyle()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.SelectAll();
        h.Controller.UseFontJustHere("Lora", null);
        Assert.True(h.Controller.SelectionUsesFontOverride);

        h.Controller.SelectAll();
        h.Controller.ClearFontOverride();

        Assert.False(h.Controller.SelectionUsesFontOverride);
        Assert.Equal(0, h.Controller.CountFontOverrides());
        Assert.All(h.Story.Paragraphs[0].Runs, run => Assert.Null(run.CharacterStyleRef));
    }

    [Fact]
    public void OneUndoStepTakesTheWholeOverrideBackOff()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.SelectAll();

        h.Controller.UseFontJustHere("Lora", null);
        h.Session.Undo();

        Assert.Equal(0, h.Controller.CountFontOverrides());
        Assert.DoesNotContain(h.Session.Document.StyleSheet.CharacterStyles, s => s.Name == "body~lora");
    }

    [Fact]
    public void AnOverriddenSpanSurvivesAChangeToTheBaseStyle()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.SelectAll();
        h.Controller.UseFontJustHere("Lora", null);

        h.Session.Execute(new Core.Commands.SetCharacterStyleFontCommand("body", "Bitter", null));

        Assert.Equal("Lora", h.Session.Document.StyleSheet.GetCharacterStyle("body~lora").FontFamily);
        Assert.Equal("Bitter", h.Session.Document.StyleSheet.GetCharacterStyle("body").FontFamily);
        Assert.Equal("Bitter", h.Session.Document.StyleSheet.GetCharacterStyle("body-bold").FontFamily);
    }

    [Fact]
    public void TheOverlayIsToldExactlyWhichCharactersAreOverridden()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.SelectAll();
        h.Controller.UseFontJustHere("Lora", null);

        SourceSpan span = Assert.Single(h.Controller.FontOverrideSpans());
        Assert.Equal(EditorTestHarness.StoryId, span.StoryId);
        Assert.Equal(0, span.ParagraphIndex);
        Assert.Equal(0, span.StartChar);
        Assert.Equal(Prose.Length, span.EndChar);
    }

    [Fact]
    public void TheOverrideDescribesItselfAgainstItsRole()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.SelectAll();
        h.Controller.UseFontJustHere("Lora", null);

        h.Controller.SelectAll();
        Assert.Equal(
            "This text uses Lora instead of the Body text font.",
            h.Controller.DescribeFontOverride());
    }

    [Fact]
    public void TextUsingItsRolesFontHasNothingToSay()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.SelectAll();

        Assert.Null(h.Controller.DescribeFontOverride());
        Assert.False(h.Controller.SelectionUsesFontOverride);
    }
}
