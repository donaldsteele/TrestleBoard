using TrestleBoard.Core.Container;
using TrestleBoard.PdfImport;

string repoRoot = FindRepoRoot();
string inputDir = Path.Combine(repoRoot, "Examples");
string outputDir = Path.Combine(inputDir, "tboard");

if (!Directory.Exists(inputDir))
{
    Console.WriteLine($"No Examples/ directory found at {inputDir}");
    return;
}

Directory.CreateDirectory(outputDir);

string[] pdfPaths = Directory.GetFiles(inputDir, "*.pdf", SearchOption.TopDirectoryOnly)
    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (pdfPaths.Length == 0)
{
    Console.WriteLine($"No PDFs found in {inputDir}");
    return;
}

foreach (string pdfPath in pdfPaths)
{
    string baseName = Path.GetFileNameWithoutExtension(pdfPath);
    Console.WriteLine($"Converting {baseName}...");

    TboardPackage package;
    try
    {
        package = PdfImporter.Convert(pdfPath, baseName);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAILED: {ex.Message}");
        continue;
    }

    string outPath = Path.Combine(outputDir, baseName + ".tboard");
    TboardContainer.SaveToFile(package, outPath);
    Console.WriteLine($"  -> {outPath}");
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "TrestleBoard.slnx")))
    {
        current = current.Parent;
    }

    return current?.FullName
        ?? throw new InvalidOperationException("Could not locate the repo root (TrestleBoard.slnx not found above the executable).");
}
