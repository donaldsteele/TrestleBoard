using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Emblems;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The emblem shelf, through the running app (PLAN.md §11 M65, rewritten for M72).
///
/// <para>The shelf itself and its provenance gate are held by <c>Emblems.Tests</c>, which needs no
/// Avalonia. What is left for here is the claim the milestone rests on — and <b>that claim changed
/// in M72</b>, which is why these assertions are rewritten rather than adjusted.</para>
///
/// <para>M65's claim was "an emblem becomes an ordinary picture", asserted four times over as
/// <c>OfType&lt;ImageFrame&gt;()</c>. M72's is <b>an emblem stays a drawing</b>: it lands as a
/// <see cref="VectorBlock"/> carrying path data, no asset is written, and nothing rasterises
/// between the shelf and the page. The old claim was not merely superseded — it was the mechanism
/// of a real defect, because a raster in the container is bytes that differ between processor
/// architectures, and a macOS member's newsletter was genuinely not the same file as a Windows
/// member's.</para>
///
/// <para>What is NOT rewritten is everything the picture path gave the emblem for free — one undo,
/// a description, moving and wrapping. Those are asserted here exactly as before, because M72 keeps
/// them deliberately rather than inheriting them, and a deliberate promise needs the test more than
/// an inherited one did.</para>
/// </summary>
public sealed class EmblemTests
{
    /// <summary>
    /// The milestone in one assertion: what lands is geometry, and the container gains nothing.
    ///
    /// <para>The emblem's own path data is on the block, so the document is self-describing — a
    /// later revision to the shelf cannot silently change how this newsletter prints.</para>
    /// </summary>
    [Fact]
    public async Task AnEmblemLandsOnThePageAsPathDataAndNotAsAPicture()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();
                int picturesBefore = PicturesOn(window);
                int assetsBefore = window.PackageForTest!.Assets.Count;

                window.EmblemAnswerForTest = "square-and-compasses";
                await window.InsertEmblemAsync();

                Assert.Equal(picturesBefore, PicturesOn(window));
                Assert.Equal(assetsBefore, window.PackageForTest!.Assets.Count);

                VectorBlock drawing = Drawings(window).Single();
                Emblem emblem = EmblemLibrary.Find("square-and-compasses")!;
                Assert.Equal(
                    emblem.Parts.Select(p => p.PathData).ToArray(),
                    drawing.Parts.Select(p => p.PathData).ToArray());
                Assert.Equal(emblem.Width, drawing.ViewBoxWidth);
                Assert.Equal(emblem.Height, drawing.ViewBoxHeight);

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The provenance the block carries, on the <c>SourcePdfAssetRef</c> precedent: which emblem it
    /// is, and which drawing of it. Nothing reads either — this asserts they are recorded, which is
    /// the only promise made about them.
    /// </summary>
    [Fact]
    public async Task ItRecordsWhichEmblemItIsAndWhichDrawingOfIt()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.EmblemAnswerForTest = "trowel";
                await window.InsertEmblemAsync();

                VectorBlock drawing = Drawings(window).Single();
                Assert.Equal("trowel", drawing.EmblemId);
                Assert.Equal(
                    EmblemFingerprint.Of(EmblemLibrary.Find("trowel")!),
                    drawing.EmblemFingerprint);

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// §12 gate 25, as near as one machine can get to it: a newsletter with an emblem on it holds
    /// no image asset at all, and saves to the same bytes twice. A container with no raster in it
    /// cannot differ between x64 and arm64, which is what makes the cross-architecture claim true
    /// by construction rather than by hoping Skia is bit-stable.
    /// </summary>
    [Fact]
    public async Task ANewsletterWithAnEmblemCarriesNoRaster()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                TboardPackage package = window.PackageForTest!;
                var assetsBefore = package.Assets.Keys.Order(StringComparer.Ordinal).ToList();

                window.EmblemAnswerForTest = "all-seeing-eye";
                await window.InsertEmblemAsync();

