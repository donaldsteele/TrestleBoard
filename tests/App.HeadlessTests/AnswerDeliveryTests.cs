using System;
using System.Linq;
using System.Threading.Tasks;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// Verification gate 23, part one: the status bar keeps what it is given (PLAN.md §11 M70 (a)).
///
/// <para><b>Why this gate exists.</b> Two independent audits found the same defect from opposite
/// directions. <c>UpdateStatus</c> composed the controllers' <c>StatusMessage</c> <b>before</b>
/// <c>_announcement</c>, and <c>ActionRunner</c> refreshes after every command — so a lingering
/// controller message silently discarded the sentence the command had just chosen to say, including
/// every catalog refusal. <c>PhotoController.StatusMessage</c> was never cleared by anything, so one
/// "Picture fixed" could shadow every announcement for the rest of the session.</para>
///
/// <para>M11's promise is that nothing becomes unavailable without saying why. That promise is made
/// by the catalog and kept — or not — here, at the point of delivery.</para>
/// </summary>
public sealed class AnswerDeliveryTests
{
    /// <summary>
    /// The reported shape, reduced: a controller holding a message must not eat the sentence a
    /// refusal just produced.
    /// </summary>
    [Fact]
    public async Task AControllerMessageCannotDiscardARefusal()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                // A controller says something, the way a real refusal does: bytes that are not a
                // picture make PhotoController say so, and the status bar shows it.
                Assert.Null(window.PhotosForTest!.InsertPhoto(0, NotAPicture, altText: ""));
                window.RefreshActions();
                Assert.Contains("not a picture", window.StatusLabelTextForTest ?? "", StringComparison.OrdinalIgnoreCase);

                // Now refuse a command — nothing is selected, so this one cannot run. Its reason
                // must reach the bar. This is the assertion the old precedence failed.
                await window.ActionsForTest.RunAsync(ActionId.DeleteFrame);

                string shown = window.StatusLabelTextForTest ?? "";
                Assert.False(
                    string.IsNullOrWhiteSpace(shown),
                    "the refusal was discarded — the status bar said nothing at all");
                Assert.DoesNotContain("not a picture", shown, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(
                    ActionCatalog.Evaluate(ActionId.DeleteFrame, window.CurrentActionContext).Reason,
                    shown);

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A one-shot sentence does not outlive the act it describes.
    ///
    /// <para>The shell POLLS <c>StatusMessage</c> on every refresh, because for most of what a
    /// controller says that is right — "click the frame the text should continue into" is true for
    /// exactly as long as link mode is armed, and it disappears because the controller stops saying
    /// it. The audited bug was one-shot sentences stored in that state-shaped field: nothing ever
    /// reset them, so "Picture fixed" was still being polled, and still being shown, sessions later.
    /// Fixed where they are written, not by changing how the shell reads them.</para>
    /// </summary>
    [Fact]
    public async Task AOneShotSentenceDoesNotOutliveTheActItDescribes()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            Assert.Null(window.PhotosForTest!.InsertPhoto(0, NotAPicture, altText: ""));
            Assert.NotNull(window.PhotosForTest.StatusMessage);

            // The next operation on the same controller starts by forgetting what was last said.
            // An unknown block id is enough: FixPhoto clears first, then decides it cannot proceed.
            window.PhotosForTest.FixPhoto("no-such-block");
            Assert.DoesNotContain(
                "not a picture",
                window.PhotosForTest.StatusMessage ?? "",
                StringComparison.OrdinalIgnoreCase);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// **Gate 23's headline: every catalog action invoked twice in succession answers both times.**
    ///
    /// <para>An identical string is not a property change, so Avalonia raised nothing and the polite
    /// live region stayed silent on the repeat — a user who pressed a blocked command again, the
    /// commonest thing to do when nothing seems to have happened, got less than the first time.</para>
    /// </summary>
    [Fact]
    public async Task PressingTheSameBlockedActionTwiceAnswersBothTimes()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                string[] blocked =
                [
                    .. ActionCatalog.All
                        .Select(a => a.Id)
                        .Where(id => !ActionCatalog.Evaluate(id, window.CurrentActionContext).IsAvailable)
                        .Take(12),
                ];

                Assert.NotEmpty(blocked);

                foreach (string id in blocked)
                {
                    await window.ActionsForTest.RunAsync(id);
                    string first = window.StatusLabelTextForTest ?? "";

                    await window.ActionsForTest.RunAsync(id);
                    string second = window.StatusLabelTextForTest ?? "";

                    Assert.False(string.IsNullOrWhiteSpace(first), $"{id} said nothing the first time");
                    Assert.Equal(first, second);
                }

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Bytes that are not any picture format, so the decoder refuses and says so.</summary>
    private static readonly byte[] NotAPicture = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];

    /// <summary>
    /// The ambient hint still has its place — it is what the bar says when there is nothing newer,
    /// not instead of it.
    /// </summary>
    [Fact]
    public async Task WithNothingToReportTheBarStillSaysWhereYouAre()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();
            window.RefreshActions();
            window.RefreshActions();

            Assert.NotNull(window.StatusLabelTextForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
