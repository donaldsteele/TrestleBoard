namespace TrestleBoard.App.Startup;

/// <summary>
/// What the command line asked for (docs/M10-spec.md §3). Double-clicking a `.tboard` file hands
/// the path to the app as an argument on Windows and Linux, so this is the front door of the file
/// association — it is pure and separately tested because the association is otherwise only
/// observable by installing the app.
/// </summary>
/// <param name="DocumentPath">The newsletter to open, or null to run the normal start flow.</param>
/// <param name="SkipUpdateCheck">Set by <c>--no-update-check</c>; also used by the test scripts.</param>
public sealed record StartupOptions(string? DocumentPath, bool SkipUpdateCheck)
{
    public const string DocumentExtension = ".tboard";

    public static StartupOptions Empty { get; } = new(null, SkipUpdateCheck: false);

    /// <summary>
    /// Takes the first `.tboard` argument and ignores everything else. Velopack passes its own
    /// switches (<c>--veloapp-install</c> and friends) through the same argv, and a shell can pass
    /// anything at all, so an unrecognised argument must never stop the app from starting.
    /// </summary>
    public static StartupOptions Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
        {
            return Empty;
        }

        string? document = null;
        bool skipUpdates = false;

        foreach (string argument in args)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            if (argument.Equals("--no-update-check", StringComparison.OrdinalIgnoreCase))
            {
                skipUpdates = true;
                continue;
            }

            if (argument.StartsWith('-'))
            {
                continue;
            }

            if (document is null
                && argument.EndsWith(DocumentExtension, StringComparison.OrdinalIgnoreCase))
            {
                document = argument.Trim('"');
            }
        }

        return new StartupOptions(document, skipUpdates);
    }
}