                // Not one byte of raster was added. The sample's own photograph is still there, and
                // is still what it always was — what matters is that putting an emblem on the page
                // no longer contributes anything that a processor architecture could disagree about.
                Assert.Equal(assetsBefore, package.Assets.Keys.Order(StringComparer.Ordinal).ToList());

                using var first = new MemoryStream();
                using var second = new MemoryStream();
                TboardContainer.Save(package, first);
                TboardContainer.Save(package, second);
                Assert.Equal(first.ToArray(), second.ToArray());

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A drawing survives a save and a reopen with its geometry intact, and the file says out loud
    /// that it needs a 1.1.0 reader — so an older TrestleBoard refuses it in plain language rather
    /// than binding an unknown block discriminator to nothing and dropping it off the page.
    /// </summary>
    [Fact]
    public async Task ADrawingSurvivesSavingAndSaysWhichReaderItNeeds()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.EmblemAnswerForTest = "level";
                await window.InsertEmblemAsync();

                using var saved = new MemoryStream();
                TboardContainer.Save(window.PackageForTest!, saved);
                saved.Position = 0;
                TboardPackage reopened = TboardContainer.Load(saved);

                Assert.Equal("1.1.0", reopened.Manifest.MinReaderVersion);
                VectorBlock drawing = reopened.Document.Pages
                    .SelectMany(p => p.Blocks).OfType<VectorBlock>().Single();
                Assert.Equal(
                    EmblemLibrary.Find("level")!.Parts[0].PathData, drawing.Parts[0].PathData);

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

                VectorBlock added = Drawings(window).Single();
                Assert.Equal(EmblemLibrary.Find("gavel")!.Description, added.AltText);
                Assert.False(string.IsNullOrWhiteSpace(added.AltText));

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// One undo puts the page back.
    ///
    /// <para><b>This doc comment used to warn against the change M72 just made</b>, and overruling
    /// it in writing is part of the milestone rather than an afterthought. It said one undo "comes
    /// for free from going through the picture ingest path … the thing that would quietly not work
    /// if somebody later optimised the emblem into a frame type of its own". The warning was sound
    /// and it has been honoured rather than ignored: <c>PhotoController.InsertVector</c> reuses the
    /// same default rectangle, the same z-order rule and the same single <c>CompositeCommand</c>
    /// that <c>InsertPhoto</c> does, precisely so this stays true. It is kept on purpose now
    /// instead of inherited, which is why the test matters more than it did.</para>
    ///
    /// <para>What the warning could not weigh is why the change was made. It was written when the
    /// raster was believed byte-identical on every platform. It is not — antialiased coverage is
    /// floating-point arithmetic and arm64 disagrees with x64 — so "a frame type of its own" is
    /// exactly what makes two committee members' newsletters the same file. The cost the warning
    /// names is real, and these assertions are what hold it.</para>
    /// </summary>
    [Fact]
    public async Task OneUndoTakesItBackOff()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();
                int before = DrawingsOn(window);

                window.EmblemAnswerForTest = "blazing-star";
                await window.InsertEmblemAsync();
                Assert.Equal(before + 1, DrawingsOn(window));

                window.Undo();
                Assert.Equal(before, DrawingsOn(window));

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
                int before = DrawingsOn(window);

                window.EmblemAnswerForTest = "no-such-emblem";
                await window.InsertEmblemAsync();

                Assert.Equal(before, DrawingsOn(window));
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

    private static int PicturesOn(MainWindow window) =>
        window.PackageForTest!.Document.Pages.SelectMany(p => p.Blocks).OfType<ImageFrame>().Count();

    private static int DrawingsOn(MainWindow window) => Drawings(window).Count;

    private static System.Collections.Generic.List<VectorBlock> Drawings(MainWindow window) =>
        [.. window.PackageForTest!.Document.Pages.SelectMany(p => p.Blocks).OfType<VectorBlock>()];
}
