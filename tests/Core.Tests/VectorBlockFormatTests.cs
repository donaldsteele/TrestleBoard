using System.Text.Json.Nodes;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Migrations;
using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// The format bump, which is the risky part of M72 and not the renderer (PLAN.md §11 M72 (b)).
///
/// <para><c>CurrentFormatVersion</c> had been <c>1.0.0</c> since M2 and <c>MigrationRunner.Chain</c>
/// was empty and had never executed a step. Raising the version without a route from 1.0.0 would
/// have thrown <c>UnsupportedFormatException</c> on <b>every newsletter in existence</b> — the whole
/// milestone's risk sits in these few lines, so it is tested harder than the drawing is.</para>
///
/// <para>Two promises are kept here. An existing newsletter opens, unchanged, and saves back to the
/// same bytes it arrived as (M61). And a newsletter that <i>does</i> contain a drawing says so, so
/// an older TrestleBoard refuses it in plain language instead of binding an unknown block
/// discriminator to nothing and silently dropping an emblem off somebody's cover.</para>
/// </summary>
public sealed class VectorBlockFormatTests
{
    // ---- the old file, which is the one that must not break -------------------------------------

    /// <summary>
    /// The claim the whole bump rests on: a newsletter written before M72 still opens. Written as
    /// raw JSON at version 1.0.0 rather than by round-tripping this build, so it stays a test about
    /// an old file even if the writer changes again.
    /// </summary>
    [Fact]
    public void ANewsletterWrittenAtTheOldVersionStillOpens()
    {
        using MemoryStream file = SavedAtVersion(Fixtures.BuildPackage(), "1.0.0");

        TboardPackage loaded = TboardContainer.Load(file);

        Assert.Equal(2, loaded.Document.Pages.Count);
        Assert.IsType<ImageFrame>(loaded.Document.GetPage("page-1").GetBlock("img-1"));
    }

    /// <summary>
    /// M61's rule, at the file level: open a document an older build wrote and save it back, and it
    /// is the same bytes. This is what the conditional stamp in <c>TboardContainer.Save</c> exists
    /// for — stamping every save with 1.1.0 would have changed the manifest of every newsletter the
    /// committee has, the first time each one was opened.
    /// </summary>
    [Fact]
    public void AnOldNewsletterSavesBackByteUnchanged()
    {
        // Written by this build, but written as 1.0.0 — which is exactly what the old build wrote,
        // through the same deterministic writer. Rewriting the zip by hand would have changed the
        // bytes for reasons that have nothing to do with the format version.
        using var original = new MemoryStream();
        TboardContainer.Save(Fixtures.BuildPackage(), original);
        Assert.Equal("1.0.0", ReadEntry(original, "manifest.json")["formatVersion"]!.GetValue<string>());

        original.Position = 0;
        TboardPackage loaded = TboardContainer.Load(original);
        using var resaved = new MemoryStream();
        TboardContainer.Save(loaded, resaved);

        Assert.Equal(original.ToArray(), resaved.ToArray());
    }

    /// <summary>
    /// A baked emblem from before M72 is an ordinary picture and stays one. Detecting and upgrading
    /// them was considered and refused: the emblem's id was deliberately not stored with the frame
    /// (docs/M65-spec.md §9), so recognising one would need pixel matching, and getting it wrong
    /// would silently rewrite somebody's newsletter. The migration touches nothing, and this is the
    /// test that says so out loud.
    /// </summary>
    [Fact]
    public void AnEmblemBakedIntoAPictureBeforeM72IsLeftExactlyAsItIs()
    {
        var manifest = new JsonObject
        {
            ["formatName"] = "trestleboard",
            ["formatVersion"] = "1.0.0",
            ["minReaderVersion"] = "1.0.0",
        };
        JsonObject body = ReadEntry(Saved(Fixtures.BuildPackage()), "document.json");
        JsonObject styles = ReadEntry(Saved(Fixtures.BuildPackage()), "styles.json");
        string bodyBefore = body.ToJsonString();
        string stylesBefore = styles.ToJsonString();

        MigrationRunner.Run(manifest, body, styles);

        Assert.Equal(bodyBefore, body.ToJsonString());
        Assert.Equal(stylesBefore, styles.ToJsonString());
    }

    /// <summary>
    /// The chain has a route from the only version this app has ever written. A step missing here
    /// is not a failed feature — it is every existing newsletter refusing to open, which is why it
    /// is asserted directly rather than only through a round trip.
    /// </summary>
    [Fact]
    public void TheChainKnowsHowToReachTodayFromEveryVersionEverWritten()
    {
        var manifest = new JsonObject
        {
            ["formatName"] = "trestleboard",
            ["formatVersion"] = TboardManifest.BaseFormatVersion,
            ["minReaderVersion"] = TboardManifest.BaseFormatVersion,
        };

        MigrationRunner.Run(manifest, new JsonObject(), new JsonObject());

        Assert.Equal(
            TboardManifest.CurrentFormatVersion, manifest["formatVersion"]!.GetValue<string>());
    }

