using System.IO.Compression;
using System.Text.Json.Nodes;
using TrestleBoard.Core.Container;
using Xunit;

namespace TrestleBoard.Core.Tests;

public sealed class ContainerTests
{
    [Fact]
    public void SaveLoadRoundTripsDocumentExactly()
    {
        TboardPackage original = Fixtures.BuildPackage();
        using var ms = new MemoryStream();
        TboardContainer.Save(original, ms);
        ms.Position = 0;

        TboardPackage loaded = TboardContainer.Load(ms);

        Assert.Equal(Fixtures.Snapshot(original.Document), Fixtures.Snapshot(loaded.Document));
        Assert.Equal(original.Assets.Keys, loaded.Assets.Keys);
        Assert.Equal(original.Assets["img-fixture.png"], loaded.Assets["img-fixture.png"]);
        Assert.Equal(original.Thumbnails["page-1.png"], loaded.Thumbnails["page-1.png"]);
        Assert.Equal(TboardManifest.CurrentFormatVersion, loaded.Manifest.FormatVersion);
    }

    [Fact]
    public void SaveIsDeterministicByteIdentical()
    {
        TboardPackage package = Fixtures.BuildPackage();
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        TboardContainer.Save(package, first);
        TboardContainer.Save(package, second);
        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public void SaveLoadSavePreservesBytes()
    {
        TboardPackage package = Fixtures.BuildPackage();
        using var first = new MemoryStream();
        TboardContainer.Save(package, first);
        first.Position = 0;
        TboardPackage reloaded = TboardContainer.Load(first);
        using var second = new MemoryStream();
        TboardContainer.Save(reloaded, second);
        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public void LoadPreservesUnknownPropertiesOnRoundTrip()
    {
        TboardPackage package = Fixtures.BuildPackage();
        using var ms = new MemoryStream();
        TboardContainer.Save(package, ms);

        // A future version adds properties at several levels; this build must not drop them.
        RewriteEntry(ms, "document.json", root =>
        {
            root["futureTopLevel"] = "keep-me";
            JsonObject block = root["pages"]![0]!["blocks"]![0]!.AsObject();
            block["futureBlockSetting"] = JsonValue.Create(42);
            JsonObject run = root["stories"]![0]!["paragraphs"]![0]!["runs"]![0]!.AsObject();
            run["futureRunFlag"] = JsonValue.Create(true);
        });
        RewriteEntry(ms, "styles.json", root =>
        {
            root["futureStylesTopLevel"] = "keep-me-too";
            root["styleSheet"]!["characterStyles"]![0]!.AsObject()["futureStyleKnob"] = "wide";
        });

        ms.Position = 0;
        TboardPackage loaded = TboardContainer.Load(ms);
        using var resaved = new MemoryStream();
        TboardContainer.Save(loaded, resaved);

        JsonObject doc = ReadEntry(resaved, "document.json");
        Assert.Equal("keep-me", doc["futureTopLevel"]!.GetValue<string>());
        Assert.Equal(42, doc["pages"]![0]!["blocks"]![0]!["futureBlockSetting"]!.GetValue<int>());
        Assert.True(doc["stories"]![0]!["paragraphs"]![0]!["runs"]![0]!["futureRunFlag"]!.GetValue<bool>());
        JsonObject styles = ReadEntry(resaved, "styles.json");
        Assert.Equal("keep-me-too", styles["futureStylesTopLevel"]!.GetValue<string>());
        Assert.Equal("wide", styles["styleSheet"]!["characterStyles"]![0]!["futureStyleKnob"]!.GetValue<string>());
    }

    [Fact]
    public void LoadPolymorphicBlocksSurviveWithDiscriminator()
    {
        TboardPackage package = Fixtures.BuildPackage();
        using var ms = new MemoryStream();
        TboardContainer.Save(package, ms);
        ms.Position = 0;
        TboardPackage loaded = TboardContainer.Load(ms);

        Model.Page page1 = loaded.Document.GetPage("page-1");
        Assert.IsType<Model.ImageFrame>(page1.GetBlock("img-1"));
        Assert.IsType<Model.TextBlock>(page1.GetBlock("text-1"));
        Assert.IsType<Model.ShapeBlock>(page1.GetBlock("rule-1"));
        Assert.IsType<Model.WidgetBlock>(loaded.Document.GetPage("page-2").GetBlock("widget-1"));
    }

    [Fact]
    public void LoadRejectsForeignFormatName()
    {
        TboardPackage package = Fixtures.BuildPackage();
        using var ms = new MemoryStream();
        TboardContainer.Save(package, ms);
        RewriteEntry(ms, "manifest.json", root => root["formatName"] = "not-trestleboard");
        ms.Position = 0;
        Assert.Throws<Migrations.UnsupportedFormatException>(() => TboardContainer.Load(ms));
    }

    [Fact]
    public void LoadRejectsFileFromNewerReader()
    {
        TboardPackage package = Fixtures.BuildPackage();
        using var ms = new MemoryStream();
        TboardContainer.Save(package, ms);
        RewriteEntry(ms, "manifest.json", root =>
        {
            root["formatVersion"] = "99.0.0";
            root["minReaderVersion"] = "99.0.0";
        });
        ms.Position = 0;
        var ex = Assert.Throws<Migrations.UnsupportedFormatException>(() => TboardContainer.Load(ms));
        Assert.Contains("newer version", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRejectsMissingEntry()
    {
        TboardPackage package = Fixtures.BuildPackage();
        using var ms = new MemoryStream();
        TboardContainer.Save(package, ms);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            zip.GetEntry("styles.json")!.Delete();
        }

        ms.Position = 0;
        Assert.Throws<Migrations.UnsupportedFormatException>(() => TboardContainer.Load(ms));
    }

    /// <summary>
    /// A widget written by a LATER version of TrestleBoard: unknown type, unknown properties inside
    /// its payload, unknown properties on its block. This build cannot draw or edit it, but it must
    /// keep the user's newsletter intact byte for byte (docs/M7-spec.md §2.1).
    /// </summary>
    [Fact]
    public void AWidgetFromANewerVersionSurvivesSaveLoadSaveVerbatim()
    {
        TboardPackage package = Fixtures.BuildPackage();
        using var ms = new MemoryStream();
        TboardContainer.Save(package, ms);

        RewriteEntry(ms, "document.json", root =>
        {
            JsonObject widget = root["pages"]![1]!["blocks"]!.AsArray()
                .First(b => b!["id"]!.GetValue<string>() == "widget-1")!.AsObject();
            widget["widgetType"] = "memorialPanel";
            widget["dataVersion"] = 9;
            widget["futureBlockKnob"] = "keep-me";
            widget["data"] = new JsonObject
            {
                ["brother"] = "A. Placeholder",
                ["futurePayloadKnob"] = JsonValue.Create(7),
            };
        });

        ms.Position = 0;
        TboardPackage loaded = TboardContainer.Load(ms);
        using var resaved = new MemoryStream();
        TboardContainer.Save(loaded, resaved);

        JsonObject widgetAfter = ReadEntry(resaved, "document.json")["pages"]![1]!["blocks"]!.AsArray()
            .First(b => b!["id"]!.GetValue<string>() == "widget-1")!.AsObject();
        Assert.Equal("memorialPanel", widgetAfter["widgetType"]!.GetValue<string>());
        Assert.Equal(9, widgetAfter["dataVersion"]!.GetValue<int>());
        Assert.Equal("keep-me", widgetAfter["futureBlockKnob"]!.GetValue<string>());
        Assert.Equal("A. Placeholder", widgetAfter["data"]!["brother"]!.GetValue<string>());
        Assert.Equal(7, widgetAfter["data"]!["futurePayloadKnob"]!.GetValue<int>());

        // And a second save is byte-identical, so an untouched newsletter never drifts.
        using var thirdSave = new MemoryStream();
        TboardContainer.Save(loaded, thirdSave);
        Assert.Equal(resaved.ToArray(), thirdSave.ToArray());
    }

    private static JsonObject ReadEntry(MemoryStream zipStream, string entryName)
    {
        zipStream.Position = 0;
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        using Stream s = zip.GetEntry(entryName)!.Open();
        return (JsonObject)JsonNode.Parse(s)!;
    }

    private static void RewriteEntry(MemoryStream zipStream, string entryName, Action<JsonObject> mutate)
    {
        zipStream.Position = 0;
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Update, leaveOpen: true);
        ZipArchiveEntry entry = zip.GetEntry(entryName)!;
        JsonObject root;
        using (Stream read = entry.Open())
        {
            root = (JsonObject)JsonNode.Parse(read)!;
        }

        mutate(root);
        entry.Delete();
        ZipArchiveEntry rewritten = zip.CreateEntry(entryName);
        using Stream write = rewritten.Open();
        using var writer = new StreamWriter(write);
        writer.Write(root.ToJsonString());
    }
}
