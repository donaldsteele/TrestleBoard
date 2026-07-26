using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Samples;
using TrestleBoard.Export.Pdf;
using TrestleBoard.Widgets;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// THE M8 acceptance (PLAN.md §11-M8): a whole five-page issue exports to a distribution-quality
/// PDF, under budget, with every page matching what the editor shows. All content fictional
/// (PLAN.md §0). See docs/M8-spec.md §5–§7.
/// </summary>
public sealed class IssueExportTests
{
    /// <summary>PLAN.md §11-M8: the real issues run 1.28–1.80 MB; the ceiling is 2.5 MB.</summary>
    private const long BudgetBytes = 2_500_000;

    private static readonly WidgetLayoutProvider Widgets = WidgetLayoutProvider.CreateDefault();

    private static DocumentRenderSource CreateSource()
    {
        TboardPackage package = SampleIssue.CreatePackage(PhotoJpeg());
        return DocumentRenderSource.Create(
            package.Document, package.Assets, SnapshotInfra.Store.Value, options: null, widgets: Widgets);
    }

    [Fact]
    public void TheIssueIsFivePages()
    {
        using DocumentRenderSource source = CreateSource();
        Assert.Equal(5, source.PageCount);
    }

    /// <summary>
    /// The cover essay is one story flowing from page 1 onto page 2 — the thing M8 exists to make
    /// work — and it must not run out of room anywhere.
    /// </summary>
    [Fact]
    public void TheCoverEssayFlowsOntoTheSecondPageAndFits()
    {
        using DocumentRenderSource source = CreateSource();

        Assert.True(source.TryGetFrameLayout("frame-essay-1", out Layout.FrameLayout? first));
        Assert.True(source.TryGetFrameLayout("frame-essay-2", out Layout.FrameLayout? second));
        Assert.NotEmpty(first!.Lines);
        Assert.NotEmpty(second!.Lines);
        Assert.False(source.IsOverset);

        // Continuous: the second frame picks up exactly where the first left off.
        Assert.Equal(first.Lines[^1].Source.EndChar, second.Lines[0].Source.StartChar);
    }

    [Fact]
    public void TextFlowsAroundThePhotoAndTheOfficersTable()
    {
        using DocumentRenderSource source = CreateSource();

        Assert.True(source.TryGetFrameLayout("frame-essay-1", out Layout.FrameLayout? page1));
        Assert.True(source.TryGetFrameLayout("frame-essay-2", out Layout.FrameLayout? page2));

        Assert.Contains(page1!.Lines, l => Narrowed(l, 504f));
        Assert.Contains(page2!.Lines, l => Narrowed(l, 504f));
    }

    /// <summary>
    /// One test per page, not a loop: a loop stops at the first missing baseline and CI then uploads
    /// only that page's candidate, so promoting a new fixture would take five round trips.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EachPageMatchesItsBaseline(int pageIndex)
    {
        using DocumentRenderSource source = CreateSource();
        AssertPageMatchesBaseline(source, pageIndex);
    }

    /// <summary>
    /// The size lever (docs/M8-spec.md §5.2). A photo handed to Skia as a raster is embedded
    /// losslessly and costs roughly four times what the same picture costs encoded.
    /// </summary>
    [Fact]
    public void TheExportedIssueIsWellUnderTheSizeBudget()
    {
        using DocumentRenderSource source = CreateSource();
        using var buffer = new MemoryStream();
        DocumentPdfExporter.Export(buffer, source, Metadata());

        Assert.True(
            buffer.Length <= BudgetBytes,
            $"exported issue is {buffer.Length / 1024} KB, over the {BudgetBytes / 1024} KB budget");
    }

    /// <summary>
    /// What the photo actually costs in the exported file. Recorded as a test rather than an
    /// assumption: the M8 spec originally claimed a 4× saving from handing Skia encoded images
    /// instead of rasters, and that number came from comparing a whole document against a bare
    /// one-image PDF. At the pixel budget the pipeline already applies, Skia's own compression of
    /// the downsampled raster is as good — so there is no lever here, and no mechanism.
    /// </summary>
    [Fact]
    public void ThePhotoIsASmallFractionOfTheExportedFile()
    {
        long withoutPhoto = ExportedSize(withPhoto: false);
        long withPhoto = ExportedSize(withPhoto: true);
        long cost = withPhoto - withoutPhoto;

        // A concrete ceiling, not a ratio: an embedded lossless raster of the same photo runs to
        // hundreds of KB, so this fails long before the file gets anywhere near the 2.5 MB budget.
        Assert.True(cost > 0, "the photo should cost something");
        Assert.True(
            cost < 100_000,
            $"the photo costs {cost / 1024} KB in the exported file — it was under 2 KB when this was "
            + "written, so something has started embedding it uncompressed");
    }

