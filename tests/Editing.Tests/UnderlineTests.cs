using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M102: a line under the words — the last of M86's three deliverables.
///
/// <para>Underline is a third dimension of the sibling machinery rather than a <c>~</c> override,
/// which is how the "just here" font and M99's colour ride. An override occupies the ONE override
/// slot, so underline-as-override could never combine with a colour, and a red underlined heading
/// is an ordinary thing to want.</para>
/// </summary>
public sealed class UnderlineTests
{
    private static EditorTestHarness Highlighted(string text)
    {
        var harness = new EditorTestHarness(text, withExclusion: false);
        harness.ClickIntoFrame();
        harness.Controller.SelectAll();
        return harness;
    }

    private static CharacterStyleDef StyleOf(EditorTestHarness harness)
    {
        string reference = Assert.IsType<string>(harness.Story.Paragraphs[0].Runs[0].CharacterStyleRef);
        return harness.Session.Document.StyleSheet.GetCharacterStyle(reference);
    }

    /// <summary>Underlining reaches the field the renderer reads.</summary>
    [Fact]
    public void UnderliningReachesTheStyleTheRendererReads()
    {
        using EditorTestHarness harness = Highlighted("A heading.");

        Assert.False(harness.Controller.IsUnderlineActive);
        harness.Controller.ToggleUnderline();

        Assert.True(harness.Controller.IsUnderlineActive);
        Assert.True(StyleOf(harness).Underline);
    }

    /// <summary>Pressing it twice puts the words back on a style with no line under them.</summary>
    [Fact]
    public void PressingItTwiceTakesTheLineOffAgain()
    {
        using EditorTestHarness harness = Highlighted("A heading.");

        harness.Controller.ToggleUnderline();
        harness.Controller.SelectAll();
        harness.Controller.ToggleUnderline();

        Assert.False(harness.Controller.IsUnderlineActive);
    }

    /// <summary>
    /// It composes with bold. `body-bold-underline` is a real style name, and this is the property
    /// that a `~` override could not have given: an override slot holds one thing.
    /// </summary>
    [Fact]
    public void ItComposesWithBold()
    {
        using EditorTestHarness harness = Highlighted("A heading.");

        harness.Controller.ToggleUnderline();
        harness.Controller.SelectAll();
        harness.Controller.ToggleBold();

        CharacterStyleDef style = StyleOf(harness);
        Assert.True(style.Underline);
        Assert.Equal(FontWeightToken.Bold, style.Weight);
        Assert.True(harness.Controller.IsUnderlineActive);
        Assert.True(harness.Controller.IsBoldActive);
    }

    /// <summary>And the other way round: bolding first, then underlining, reaches the same place.</summary>
    [Fact]
    public void TheOrderOfBoldAndUnderlineDoesNotMatter()
    {
        using EditorTestHarness harness = Highlighted("A heading.");
        harness.Controller.ToggleBold();
        harness.Controller.SelectAll();
        harness.Controller.ToggleUnderline();

        CharacterStyleDef style = StyleOf(harness);
        Assert.True(style.Underline);
        Assert.Equal(FontWeightToken.Bold, style.Weight);
    }

    /// <summary>Taking the bold off leaves the line under the words.</summary>
    [Fact]
    public void TakingTheBoldOffKeepsTheLine()
    {
        using EditorTestHarness harness = Highlighted("A heading.");

        harness.Controller.ToggleUnderline();
        harness.Controller.SelectAll();
        harness.Controller.ToggleBold();
        harness.Controller.SelectAll();
        harness.Controller.ToggleBold();

        Assert.True(harness.Controller.IsUnderlineActive);
        Assert.False(harness.Controller.IsBoldActive);
    }

    /// <summary>One Ctrl+Z takes it back, and the minted style with it.</summary>
    [Fact]
    public void TakingItBackRemovesTheStyleItMinted()
    {
        using EditorTestHarness harness = Highlighted("A heading.");
        int before = harness.Session.Document.StyleSheet.CharacterStyles.Count;

        harness.Controller.ToggleUnderline();
        harness.Session.Undo();

        Assert.Null(harness.Story.Paragraphs[0].Runs[0].CharacterStyleRef);
        Assert.Equal(before, harness.Session.Document.StyleSheet.CharacterStyles.Count);
    }

    // ---- the naming convention ------------------------------------------------------------------

    /// <summary>Every combination of the three unwinds back to the role it came from.</summary>
    [Theory]
    [InlineData("body-underline", "body")]
    [InlineData("body-bold-underline", "body")]
    [InlineData("body-italic-underline", "body")]
    [InlineData("body-bold-italic-underline", "body")]
    [InlineData("heading-underline", "heading")]
    public void EveryCombinationUnwindsToItsRole(string name, string role) =>
        Assert.Equal(role, CharacterStyleResolver.BaseName(name));

    /// <summary>The suffixes go on in one order, so a style has exactly one name.</summary>
    [Fact]
    public void ThereIsOnlyOneNameForEachCombination()
    {
        Assert.Equal(
            "body-bold-italic-underline",
            CharacterStyleResolver.VariantName(
                "body", FontWeightToken.Bold, FontSlantToken.Italic, underline: true));

        Assert.Equal(
            "body-underline",
            CharacterStyleResolver.VariantName(
                "body", FontWeightToken.Regular, FontSlantToken.Normal, underline: true));
    }

    /// <summary>
    /// Asking for the underlined sibling must not come back with the plain one.
    ///
    /// <para>The attribute scan matches on family, size and colour, and "body" satisfies all three
    /// — so without underline in the comparison it would be returned as its own underlined sibling
    /// and the line would silently never appear. This is the defect the extra clause exists for.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePlainStyleIsNotMistakenForItsUnderlinedSibling()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        StyleSheet sheet = harness.Session.Document.StyleSheet;

        Assert.False(
            CharacterStyleResolver.TryResolve(
                sheet, "body", FontWeightToken.Regular, FontSlantToken.Normal,
                out _, underline: true),
            "the plain body style was offered as its own underlined sibling");
    }

    /// <summary>Deriving keeps the underline unless it is explicitly changed.</summary>
    [Fact]
    public void DerivingKeepsTheUnderlineUnlessAskedOtherwise()
    {
        var from = new CharacterStyleDef
        {
            Name = "body-underline",
            FontFamily = "Source Serif 4",
            SizePt = 11f,
            Underline = true,
        };

        Assert.True(CharacterStyleResolver
            .Derive(from, "body-bold-underline", FontWeightToken.Bold, FontSlantToken.Normal)
            .Underline);

        Assert.False(CharacterStyleResolver
            .Derive(from, "body-bold", FontWeightToken.Bold, FontSlantToken.Normal, underline: false)
            .Underline);
    }
}
