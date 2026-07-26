using System.Diagnostics;
using System.Runtime.InteropServices;
using TrestleBoard.Export.Pdf;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// M3 WYSIWYG gate: the sample document's PDF export, rasterized by poppler, must match the
/// screen-path render of the SAME DocumentRenderSource. Tolerant comparison (different
/// rasterizers); the strict identity proof is the PNG snapshot suite. Linux CI only.
/// </summary>
public sealed class DocumentPdfParityTests
{
    private static void RequireLinuxPoppler()
    {
        Assert.SkipUnless(
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && ToolExists("pdftoppm") && ToolExists("pdftotext"),
            "PDF parity runs on Linux with poppler-utils only");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PdfPageMatchesScreenRenderWithinTolerance(int pageIndex)
    {
        RequireLinuxPoppler();
        using DocumentRenderSource source = DocumentSnapshotTests.CreateSource();
        string dir = ExportPdf(source, out string pdfPath);
        byte[] screenPng = DocumentSnapshotTests.RenderPagePng(source, pageIndex);

        int page = pageIndex + 1;
        Run("pdftoppm", $"-r 72 -png -f {page} -l {page} -singlefile \"{pdfPath}\" \"{Path.Combine(dir, "page")}\"");
        byte[] pdfPng = File.ReadAllBytes(Path.Combine(dir, "page.png"));

        // Same tolerance rationale as the M1 parity gate (see PdfParityTests).
        SnapshotInfra.BlockComparisonResult diff =
            SnapshotInfra.CompareDownsampled(pdfPng, screenPng, factor: 4, channelThreshold: 64);
        double fraction = (double)diff.BlocksOverThreshold / diff.TotalBlocks;
        Assert.True(
            fraction <= 0.002,
            $"PDF page {page} diverges from screen render: {diff.BlocksOverThreshold}/{diff.TotalBlocks} "
            + $"blocks over threshold ({fraction:P2}), max block diff {diff.MaxBlockDiff:F1}");
    }

    [Fact]
    public void PdfTextIsSelectableAcrossPages()
    {
        RequireLinuxPoppler();
        using DocumentRenderSource source = DocumentSnapshotTests.CreateSource();
        ExportPdf(source, out string pdfPath);
        string text = Run("pdftotext", $"\"{pdfPath}\" -");
        Assert.Contains("Placeholder Lodge", text, StringComparison.Ordinal);
        Assert.Contains("Worshipful Master", text, StringComparison.Ordinal);
    }

    private static string ExportPdf(DocumentRenderSource source, out string pdfPath)
    {
        string dir = Path.Combine(Path.GetTempPath(), "trestleboard-doc-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        pdfPath = Path.Combine(dir, "sample.pdf");
        using (FileStream stream = File.Create(pdfPath))
        {
            DocumentPdfExporter.Export(
                stream,
                source,
                new PdfMetadata("Sample Trestle Board", "TrestleBoard Tests", "M3 viewer parity fixture"));
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