    private static long ExportedSize(bool withPhoto)
    {
        TboardPackage package = SampleIssue.CreatePackage(withPhoto ? PhotoJpeg() : null);
        using DocumentRenderSource source = DocumentRenderSource.Create(
            package.Document, package.Assets, SnapshotInfra.Store.Value, options: null, widgets: Widgets);
        using var buffer = new MemoryStream();
        DocumentPdfExporter.Export(buffer, source, Metadata());
        return buffer.Length;
    }

    /// <summary>Exporting must never write back to the stored original (M6's promise, still true).</summary>
    [Fact]
    public void ExportingLeavesTheStoredPhotoUntouched()
    {
        byte[] photo = PhotoJpeg();
        TboardPackage package = SampleIssue.CreatePackage(photo);
        using DocumentRenderSource source = DocumentRenderSource.Create(
            package.Document, package.Assets, SnapshotInfra.Store.Value, options: null, widgets: Widgets);

        using var buffer = new MemoryStream();
        DocumentPdfExporter.Export(buffer, source, Metadata());

        Assert.Equal(photo, package.Assets[SampleIssue.PhotoAssetName]);
    }

    [Fact]
    public void RenderingIsStableAcrossRepeatedPaints()
    {
        using DocumentRenderSource source = CreateSource();
        for (int page = 0; page < source.PageCount; page++)
        {
            Assert.Equal(
                DocumentSnapshotTests.RenderPagePng(source, page),
                DocumentSnapshotTests.RenderPagePng(source, page));
        }
    }

    // ---- poppler gates (Linux CI only) ---------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryExportedPageMatchesWhatTheEditorShows(int pageIndex)
    {
        RequireLinuxPoppler();
        using DocumentRenderSource source = CreateSource();
        string dir = ExportToDisk(source, out string pdfPath);
        byte[] screenPng = DocumentSnapshotTests.RenderPagePng(source, pageIndex);

        int page = pageIndex + 1;
        Run("pdftoppm", $"-r 72 -png -f {page} -l {page} -singlefile \"{pdfPath}\" \"{Path.Combine(dir, "page")}\"");
        byte[] pdfPng = File.ReadAllBytes(Path.Combine(dir, "page.png"));

        SnapshotInfra.BlockComparisonResult diff =
            SnapshotInfra.CompareDownsampled(pdfPng, screenPng, factor: 4, channelThreshold: 64);
        double fraction = (double)diff.BlocksOverThreshold / diff.TotalBlocks;
        Assert.True(
            fraction <= 0.002,
            $"page {page} diverges from the editor: {diff.BlocksOverThreshold}/{diff.TotalBlocks} blocks "
            + $"({fraction:P2}), max block diff {diff.MaxBlockDiff:F1}");
    }

