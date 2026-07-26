using System.Diagnostics;
using System.Runtime.InteropServices;
using TrestleBoard.Export.Pdf;
using TrestleBoard.Layout;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// The WYSIWYG guarantee proxy (PLAN.md §8): export the acceptance fixture to PDF, rasterize
/// with poppler's pdftoppm, and compare against the screen PNG. Poppler's rasterizer is not
/// Skia's, so this comparison is TOLERANT — the strict cross-OS identity proof is the PNG
/// snapshot suite. Runs only on Linux CI where poppler-utils is installed.
/// </summary>
public sealed class PdfParityTests
{
    private static void RequireLinuxPoppler()
    {
        Assert.SkipUnless(
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && ToolExists("pdftoppm") && ToolExists("pdftotext"),
            "PDF parity runs on Linux with poppler-utils only");
    }

    [Fact]
    public void PdfMatchesScreenRenderWithinTolerance()
    {
        RequireLinuxPoppler();
        string dir = ExportAcceptancePdf(out string pdfPath);
        byte[] screenPng = SnapshotInfra.RenderFixturePng(SnapshotInfra.AcceptanceFixture());

        // 72 dpi: 1 typographic point = 1 pixel, matching the screen render's scale of 1.
        Run("pdftoppm", $"-r 72 -png -f 1 -l 1 -singlefile \"{pdfPath}\" \"{Path.Combine(dir, "page")}\"");
        byte[] pdfPng = File.ReadAllBytes(Path.Combine(dir, "page.png"));

        SnapshotInfra.ComparisonResult diff = SnapshotInfra.ComparePixels(pdfPng, screenPng);
        double fraction = (double)diff.DiffPixelCount / diff.TotalPixels;
        Assert.True(
            diff.MaxChannelDiff <= 96 && fraction <= 0.02,
            $"PDF render diverges from screen render: {diff.DiffPixelCount}/{diff.TotalPixels} pixels "
            + $"({fraction:P2}), max channel diff {diff.MaxChannelDiff}");
    }

    [Fact]
    public void PdfTextIsSelectable()
    {
        RequireLinuxPoppler();
        ExportAcceptancePdf(out string pdfPath);
        string text = Run("pdftotext", $"\"{pdfPath}\" -");
        Assert.Contains("Placeholder Lodge", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfFontsAreSubsetEmbedded()
    {
        RequireLinuxPoppler();
        Assert.SkipUnless(ToolExists("pdffonts"), "pdffonts not available");
        ExportAcceptancePdf(out string pdfPath);
        string report = Run("pdffonts", $"\"{pdfPath}\"");

        Assert.Contains("SourceSerif4", report, StringComparison.Ordinal);
        string[] rows = report.Split('\n', StringSplitOptions.RemoveEmptyEntries)[2..];
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Contains("yes yes", row, StringComparison.Ordinal));
    }

    private static string ExportAcceptancePdf(out string pdfPath)
    {
        string dir = Path.Combine(Path.GetTempPath(), "trestleboard-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        pdfPath = Path.Combine(dir, "acceptance.pdf");
        LayoutResult layout = SnapshotInfra.AcceptanceFixture();
        var exporter = new PdfExporter(new PageRenderer());
        using (FileStream stream = File.Create(pdfPath))
        {
            exporter.Export(
                stream,
                [new PdfPage(SnapshotInfra.PageWidthPt, SnapshotInfra.PageHeightPt, layout)],
                new PdfMetadata("M1 Acceptance Fixture", "TrestleBoard Tests", "Layout engine parity fixture"));
        }

        Assert.True(new FileInfo(pdfPath).Length > 0);
        return dir;
    }

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
