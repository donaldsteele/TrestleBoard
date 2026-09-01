using System.Diagnostics;
using System.Globalization;

namespace TrestleBoard.App.Integration;

/// <summary>What happened when an iPhone photograph was handed over.</summary>
/// <param name="Converted">The JPEG bytes, or null when nothing could be done.</param>
/// <param name="WhatToTell">
/// The sentence for the user — either what TrestleBoard did, or what they can do instead. Never
/// empty: this is the one place in the app whose whole job is to explain a format.
/// </param>
public readonly record struct HeicResult(byte[]? Converted, string WhatToTell)
{
    public bool Worked => Converted is not null;
}

/// <summary>
/// Turning an iPhone photograph into something Skia can read (PLAN.md §11 M80).
///
/// <para>A seam rather than a static call, so the two paths that matter can both be driven in a
/// test: a computer that can convert, and a computer that cannot. Neither is reproducible on CI —
/// the Linux runner has no converter and the Windows one may or may not have the codec installed —
/// which is precisely why the DECISION has to be testable apart from the machine.</para>
/// </summary>
internal interface IHeicConverter
{
    /// <summary>
    /// Whether this computer can do it at all. False means the picker leaves iPhone photographs out
    /// of its filter and the card tells the user what to do on the phone instead.
    /// </summary>
    bool CanConvert { get; }

    /// <summary>The JPEG bytes, or null if the attempt failed.</summary>
    byte[]? ToJpeg(byte[] heic);
}

/// <summary>
/// The plain-language half, which is the same on every operating system and therefore testable on
/// all of them (PLAN.md §11 M80).
///
/// <para><b>"My phone photo will not open" is the sharpest daily complaint this app produces.</b>
/// Until now the answer was a card reading "TrestleBoard could not read that file as a picture.
/// JPEG and PNG files work best" — which is true, useless, and leaves a committee member believing
/// their photograph is broken. It is not broken. It is in a format Apple chose and Skia does not
/// read, and there is something to be done about it on two of the three operating systems.</para>
///
/// <para><b>The converted JPEG is what goes into the newsletter, never the HEIC.</b> Keeping the
/// original as the "real" asset would mean the package opened fine on the machine that converted it
/// and failed on the secretary's, which is a worse failure than the one being fixed — and "Fix the
/// photo" would then be reaching for bytes Skia cannot decode.</para>
/// </summary>
internal static class HeicConversion
{
    /// <summary>What the user is told when the conversion worked.</summary>
    internal const string Converted =
        "That is an iPhone photograph, in a format this computer cannot read on its own. "
        + "TrestleBoard has converted it, and the newsletter now holds an ordinary picture.";

    /// <summary>What the user is told when it cannot be done here.</summary>
    internal const string CannotConvert =
        "That is an iPhone photograph, in a format this computer cannot read. Nothing is wrong with "
        + "the photograph. On the phone, open Settings, then Camera, then Formats, and choose "
        + "“Most Compatible” — photographs taken after that will open here. For this one, "
        + "email it to yourself from the phone and save what arrives.";

    /// <summary>What the user is told when the attempt was made and failed.</summary>
    internal const string ConversionFailed =
        "That is an iPhone photograph, and TrestleBoard tried to convert it and could not. "
        + "Email it to yourself from the phone and save what arrives — that gives an ordinary "
        + "picture this computer can read.";

    /// <summary>
    /// The decision, apart from the machine. Given a converter and some bytes, what happens and
    /// what the user is told.
    /// </summary>
    internal static HeicResult Handle(IHeicConverter converter, byte[] heic)
    {
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(heic);

        if (!converter.CanConvert)
        {
            return new HeicResult(null, CannotConvert);
        }

        byte[]? jpeg = converter.ToJpeg(heic);
        return jpeg is { Length: > 0 }
            ? new HeicResult(jpeg, Converted)
            : new HeicResult(null, ConversionFailed);
    }

    /// <summary>The converter for the computer this is running on.</summary>
    internal static IHeicConverter ForThisComputer() =>
        OperatingSystem.IsMacOS() ? new SipsConverter()
        : OperatingSystem.IsWindows() ? new WindowsCodecConverter()
        : new NoConverter();
}

