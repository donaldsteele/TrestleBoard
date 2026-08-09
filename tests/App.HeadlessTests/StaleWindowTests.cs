using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// Verification gate 23, part six: closing a document does not leave a window pointing into it
/// (PLAN.md §11 M70 (g)).
///
/// <para><b>Why.</b> Only the find window was closed when a document closed. Review, ReadAloud,
/// Spelling, LastYear and Help stayed open holding a snapshot of a newsletter that was no longer
/// open — so their "Take me there" buttons pointed into a document nobody could see. It is the
/// quietest failure in the audit: everything looks normal, and the button simply refers to
/// something that is gone.</para>
/// </summary>
public sealed class StaleWindowTests
{
    /// <summary>
    /// Opening a different newsletter must not leave a window behind holding the old one. Asserted
    /// through the real shell rather than by reading source, because this one CAN be driven: the
    /// windows are opened by public methods and the switch is an ordinary command.
    /// </summary>
    [Fact]
    public async Task SwitchingDocumentsLeavesNoWindowHoldingTheOldOne()
    {
        await HeadlessSession.Instance.Dispatch(
            () =>
            {
                var window = new MainWindow();
                window.Show();
                window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
                window.OpenSample();

                window.ShowReview();
                window.ShowSpellingCheck();
                window.ReadItBackToMe();

                Assert.True(
                    AnyHelperWindowOpen(window),
                    "no helper window opened, so this test is not exercising anything");

                // A different newsletter, through ShowPackage — the funnel every open, template,
                // carry-forward and restore path goes through. Deliberately NOT the template
                // command: that opens a modal, and a headless test has nobody to answer it.
                window.OpenIssueSample();

                Assert.False(
                    AnyHelperWindowOpen(window),
                    "a window survived the document switch still holding the previous newsletter's "
                    + "snapshot — its buttons now point into a document that is not open. "
                    + "See PLAN.md M70 (g).");

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A window that closes itself must say why. A window vanishing unexplained is its own small
    /// version of the defect this whole milestone is about.
    /// </summary>
    [Fact]
    public async Task AWindowThatClosesItselfSaysWhy()
    {
        await HeadlessSession.Instance.Dispatch(
            () =>
            {
                var window = new MainWindow();
                window.Show();
                window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
                window.OpenSample();
                window.ShowReview();

                window.OpenIssueSample();

                Assert.False(
                    string.IsNullOrWhiteSpace(window.StatusLabelTextForTest),
                    "windows were closed by the document switch and the app said nothing about it");

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// (h) The residual edge in M69's own fix: the paragraph-style flyout must never be anchored to
    /// a control that has left the visual tree, which is what <c>?? source</c> did whenever the
    /// rebuilt panel no longer offered the command.
    /// </summary>
    [Fact]
    public void TheParagraphStyleFlyoutNeverFallsBackToADetachedControl()
    {
        string source = System.IO.File.ReadAllText(
            System.IO.Path.Combine(RepoRoot(), "src", "TrestleBoard.App", "MainWindow.axaml.cs"));

        Assert.DoesNotContain(
            "PanelButtonFor(ActionId.ParagraphStyle) ?? source",
            source,
            StringComparison.Ordinal);
    }

    private static bool AnyHelperWindowOpen(MainWindow window) =>
        window.ReviewWindowForTest is not null
        || window.SpellingWindowForTest is not null
        || window.ReadAloudWindowForTest is not null;

    private static string RepoRoot()
    {
        var at = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !System.IO.File.Exists(System.IO.Path.Combine(at.FullName, "PLAN.md")))
        {
            at = at.Parent;
        }

        Assert.True(at is not null, "could not find the repository root above the test binary");
        return at!.FullName;
    }
}
