using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M99: what colour the writing is — M86's third deliverable.
///
/// <para><see cref="CharacterStyleDef.ColorArgb"/> has been plumbed end to end since M1: through
/// <c>CharacterStyleResolver</c>, the layout adapter, the HarfBuzz shaper, `PositionedGlyphRun` and
/// both the canvas and PDF renderers. **And nothing could set it.** Every piece of writing in every
/// newsletter this app has produced has been black because no command existed to make it anything
/// else, not because anybody chose black.</para>
/// </summary>
public sealed class TextColourTests
{
    private static uint Red => PageLooks.TextColours[2].Argb;

    private static EditorTestHarness Highlighted(string text)
    {
        var harness = new EditorTestHarness(text, withExclusion: false);
        harness.ClickIntoFrame();
        harness.Controller.SelectAll();
        return harness;
    }

    private static CharacterStyleDef StyleOf(EditorTestHarness harness)
    {
        string? reference = harness.Story.Paragraphs[0].Runs[0].CharacterStyleRef
            ?? harness.Story.Paragraphs[0].ParagraphStyleRef;
        return harness.Session.Document.StyleSheet.GetCharacterStyle(
            harness.Session.Document.StyleSheet.CharacterStyles.Exists(s => s.Name == reference)
                ? reference
                : "body");
    }

    /// <summary>The colour reaches the field the renderer actually paints with.</summary>
    [Fact]
    public void ColouringReachesWhatTheRendererPaintsWith()
    {
        using EditorTestHarness harness = Highlighted("A heading.");

        Assert.True(harness.Controller.UseColourJustHere(Red));
        Assert.Equal(Red, StyleOf(harness).ColorArgb);
    }

    /// <summary>
    /// It mints a DERIVED style rather than editing the role, so colouring one heading does not
    /// colour every other piece of writing in the newsletter that shares its role.
    /// </summary>
    [Fact]
    public void ColouringSomeWordsLeavesTheRoleAlone()
    {
        using EditorTestHarness harness = Highlighted("A heading.");

        Assert.True(harness.Controller.UseColourJustHere(Red));

        CharacterStyleDef role = harness.Session.Document.StyleSheet.GetCharacterStyle("body");
        Assert.Equal(0xFF000000u, role.ColorArgb);
    }

    /// <summary>
    /// Black is the way out, not a fourth command: choosing the role's own colour names the role
    /// again rather than minting an override that says "the same as the role".
    /// </summary>
    [Fact]
    public void ChoosingBlackPutsTheWritingBackOnItsRole()
    {
        using EditorTestHarness harness = Highlighted("A heading.");

        Assert.True(harness.Controller.UseColourJustHere(Red));
        Assert.NotNull(harness.Story.Paragraphs[0].Runs[0].CharacterStyleRef);

        Assert.True(harness.Controller.UseColourJustHere(PageLooks.TextColours[0].Argb));
        Assert.Null(harness.Story.Paragraphs[0].Runs[0].CharacterStyleRef);
    }

    /// <summary>Asking for the colour it already is changes nothing.</summary>
    [Fact]
    public void AskingForTheColourItAlreadyIsChangesNothing()
    {
        using EditorTestHarness harness = Highlighted("A heading.");

        Assert.False(harness.Controller.UseColourJustHere(PageLooks.TextColours[0].Argb));

        Assert.True(harness.Controller.UseColourJustHere(Red));
        Assert.False(harness.Controller.UseColourJustHere(Red));
    }

    /// <summary>One Ctrl+Z takes the colour back and the minted style with it.</summary>
    [Fact]
    public void TakingItBackRestoresTheWritingAndRemovesTheStyle()
    {
        using EditorTestHarness harness = Highlighted("A heading.");
        int before = harness.Session.Document.StyleSheet.CharacterStyles.Count;

        Assert.True(harness.Controller.UseColourJustHere(Red));
        harness.Session.Undo();

        Assert.Null(harness.Story.Paragraphs[0].Runs[0].CharacterStyleRef);
        Assert.Equal(before, harness.Session.Document.StyleSheet.CharacterStyles.Count);
    }

    /// <summary>
    /// Bold keeps working inside coloured text. That is the property the `~` convention was chosen
    /// for: `BaseName` strips only -bold/-italic, so `body~ink-8c2a2a-bold` bases to
    /// `body~ink-8c2a2a` and the sibling machinery keeps running INSIDE the override.
    /// </summary>
    [Fact]
    public void BoldStillWorksInsideColouredWriting()
    {
        using EditorTestHarness harness = Highlighted("A heading.");

        Assert.True(harness.Controller.UseColourJustHere(Red));
        harness.Controller.SelectAll();
        harness.Controller.ToggleBold();

        Assert.True(harness.Controller.IsBoldActive);

        string reference = Assert.IsType<string>(harness.Story.Paragraphs[0].Runs[0].CharacterStyleRef);
        CharacterStyleDef style = harness.Session.Document.StyleSheet.GetCharacterStyle(reference);
        Assert.Equal(FontWeightToken.Bold, style.Weight);

        // Still red, and still based on the coloured override rather than on plain body-bold.
        Assert.Equal(Red, style.ColorArgb);
        Assert.True(StyleOverrides.IsColourOverride(CharacterStyleResolver.BaseName(reference)));
    }

    /// <summary>A colour override is recognisable as one, and never mistaken for a font override.</summary>
    [Fact]
    public void AColourOverrideIsTellableFromAFontOne()
    {
        Assert.True(StyleOverrides.IsColourOverride("body~ink-8c2a2a"));
        Assert.False(StyleOverrides.IsColourOverride("body~ebgaramond"));
        Assert.False(StyleOverrides.IsColourOverride("body"));

        // The name carries the colour, so two different colours cannot share a style.
        Assert.NotEqual(
            StyleOverrides.ColourNameFor("body", 0xFF8C2A2A),
            StyleOverrides.ColourNameFor("body", 0xFF1F3864));
    }

    /// <summary>
    /// Every colour offered clears the 4.5:1 contrast floor §6 sets against white paper. That is
    /// what makes a fixed list defensible where a full picker would need a warning.
    /// </summary>
    [Fact]
    public void EveryColourOfferedIsLegibleOnWhitePaper()
    {
        foreach ((string name, uint argb) in PageLooks.TextColours)
        {
            Assert.True(ContrastAgainstWhite(argb) >= 4.5, $"{name} is too pale to read in print");
        }
    }

    /// <summary>WCAG relative luminance, the same formula the M16 theme tests use.</summary>
    private static double ContrastAgainstWhite(uint argb)
    {
        double Channel(int v)
        {
            double c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        double luminance = (0.2126 * Channel((int)((argb >> 16) & 0xFF)))
            + (0.7152 * Channel((int)((argb >> 8) & 0xFF)))
            + (0.0722 * Channel((int)(argb & 0xFF)));

        return 1.05 / (luminance + 0.05);
    }
}