    // ---- the new file ---------------------------------------------------------------------------

    /// <summary>
    /// The version is a function of what is in the document, not of what the build can write. Two
    /// saves of the same package, one with a drawing on a page and one without.
    /// </summary>
    [Fact]
    public void TheVersionFollowsTheDocumentAndNotTheBuild()
    {
        TboardPackage package = Fixtures.BuildPackage();
        Assert.Equal("1.0.0", ManifestOf(package)["formatVersion"]!.GetValue<string>());
        Assert.Equal("1.0.0", ManifestOf(package)["minReaderVersion"]!.GetValue<string>());

        package.Document.GetPage("page-1").Blocks.Add(Drawing());

        Assert.Equal("1.1.0", ManifestOf(package)["formatVersion"]!.GetValue<string>());
        Assert.Equal("1.1.0", ManifestOf(package)["minReaderVersion"]!.GetValue<string>());
    }

    /// <summary>
    /// And an older TrestleBoard — one whose <c>CurrentFormatVersion</c> is 1.0.0 — meets that
    /// <c>minReaderVersion</c> and says the sentence written for it, rather than quietly losing the
    /// drawing. Simulated by asking the runner to reach a target it cannot, which is exactly the
    /// comparison the old build would make.
    /// </summary>
    [Fact]
    public void AnOlderTrestleBoardIsToldToUpdateRatherThanLosingTheDrawing()
    {
        TboardPackage package = Fixtures.BuildPackage();
        package.Document.GetPage("page-1").Blocks.Add(Drawing());
        using var saved = new MemoryStream();
        TboardContainer.Save(package, saved);

        JsonObject manifest = ManifestOf(package);
        Assert.True(
            Version.Parse(manifest["minReaderVersion"]!.GetValue<string>())
            > Version.Parse(TboardManifest.BaseFormatVersion),
            "a build that predates the vector block would have opened this file and dropped it");

        // The message the pre-M72 reader would produce, produced here by the same code path.
        manifest["minReaderVersion"] = "99.0.0";
        var ex = Assert.Throws<UnsupportedFormatException>(
            () => MigrationRunner.Run(manifest, new JsonObject(), new JsonObject()));
        Assert.Contains("newer version", ex.Message, StringComparison.Ordinal);
        Assert.Contains("update", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A drawing survives the container with its geometry, its ink and its provenance.</summary>
    [Fact]
    public void ADrawingRoundTripsWithItsGeometryAndItsProvenance()
    {
        TboardPackage package = Fixtures.BuildPackage();
        package.Document.GetPage("page-1").Blocks.Add(Drawing());

        using var saved = new MemoryStream();
        TboardContainer.Save(package, saved);
        saved.Position = 0;
        TboardPackage loaded = TboardContainer.Load(saved);

        var drawing = Assert.IsType<VectorBlock>(loaded.Document.GetPage("page-1").GetBlock("drawing-1"));
        Assert.Equal(2, drawing.Parts.Count);
        Assert.Equal("M0,0 L100,100", drawing.Parts[0].PathData);
        Assert.Equal(4, drawing.Parts[0].StrokeWidth);
        Assert.Equal(0, drawing.Parts[1].StrokeWidth);
        Assert.Equal(100, drawing.ViewBoxWidth);
        Assert.Equal(50, drawing.ViewBoxHeight);
        Assert.Equal(VectorBlock.DefaultInkArgb, drawing.InkArgb);
        Assert.Equal("A placeholder drawing", drawing.AltText);
        Assert.Equal("Under the drawing", drawing.Caption);
        Assert.Equal("placeholder-emblem", drawing.EmblemId);
        Assert.Equal("0123abcd", drawing.EmblemFingerprint);
    }

    /// <summary>
    /// A newsletter with a drawing in it saves to the same bytes twice, and re-saving one that has
    /// been through the reader changes nothing. There is no asset, so there is nothing in the
    /// container that a processor architecture could disagree about (§12 gate 25).
    /// </summary>
    [Fact]
    public void ADrawingSavesToTheSameBytesEveryTime()
    {
        TboardPackage package = Fixtures.BuildPackage();
        package.Document.GetPage("page-1").Blocks.Add(Drawing());

        using var first = new MemoryStream();
        TboardContainer.Save(package, first);
        first.Position = 0;
        TboardPackage reloaded = TboardContainer.Load(first);
        using var second = new MemoryStream();
        TboardContainer.Save(reloaded, second);

        Assert.Equal(first.ToArray(), second.ToArray());
    }

    /// <summary>
    /// A drawing written by a LATER version of TrestleBoard, with properties this build has never
    /// heard of on the block and on one of its parts. This build must keep them, exactly as it does
    /// for a widget from the future (docs/M7-spec.md §2.1) — the drawing is new, but the promise is
    /// the old one.
    /// </summary>
    [Fact]
    public void ADrawingFromANewerVersionSurvivesSaveLoadSaveVerbatim()
    {
        TboardPackage package = Fixtures.BuildPackage();
        package.Document.GetPage("page-1").Blocks.Add(Drawing());
        using var ms = new MemoryStream();
        TboardContainer.Save(package, ms);

        RewriteEntry(ms, "document.json", root =>
        {
            JsonObject drawing = root["pages"]![0]!["blocks"]!.AsArray()
                .First(b => b!["id"]!.GetValue<string>() == "drawing-1")!.AsObject();
            drawing["futureBlockKnob"] = "keep-me";
            drawing["parts"]![0]!.AsObject()["futurePartKnob"] = JsonValue.Create(7);
        });

        ms.Position = 0;
        TboardPackage loaded = TboardContainer.Load(ms);
        using var resaved = new MemoryStream();
        TboardContainer.Save(loaded, resaved);

        JsonObject after = ReadEntry(resaved, "document.json")["pages"]![0]!["blocks"]!.AsArray()
            .First(b => b!["id"]!.GetValue<string>() == "drawing-1")!.AsObject();
        Assert.Equal("keep-me", after["futureBlockKnob"]!.GetValue<string>());
        Assert.Equal(7, after["parts"]![0]!["futurePartKnob"]!.GetValue<int>());
    }

    /// <summary>
    /// M74 (d): the version stamp walks the page masters too. A master's blocks render through the
    /// same block switch as a page's and carry-forward walks them, so a drawing on a master stamped
    /// 1.0.0 would let a pre-M72 reader past the version gate and into the very crash the stamp
    /// exists to prevent. This defect is latent — no UI path puts a drawing on a master today — so
    /// this test, not a user, is the only thing standing in front of it.
    /// </summary>
    [Fact]
    public void ADrawingOnAPageMasterStampsTheVersionToo()
    {
        TboardPackage package = Fixtures.BuildPackage();
        package.Document.GetMaster("master-1").Blocks.Add(Drawing());

        JsonObject manifest = ManifestOf(package);
        Assert.Equal("1.1.0", manifest["formatVersion"]!.GetValue<string>());
        Assert.Equal("1.1.0", manifest["minReaderVersion"]!.GetValue<string>());
    }

    // ---- plumbing --------------------------------------------------------------------------------

    private static VectorBlock Drawing() => new()
    {
        Id = "drawing-1",
        FrameRect = new RectPt(72f, 500f, 120f, 60f),
        ZOrder = 20,
        WrapMode = WrapMode.Rectangle,
        WrapMarginPt = 6f,
        ViewBoxWidth = 100,
        ViewBoxHeight = 50,
        Parts =
        [
            new VectorPart { PathData = "M0,0 L100,100", StrokeWidth = 4 },
            new VectorPart { PathData = "M10,10 L20,20 L10,20 Z" },
        ],
        AltText = "A placeholder drawing",
        Caption = "Under the drawing",
        EmblemId = "placeholder-emblem",
        EmblemFingerprint = "0123abcd",
    };

    private static JsonObject ManifestOf(TboardPackage package) =>
        ReadEntry(Saved(package), "manifest.json");

    private static MemoryStream Saved(TboardPackage package)
    {
        var ms = new MemoryStream();
        TboardContainer.Save(package, ms);
        return ms;
    }

    /// <summary>
    /// The package as an older build would have written it: this build's bytes with the manifest
    /// forced back to the given version.
    /// </summary>
    private static MemoryStream SavedAtVersion(TboardPackage package, string version)
    {
        var ms = new MemoryStream();
        TboardContainer.Save(package, ms);
        RewriteEntry(ms, "manifest.json", root =>
        {
            root["formatVersion"] = version;
            root["minReaderVersion"] = version;
        });
        ms.Position = 0;
        return ms;
    }

    private static JsonObject ReadEntry(MemoryStream zipStream, string entryName)
    {
        zipStream.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(
            zipStream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        using Stream s = zip.GetEntry(entryName)!.Open();
        return (JsonObject)JsonNode.Parse(s)!;
    }

    private static void RewriteEntry(MemoryStream zipStream, string entryName, Action<JsonObject> mutate)
    {
        zipStream.Position = 0;
        JsonObject root = ReadEntry(zipStream, entryName);
        mutate(root);

        zipStream.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(
            zipStream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true);
        zip.GetEntry(entryName)!.Delete();
        using Stream write = zip.CreateEntry(entryName).Open();
        using var writer = new StreamWriter(write);
        writer.Write(root.ToJsonString());
    }
}
