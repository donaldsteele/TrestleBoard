using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TrestleBoard.App.Integration;

/// <summary>
/// Makes a `.tboard` file open in TrestleBoard when the user double-clicks it (PLAN.md §11-M10,
/// docs/M10-spec.md §3). Registered from Velopack's install hook and removed from its uninstall
/// hook, per user — never machine-wide, so no elevation prompt ever appears.
/// <para>
/// macOS is absent on purpose: there the association is declared by
/// <c>build/macos/Info.plist</c> inside the .app bundle, which is the only mechanism Launch
/// Services honours.
/// </para>
/// </summary>
public static class FileAssociations
{
    /// <summary>The Windows ProgId and the Linux MIME type — both are identity, so both are fixed.</summary>
    public const string ProgId = "TrestleBoard.Newsletter";

    public const string MimeType = "application/x-tboard";

    public const string FriendlyName = "TrestleBoard newsletter";

    /// <summary>Best-effort by design: a failed association is a nuisance, a failed install is not.</summary>
    public static void Register(string executablePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                RegisterWindows(executablePath);
            }
            else if (OperatingSystem.IsLinux())
            {
                RegisterLinux(executablePath);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }

    public static void Unregister()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                UnregisterWindows();
            }
            else if (OperatingSystem.IsLinux())
            {
                UnregisterLinux();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }

    /// <summary>
    /// The value Explorer runs. Both the executable and the `%1` are quoted: lodge machines put
    /// documents in "My Documents" and the app under "Program Files", and an unquoted command line
    /// loses everything after the first space in either.
    /// </summary>
    public static string WindowsOpenCommand(string executablePath) => $"\"{executablePath}\" \"%1\"";

    /// <summary>
    /// `%f` (a single local file), not `%F` or `%u`: the app opens one newsletter at a time and
    /// cannot fetch a URL.
    /// </summary>
    public static string DesktopEntry(string executablePath) =>
        $"""
        [Desktop Entry]
        Type=Application
        Name=TrestleBoard
        GenericName=Newsletter editor
        Comment=Make the lodge trestle board newsletter
        Exec="{executablePath}" %f
        Terminal=false
        Categories=Office;Publishing;
        MimeType={MimeType};

        """;

    public static string MimePackageXml() =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
          <mime-type type="{MimeType}">
            <comment>{FriendlyName}</comment>
            <glob pattern="*{Startup.StartupOptions.DocumentExtension}"/>
          </mime-type>
        </mime-info>

        """;

    [SupportedOSPlatform("windows")]
    private static void RegisterWindows(string executablePath)
    {
        using (RegistryKey extension = Registry.CurrentUser.CreateSubKey(
            $@"Software\Classes\{Startup.StartupOptions.DocumentExtension}"))
        {
            extension.SetValue(null, ProgId);
        }

        using RegistryKey progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}");
        progId.SetValue(null, FriendlyName);

        using (RegistryKey icon = progId.CreateSubKey("DefaultIcon"))
        {
            icon.SetValue(null, $"\"{executablePath}\",0");
        }

        using RegistryKey command = progId.CreateSubKey(@"shell\open\command");
        command.SetValue(null, WindowsOpenCommand(executablePath));
    }

    [SupportedOSPlatform("windows")]
    private static void UnregisterWindows()
    {
        Registry.CurrentUser.DeleteSubKeyTree(
            $@"Software\Classes\{Startup.StartupOptions.DocumentExtension}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
    }

    [SupportedOSPlatform("linux")]
    private static void RegisterLinux(string executablePath)
    {
        string share = LinuxDataHome();
        string applications = Path.Combine(share, "applications");
        string mimePackages = Path.Combine(share, "mime", "packages");
        Directory.CreateDirectory(applications);
        Directory.CreateDirectory(mimePackages);

        File.WriteAllText(Path.Combine(mimePackages, "trestleboard.xml"), MimePackageXml());
        File.WriteAllText(Path.Combine(applications, "trestleboard.desktop"), DesktopEntry(executablePath));

        // The desktop only notices new types once the caches are rebuilt. Absent on a minimal
        // system, which is why the failure is swallowed rather than reported.
        RunQuietly("update-mime-database", Path.Combine(share, "mime"));
        RunQuietly("update-desktop-database", applications);
    }

    [SupportedOSPlatform("linux")]
    private static void UnregisterLinux()
    {
        string share = LinuxDataHome();
        string applications = Path.Combine(share, "applications");
        File.Delete(Path.Combine(share, "mime", "packages", "trestleboard.xml"));
        File.Delete(Path.Combine(applications, "trestleboard.desktop"));
        RunQuietly("update-mime-database", Path.Combine(share, "mime"));
        RunQuietly("update-desktop-database", applications);
    }

    private static string LinuxDataHome()
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : xdg;
    }

    private static void RunQuietly(string fileName, string argument)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(fileName, argument)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process?.WaitForExit(milliseconds: 5000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
        }
    }
}
