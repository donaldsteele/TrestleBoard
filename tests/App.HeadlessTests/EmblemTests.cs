using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Emblems;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The emblem shelf, through the running app (PLAN.md §11 M65).
///
/// <para>The shelf itself, its provenance gate and its determinism are held by
/// <c>Emblems.Tests</c>, which needs no Avalonia. What is left for here is the claim the milestone
/// actually rests on: <b>an emblem becomes an ordinary picture</b>, so everything the app already
/// knows how to do to a picture works on it and nothing downstream learns that emblems exist.</para>
/// </summary>
public sealed class EmblemTests
{
    [Fact]
    public async Task AnEmblemLandsOnThePageAsAnOrdinaryPicture()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();
                int before = PicturesOn(window);

                window.EmblemAnswerForTest = "square-and-compasses";
                await window.InsertEmblemAsync();

                Assert.Equal(before + 1, PicturesOn(window));
                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The acceptance: "Emblems carry default descriptions for the screen reader". The app drew the
    /// picture, so it knows what it is — asking the user to describe the square and compasses back
    /// to it would be the software pretending not to know something.
    /// </summary>
    [Fact]
    public async Task ItArrivesAlreadyDescribedForAScreenReader()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.EmblemAnswerForTest = "gavel";
                await window.InsertEmblemAsync();

                ImageFrame added = Pictures(window).Last();
                Assert.Equal(EmblemLibrary.Find("gavel")!.Description, added.AltText);
                Assert.False(string.IsNullOrWhiteSpace(added.AltText));

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// One undo puts the page back. It comes for free from going through the picture ingest path,
    /// which is the point of going through it — and it is the thing that would quietly not work if
    /// somebody later "optimised" the emblem into a frame type of its own.
    /// </summary>
    [Fact]
    public async Task OneUndoTakesItBackOff()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();
                int before = PicturesOn(window);

                window.EmblemAnswerForTest = "blazing-star";
                await window.InsertEmblemAsync();
                Assert.Equal(before + 1, PicturesOn(window));

                window.Undo();
                Assert.Equal(before, PicturesOn(window));

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Closing the picker without choosing changes nothing at all.</summary>
    [Fact]
    public async Task TakingNothingFromTheShelfChangesNothing()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();
                int before = PicturesOn(window);

                window.EmblemAnswerForTest = "no-such-emblem";
                await window.InsertEmblemAsync();

                Assert.Equal(before, PicturesOn(window));
                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    // ---- the picker ---------------------------------------------------------------------------------

    [Fact]
    public async Task TheShelfOpensShowingEverythingAndTypingNarrowsIt()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var picker = new EmblemPickerWindow();

            Assert.Equal(EmblemLibrary.All.Count, picker.TilesForTest.Count);
            Assert.Contains("All ", picker.CountTextForTest, StringComparison.Ordinal);

            picker.TypeForTest("gavel");
            Assert.Single(picker.TilesForTest);
            Assert.Contains("One emblem", picker.CountTextForTest, StringComparison.Ordinal);

            picker.TypeForTest("kubernetes");
            Assert.Empty(picker.TilesForTest);
            Assert.Contains("Nothing on the shelf", picker.CountTextForTest, StringComparison.Ordinal);

            picker.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Somebody choosing a picture they cannot see gets the name and the sentence. The name alone
    /// leaves them guessing what is about to land on the page.
    /// </summary>
    [Fact]
    public async Task EveryTileSaysWhatItIsAndWhatItLooksLike()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var picker = new EmblemPickerWindow();

            foreach (Button tile in picker.TilesForTest)
            {
                string spoken = AutomationProperties.GetName(tile) ?? "";
                Emblem emblem = EmblemLibrary.Find((string)tile.Tag!)!;

                Assert.Contains(emblem.Name, spoken, StringComparison.Ordinal);
                Assert.Contains(emblem.Description, spoken, StringComparison.Ordinal);
            }

            picker.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheShelfIsLaidOutUnderItsHeadings()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var picker = new EmblemPickerWindow();

            // Tiles come out in shelf order, which is category order — so somebody walking the
            // window with Tab meets the working tools before the ornaments.
            string[] categories = [.. picker.TilesForTest
                .Select(t => EmblemLibrary.Find((string)t.Tag!)!.Category)];

            Assert.Equal(categories.Distinct().ToArray(), EmblemLibrary.Categories.ToArray());

            picker.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- the catalog ---------------------------------------------------------------------------------

    [Fact]
    public void AddingAnEmblemNeedsANewsletterAndSaysSoWhenThereIsNone()
    {
        Assert.False(ActionCatalog.Evaluate(ActionId.InsertEmblem, new ActionContext()).IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(
            ActionCatalog.Evaluate(ActionId.InsertEmblem, new ActionContext()).Reason));

        Assert.True(ActionCatalog
            .Evaluate(ActionId.InsertEmblem, new ActionContext { HasDocument = true })
            .IsAvailable);
    }

    // ---- plumbing --------------------------------------------------------------------------------------

    private static int PicturesOn(MainWindow window) => Pictures(window).Count;

    private static System.Collections.Generic.List<ImageFrame> Pictures(MainWindow window) =>
        [.. window.PackageForTest!.Document.Pages.SelectMany(p => p.Blocks).OfType<ImageFrame>()];
}
