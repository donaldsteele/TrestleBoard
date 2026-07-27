using Avalonia;
using TrestleBoard.App.Integration;
using TrestleBoard.App.Startup;
using Velopack;

namespace TrestleBoard.App;

public static class Program
{
    /// <summary>What the command line asked for; read by <see cref="App"/> once Avalonia is up.</summary>
    public static StartupOptions Startup { get; private set; } = StartupOptions.Empty;

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    [STAThread]
    public static int Main(string[] args)
    {
        // MUST be first, before any window or file is touched: on an install, an update or an
        // uninstall, Velopack runs this same executable, does its work through these hooks and
        // exits. Anything above this line would run during a silent install (docs/M10-spec.md §1).
        VelopackApp builder = VelopackApp.Build()
            // Linux has no Velopack install hooks; first run is where its .desktop and MIME files
            // get written. On Windows this is belt-and-braces behind the install hook below.
            .OnFirstRun(_ => FileAssociations.Register(Environment.ProcessPath!));

        if (OperatingSystem.IsWindows())
        {
            builder = builder
                .OnAfterInstallFastCallback(_ => FileAssociations.Register(Environment.ProcessPath!))
                .OnAfterUpdateFastCallback(_ => FileAssociations.Register(Environment.ProcessPath!))
                .OnBeforeUninstallFastCallback(_ => FileAssociations.Unregister());
        }

        builder.Run();

        Startup = StartupOptions.Parse(args);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
}
