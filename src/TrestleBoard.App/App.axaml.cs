using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace TrestleBoard.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A real launch runs the startup flow — recovery first, then the start screen. The
            // headless tests set this true so they can drive the shell without a modal in the way.
            MainWindow.SuppressStartupForTest = false;
            var window = new MainWindow { StartupOptions = Program.Startup };
            desktop.MainWindow = window;
            WireFileActivation(window);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// macOS does not pass a double-clicked document on the command line — Launch Services sends it
    /// as an activation event instead, which Avalonia surfaces here (docs/M10-spec.md §3). Windows
    /// and Linux go through <see cref="Program.Startup"/>; this covers the third platform and also
    /// the case of dropping a second newsletter onto a running app.
    /// </summary>
    private void WireFileActivation(MainWindow window)
    {
        if (TryGetFeature(typeof(IActivatableLifetime)) is not IActivatableLifetime activatable)
        {
            return;
        }

        activatable.Activated += (_, e) =>
        {
            if (e is not FileActivatedEventArgs files)
            {
                return;
            }

            string? path = files.Files
                .Select(f => f.TryGetLocalPath())
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

            if (path is not null)
            {
                window.OpenDocumentFromPath(path);
            }
        };
    }
}
