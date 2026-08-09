using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// Verification gate 23, parts four and five: a rebuilt container puts focus back, and a dialog that
/// refuses says so out loud (PLAN.md §11 M70 (e) and (f)).
///
/// <para><b>Why.</b> Five containers in the app are cleared and rebuilt wholesale, and only three
/// put focus back — a keyboard or screen-reader user standing on a control loses their place
/// entirely when it is destroyed under them. Separately, <c>WidgetGridWindow</c> was the one dialog
/// in the project with neither a <c>.Focus()</c> call nor a live region anywhere: press "Save it"
/// with a validation error and a screen-reader user was told nothing at all, because the warning
/// glyph and its colour are both visual.</para>
///
/// <para>These read source text rather than driving the UI, and that is a real limitation worth
/// stating: they prove the mechanism is <b>declared</b>, not that it fires. Constructing these
/// dialogs needs a live document and a wizard session, and an assertion about where focus lands
/// after an awaited modal is not something a headless harness can make honestly. The manual
/// screen-reader pass in <c>docs/accessibility-test-script.md</c> is what actually closes the gap;
/// this stops the declaration being deleted by accident in the meantime.</para>
/// </summary>
public sealed class FocusAndAnnouncementTests
{
    /// <summary>
    /// Every container that clears its children wholesale must put focus somewhere afterwards.
    /// This is the rule the audit found broken in two of five places, asserted as a rule so a sixth
    /// rebuilt container inherits it.
    /// </summary>
    [Theory]
    [InlineData("Dialogs/WidgetGridWindow.cs")]
    [InlineData("Dialogs/WizardWindow.cs")]
    [InlineData("Dialogs/ReviewWindow.cs")]
    [InlineData("Dialogs/SpellingWindow.cs")]
    [InlineData("Dialogs/PhraseWindow.cs")]
    public void AWholesaleRebuildPutsFocusBack(string relativePath)
    {
        string source = Source(relativePath);

        Assert.Contains("Children.Clear()", source, StringComparison.Ordinal);
        Assert.True(
            source.Contains(".Focus()", StringComparison.Ordinal),
            $"{relativePath} rebuilds a container wholesale and never calls Focus(), so a keyboard "
            + "user standing on one of those controls is left with nothing focused. See PLAN.md M70 (e).");
    }

    /// <summary>
    /// The action panel is rebuilt after every single command, so it is the most frequent instance
    /// of the same defect — and the machinery to fix it already existed, added for M69's flyout.
    /// </summary>
    [Fact]
    public void ThePanelRebuildRestoresFocusToTheSameCommand()
    {
        string source = Source("MainWindow.axaml.cs");

        Assert.Contains("PanelButtonFor", source, StringComparison.Ordinal);
        Assert.True(
            Regex.IsMatch(source, @"PanelButtonFor[^;]*\.Focus\(\)", RegexOptions.Singleline)
            || Regex.IsMatch(source, @"Focus\(\)[^;]*PanelButtonFor", RegexOptions.Singleline)
            || source.Contains("RestorePanelFocus", StringComparison.Ordinal),
            "RefreshActions rebuilds every panel button and nothing puts focus back on the command "
            + "the user was standing on. PanelButtonFor already finds a button by Tag in the rebuilt "
            + "panel — M69 added it for the flyout. See PLAN.md M70 (e).");
    }

    /// <summary>
    /// **The worst case in the app.** A dialog that can refuse to close must say why in a way a
    /// screen reader reaches — a warning glyph and a colour are both visual, and this dialog had
    /// neither a live region nor a focus move nor a window rename.
    /// </summary>
    [Theory]
    [InlineData("Dialogs/WidgetGridWindow.cs")]
    [InlineData("Dialogs/WizardWindow.cs")]
    public void AValidationRefusalIsAnnounced(string relativePath)
    {
        string source = Source(relativePath);

        Assert.Contains("RenderErrors", source, StringComparison.Ordinal);
        Assert.True(
            source.Contains("AutomationLiveSetting.Polite", StringComparison.Ordinal),
            $"{relativePath} refuses to save on a validation error and declares no live region, so "
            + "a screen-reader user presses Save, the window stays open, and nothing is said. "
            + "See PLAN.md M70 (f).");
    }

    /// <summary>
    /// The settings preview is the only place the consequence of a theme or size choice is put into
    /// words, and it is the one thing a screen-reader user cannot verify before pressing Save.
    /// </summary>
    [Fact]
    public void TheSettingsPreviewSentenceIsAnnounced()
    {
        string source = Source("Dialogs/SettingsDialog.cs");

        Assert.True(
            source.Contains("AutomationLiveSetting.Polite", StringComparison.Ordinal),
            "SettingsDialog rewrites the sentence describing what the chosen theme and size will do, "
            + "and never announces it. See PLAN.md M70 (f).");
    }

    /// <summary>
    /// Two things the audit explicitly ruled NOT findings, pinned so a later pass does not "fix"
    /// them into noise: both already have redundant coverage.
    /// </summary>
    [Fact]
    public void TheRedundantlyCoveredReadoutsAreLeftAlone()
    {
        // The slider announces its own value; the crop stage's automation name carries the zoom.
        Assert.Contains("Slider", Source("Dialogs/SettingsDialog.cs"), StringComparison.Ordinal);

        // Whitespace-insensitive on purpose. The first version of this test matched a literal
        // "AutomationProperties.SetName(_stage", which failed only because the call happened to be
        // wrapped across three lines — and the implementation was then reformatted to satisfy the
        // test. That is backwards: a pin on behaviour must not hold production formatting hostage.
        Assert.Matches(
            new Regex(@"AutomationProperties\.SetName\(\s*_stage", RegexOptions.Singleline),
            Source("Dialogs/PositionPhotoWindow.cs"));
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "TrestleBoard.App", relativePath));

    private static string RepoRoot()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "PLAN.md")))
        {
            at = at.Parent;
        }

        Assert.True(at is not null, "could not find the repository root above the test binary");
        return at!.FullName;
    }
}
