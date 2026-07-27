using System.Globalization;

namespace TrestleBoard.Screenshots;

/// <summary>
/// Generates the images under <c>docs/images/</c> (PLAN.md §11 M15).
///
/// Run it by hand, from the repository root:
/// <code>
///   dotnet run --project tools/TrestleBoard.Screenshots
///   dotnet run --project tools/TrestleBoard.Screenshots -- --only hero-issue-page1,font-picker
/// </code>
///
/// It is deliberately not a test and deliberately not run by CI. Gating on pixels would either
/// block every unrelated UI tweak on a re-bake, or need a tolerance — at which point it stops
/// being a gate at all. <c>--only</c> is the churn control instead: PNG encoder output is not
/// stable across SkiaSharp versions, so regenerate by name and rarely.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(Options.Usage);
            return 0;
        }

        Directory.CreateDirectory(options.OutputDirectory);

        IReadOnlyList<Shot> shots = ShotList.All;
        if (options.Only.Count > 0)
        {
            string[] unknown = options.Only
                .Where(name => !shots.Any(s => string.Equals(s.Name, name, StringComparison.Ordinal)))
                .ToArray();
            if (unknown.Length > 0)
            {
                Console.Error.WriteLine(
                    $"No such shot: {string.Join(", ", unknown)}. Known shots: "
                    + string.Join(", ", shots.Select(s => s.Name)));
                return 2;
            }

            shots = shots.Where(s => options.Only.Contains(s.Name)).ToArray();
        }

        using var harness = ScreenshotHarness.Start();

        int written = 0;
        int skipped = 0;
        int failed = 0;

        foreach (Shot shot in shots)
        {
            // A slipping milestone must degrade this harness rather than block it (PLAN.md §11,
            // sizing notes). Each shot names the milestone it needs; a missing one skips with a
            // printed reason, and the README section goes with it rather than leaving a broken
            // image behind.
            if (!MilestoneGate.IsPresent(shot.NeedsMilestone, out string reason))
            {
                Console.WriteLine($"  skip  {shot.Name} — {reason}");
                skipped++;
                continue;
            }

            string path = Path.Combine(options.OutputDirectory, shot.Name + ".png");
            try
            {
                byte[] png = harness.Capture(shot);
                File.WriteAllBytes(path, png);
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  write {shot.Name}.png ({png.Length / 1024} KB)"));
                written++;
            }
#pragma warning disable CA1031 // One broken shot must not cost the other seventeen.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Console.Error.WriteLine($"  FAIL  {shot.Name} — {ex.GetType().Name}: {ex.Message}");
                failed++;
            }
        }

        // The index lists what is actually on disk, not what was asked for: a shot skipped for a
        // missing milestone must not leave a link to an image nobody generated.
        ImageIndex.Write(
            options.OutputDirectory,
            [.. ShotList.All.Where(s => File.Exists(Path.Combine(options.OutputDirectory, s.Name + ".png")))],
            DateOnly.FromDateTime(DateTime.Now));

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{written} written, {skipped} skipped, {failed} failed → {options.OutputDirectory}"));
        return failed == 0 ? 0 : 1;
    }
}

/// <summary>What the command line asked for.</summary>
internal sealed record Options(string OutputDirectory, HashSet<string> Only, bool ShowHelp)
{
    public const string Usage = """
        Usage: dotnet run --project tools/TrestleBoard.Screenshots -- [options]

          --only a,b,c   Regenerate only the named shots (default: all of them).
          --out <dir>    Where to write them (default: docs/images beside the repository root).
          --list         Print the shot list and exit.
          --help         This text.
        """;

    public static Options Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string output = DefaultOutputDirectory();
        var only = new HashSet<string>(StringComparer.Ordinal);
        bool help = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    help = true;
                    break;
                case "--list":
                    foreach (Shot shot in ShotList.All)
                    {
                        Console.WriteLine($"{shot.Name,-28} {shot.Kind,-9} {shot.Caption}");
                    }

                    help = true;
                    break;
                case "--only":
                    foreach (string name in Next(args, ref i, "--only").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        only.Add(name);
                    }

                    break;
                case "--out":
                    output = Path.GetFullPath(Next(args, ref i, "--out"));
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new Options(output, only, help);
    }

    private static string Next(string[] args, ref int i, string flag) =>
        ++i < args.Length ? args[i] : throw new ArgumentException($"{flag} needs a value.");

    /// <summary>
    /// Walks up from the binary to the repository root, so the tool works from anywhere without
    /// the caller having to know where <c>docs/images</c> is.
    /// </summary>
    private static string DefaultOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TrestleBoard.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? Path.GetFullPath("docs/images")
            : Path.Combine(directory.FullName, "docs", "images");
    }
}