    [Fact]
    public void TheIssueTextIsSelectableAndTheMetadataIsThere()
    {
        RequireLinuxPoppler();
        using DocumentRenderSource source = CreateSource();
        ExportToDisk(source, out string pdfPath);

        string text = Run("pdftotext", $"\"{pdfPath}\" -");
        Assert.Contains("STATED COMMUNICATION", text, StringComparison.Ordinal);
        Assert.Contains("Worshipful Master", text, StringComparison.Ordinal);
        Assert.Contains("Happy Birthday", text, StringComparison.Ordinal);
        Assert.Contains("22nd District", text, StringComparison.Ordinal);
        Assert.Contains("Summer Picnic", text, StringComparison.Ordinal);

        if (ToolExists("pdfinfo"))
        {
            string info = Run("pdfinfo", $"\"{pdfPath}\"");
            Assert.Contains("Placeholder Lodge", info, StringComparison.Ordinal);
            Assert.Matches(@"Pages:\s+5", info);
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static PdfMetadata Metadata() =>
        new("Placeholder Lodge Trestle Board — July 2026", "Placeholder Lodge No. 000", "Trestle board 2026-07");

    private static bool Narrowed(Layout.LineBox line, float fullWidth) =>
        line.Segments.Count > 1 || (line.Segments.Count == 1 && line.Segments[0].XRange.Width < fullWidth - 1f);

    private static void AssertPageMatchesBaseline(DocumentRenderSource source, int pageIndex)
    {
        byte[] actual = DocumentSnapshotTests.RenderPagePng(source, pageIndex);
        string name = $"issue-page{pageIndex + 1}";
        string baselinePath = Path.Combine(SnapshotInfra.BaselineDir, name + ".png");

        if (SnapshotInfra.UpdateBaselines)
        {
            Directory.CreateDirectory(SnapshotInfra.BaselineDir);
            File.WriteAllBytes(baselinePath, actual);
            return;
        }

        if (!File.Exists(baselinePath))
        {
            string candidate = SnapshotInfra.WriteActualArtifact(name, actual);
            Assert.Fail($"Baseline missing: {baselinePath}. Actual written to {candidate} " +
                        "(run with TRESTLEBOARD_UPDATE_BASELINES=1 or promote the CI artifact).");
        }

        byte[] expected = File.ReadAllBytes(baselinePath);
        SnapshotInfra.ComparisonResult diff = SnapshotInfra.ComparePixels(actual, expected);
        if (diff.DiffPixelCount > 0)
        {
            string artifact = SnapshotInfra.WriteActualArtifact(name, actual);
            Assert.Fail(
                $"Snapshot '{name}' differs from baseline: {diff.DiffPixelCount}/{diff.TotalPixels} pixels, "
                + $"max channel diff {diff.MaxChannelDiff}. Actual written to {artifact}");
        }
    }

    /// <summary>A photograph-like JPEG: gradients and detail, not flat fills. Fictional pixels.</summary>
    private static byte[] PhotoJpeg()
    {
        var info = new SKImageInfo(1600, 1100, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("raster surface");
        SKCanvas canvas = surface.Canvas;

        using (var sky = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, 700),
                [new SKColor(0xFFB9CBDA), new SKColor(0xFF8FA5B6)],
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(SKRect.Create(0, 0, 1600, 700), sky);
        }

        using (var ground = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 700),
                new SKPoint(0, 1100),
                [new SKColor(0xFF7E8A6B), new SKColor(0xFF4E5742)],
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(SKRect.Create(0, 700, 1600, 400), ground);
        }

        using (var wall = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(400, 0),
                new SKPoint(1200, 0),
                [new SKColor(0xFFB3A896), new SKColor(0xFF80776A)],
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(SKRect.Create(400, 260, 800, 460), wall);
        }

        // Detail, so the encoder has something to work on and the size figures mean something.
        for (int i = 0; i < 900; i++)
        {
            using var fleck = new SKPaint { Color = new SKColor((uint)(0xFF000000 | (i * 7919))) };
            canvas.DrawCircle((i * 37) % 1600, (i * 53) % 1100, 6, fleck);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 85)
            ?? throw new InvalidOperationException("jpeg encode");
        return data.ToArray();
    }

    private static string ExportToDisk(DocumentRenderSource source, out string pdfPath)
    {
        string dir = Path.Combine(Path.GetTempPath(), "trestleboard-issue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        pdfPath = Path.Combine(dir, "issue.pdf");
        using (FileStream stream = File.Create(pdfPath))
        {
            DocumentPdfExporter.Export(stream, source, Metadata());
        }

        return dir;
    }

    private static void RequireLinuxPoppler() =>
        Assert.SkipUnless(
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && ToolExists("pdftoppm") && ToolExists("pdftotext"),
            "PDF parity runs on Linux with poppler-utils only");

    private static bool ToolExists(string tool)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = tool,
                Arguments = "-v",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            process?.WaitForExit(10_000);
            return process is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static string Run(string tool, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = tool,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException($"failed to start {tool}");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{tool} {arguments} exited {process.ExitCode}: {stderr}");
        return stdout;
    }
}