/// <summary>
/// Linux, and anywhere else. There is no conversion here to reach for, and saying so is better than
/// pretending: the card names what the user can do on the phone instead.
/// </summary>
internal sealed class NoConverter : IHeicConverter
{
    public bool CanConvert => false;

    public byte[]? ToJpeg(byte[] heic) => null;
}

/// <summary>
/// macOS, through <c>sips</c> — which has shipped with every version of macOS this app supports and
/// needs nothing installed.
/// </summary>
internal sealed class SipsConverter : IHeicConverter
{
    public bool CanConvert => OperatingSystem.IsMacOS() && File.Exists("/usr/bin/sips");

    public byte[]? ToJpeg(byte[] heic) => ExternalConversion.Run(
        heic,
        ".heic",
        (input, output) => ("/usr/bin/sips", $"-s format jpeg \"{input}\" --out \"{output}\""));
}

/// <summary>
/// Windows, through the imaging component the operating system already uses to show the photograph
/// in File Explorer.
///
/// <para><b>Why PowerShell rather than a package.</b> The decoder is Windows' own, and reaching it
/// from .NET means either a Windows-only dependency in a cross-platform project or a native interop
/// layer — both of which this project's architecture rules would have to bend for. A short script
/// against WPF's imaging classes is the same decoder, costs nothing when it is not used, and fails
/// by returning nothing rather than by failing to load.</para>
///
/// <para>It works when the user has Microsoft's HEIF Image Extensions, which most Windows 11
/// installations do. When they do not, the run fails and the user gets the honest card.</para>
/// </summary>
internal sealed class WindowsCodecConverter : IHeicConverter
{
    public bool CanConvert => OperatingSystem.IsWindows();

    public byte[]? ToJpeg(byte[] heic) => ExternalConversion.Run(
        heic,
        ".heic",
        (input, output) =>
        {
            string script = string.Create(
                CultureInfo.InvariantCulture,
                $"Add-Type -AssemblyName PresentationCore; "
                + $"$s=[System.IO.File]::OpenRead('{input}'); "
                + $"$d=[System.Windows.Media.Imaging.BitmapDecoder]::Create($s,'None','OnLoad'); "
                + $"$e=New-Object System.Windows.Media.Imaging.JpegBitmapEncoder; "
                + $"$e.QualityLevel=95; "
                + $"$e.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($d.Frames[0])); "
                + $"$o=[System.IO.File]::Create('{output}'); $e.Save($o); $o.Close(); $s.Close()");

            return ("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script.Replace("\"", "\\\"", StringComparison.Ordinal)}\"");
        });
}

/// <summary>
/// The part both converters share: bytes to a temporary file, run something, read what came back,
/// and clean up whatever happens.
/// </summary>
internal static class ExternalConversion
{
    /// <summary>
    /// Longer than a photograph should ever take and short enough that a wedged process does not
    /// look like a frozen app. A conversion that has not finished in this is one the user is told
    /// about rather than one they wait for.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    internal static byte[]? Run(
        byte[] input, string inputExtension, Func<string, string, (string Exe, string Arguments)> command)
    {
        ArgumentNullException.ThrowIfNull(command);

        string folder = Path.Combine(Path.GetTempPath(), "trestleboard-convert-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            string inputPath = Path.Combine(folder, "photo" + inputExtension);
            string outputPath = Path.Combine(folder, "photo.jpg");
            File.WriteAllBytes(inputPath, input);

            (string exe, string arguments) = command(inputPath, outputPath);
            using var process = Process.Start(new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null || !process.WaitForExit(Patience))
            {
                return null;
            }

            return File.Exists(outputPath) ? File.ReadAllBytes(outputPath) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Every one of these means the same thing to the user — it could not be done — and the
            // card above says so. A conversion is a convenience; failing it must never be an error
            // dialog with a process name in it.
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A temporary folder that would not delete is the operating system's problem, not
                // the committee's.
            }
        }
    }
}
