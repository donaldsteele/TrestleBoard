using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace TrestleBoard.App.Integration;

/// <summary>
/// Something that can say a sentence out loud (PLAN.md §11 M58).
///
/// <para>An interface so that the whole read-aloud feature can be driven, and tested, without a
/// voice: <c>Available</c> false is a first-class state, not a failure. PLAN.md's acceptance is that
/// all voice code stays in App behind an interface, Core stays BCL-only, and no test depends on
/// audio.</para>
/// </summary>
internal interface ISpeaker
{
    /// <summary>True when this machine has something that can talk.</summary>
    bool Available { get; }

    /// <summary>Says it. Returns false when nothing came of it.</summary>
    bool Say(string text);

    /// <summary>Stops whatever is being said, if anything.</summary>
    void Hush();
}

/// <summary>
/// The operating system's own voice — <c>say</c> on macOS, <c>spd-say</c> on Linux, PowerShell's
/// speech synthesiser on Windows (M58).
///
/// <para><b>Hand-off, not a speech engine of our own</b>, for the same reason M53 does not build a
/// print subsystem and M56 does not send mail: the machine already has the voices the user has
/// chosen, at the speed they have set, and reimplementing that would be three new ways to be worse
/// than what is already there.</para>
///
/// <para>Linux is best-effort by design, matching §6's existing AT-SPI stance. A machine with no
/// <c>speech-dispatcher</c> simply reports <see cref="Available"/> false, and the walk-through runs
/// silently — which is a real feature rather than a degraded one.</para>
/// </summary>
internal sealed class SystemSpeaker : ISpeaker
{
    private Process? _speaking;

    internal SystemSpeaker() => Available = Detect();

    public bool Available { get; }

    public bool Say(string text)
    {
        if (!Available || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Hush();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Start("say", [text]);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Start("spd-say", ["--wait", text]);
        }

        // Windows has no console speech command, so the synthesiser is reached through PowerShell.
        // The text goes in as a single-quoted literal with its own quotes doubled, so a sentence
        // containing an apostrophe — "the Master's message" — cannot end the string early.
        string script =
            "Add-Type -AssemblyName System.Speech; "
            + "(New-Object System.Speech.Synthesis.SpeechSynthesizer).Speak('"
            + text.Replace("'", "''", StringComparison.Ordinal)
            + "')";
        return Start("powershell", ["-NoProfile", "-NonInteractive", "-Command", script]);
    }

    public void Hush()
    {
        try
        {
            if (_speaking is { HasExited: false })
            {
                _speaking.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
        }
        finally
        {
            _speaking?.Dispose();
            _speaking = null;
        }
    }

    /// <summary>
    /// Whether this machine can talk at all. Asked once, at construction: a user who has no voices
    /// should be told so on the first screen rather than after pressing Play and hearing nothing.
    /// </summary>
    private static bool Detect()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Every supported Windows carries System.Speech; whether a voice is installed is
            // another matter, and one only speaking will reveal.
            return true;
        }

        string command = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "say" : "spd-say";
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(directory.Trim(), command)))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is not worth a word to anybody.
            }
        }

        return false;
    }

    private bool Start(string fileName, string[] arguments)
    {
        var info = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            _speaking = Process.Start(info);
            return _speaking is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or PlatformNotSupportedException
            or IOException)
        {
            _speaking = null;
            return false;
        }
    }
}

/// <summary>A machine with no voice. What the tests use, and what a bare Linux box gets.</summary>
internal sealed class SilentSpeaker : ISpeaker
{
    public bool Available => false;

    public bool Say(string text) => false;

    public void Hush()
    {
    }
}
