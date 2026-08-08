using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace TrestleBoard.App.Integration;

/// <summary>What happened when the finished PDF was handed to the operating system (M53).</summary>
internal enum PrintOutcome
{
    /// <summary>The OS took it. Whatever happens next is between the user and their printer.</summary>
    HandedOver,

    /// <summary>The PDF was opened instead, so the user can print it from there.</summary>
    OpenedInstead,

    /// <summary>Nothing answered. The card says where the file is and what to do.</summary>
    NothingAnswered,
}

/// <summary>
/// "Print it" (PLAN.md §11 M53): the finished PDF handed to whatever the operating system already
/// uses to print PDFs.
///
/// <para><b>No print subsystem is built, and that is the design rather than a shortcut.</b> The
/// machine this runs on already has a program that prints PDFs, already knows which printer is the
/// default, and already has the dialog the user has seen a hundred times. Reimplementing any of
/// that would mean a page-setup dialog, a printer list and a driver conversation — three new places
/// to get printing subtly wrong for an audience who would have no way to tell what had gone
/// wrong.</para>
///
/// <para>Every path degrades to something honest. If the print verb does not answer, the PDF is
/// opened, and the card says <i>press Ctrl+P</i>. If nothing opens it either, the card says where
/// the file is. It never claims to have printed anything.</para>
/// </summary>
internal static class PrintService
{
    /// <summary>
    /// Hands the file over. Never throws: this is the last step of an evening's work and the file
    /// is safely on disk either way, so the worst outcome allowed here is a sentence.
    /// </summary>
    internal static PrintOutcome Print(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            return PrintOutcome.NothingAnswered;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // The shell's own "print" verb, which is what right-clicking the file in Explorer does.
            // UseShellExecute is required for a verb and is the whole point here.
            if (Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true, Verb = "print" }))
            {
                return PrintOutcome.HandedOver;
            }
        }
        else
        {
            // CUPS, which macOS and every desktop Linux has. lp first, lpr as the older spelling.
            if (Start(new ProcessStartInfo("lp", Quote(pdfPath)) { UseShellExecute = false })
                || Start(new ProcessStartInfo("lpr", Quote(pdfPath)) { UseShellExecute = false }))
            {
                return PrintOutcome.HandedOver;
            }
        }

        return Open(pdfPath) ? PrintOutcome.OpenedInstead : PrintOutcome.NothingAnswered;
    }

    /// <summary>Opens the PDF in whatever the machine uses to read them.</summary>
    internal static bool Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        string opener = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
        return Start(new ProcessStartInfo(opener, Quote(path)) { UseShellExecute = false });
    }

    /// <summary>
    /// What to say when the hand-off did not work. Plain, and it names the file rather than
    /// describing it — the user is about to go looking for it in a folder.
    /// </summary>
    internal static string FallbackMessage(string pdfPath, PrintOutcome outcome) => outcome switch
    {
        PrintOutcome.OpenedInstead =>
            "TrestleBoard could not send the newsletter to a printer by itself, so it has opened "
            + "the PDF instead. Press Ctrl+P in that window to print it.",
        _ =>
            "TrestleBoard could not find anything on this computer to print the PDF with. The file "
            + $"is saved as {Path.GetFileName(pdfPath)}, in {Path.GetDirectoryName(pdfPath)}. Open "
            + "it the way you normally open a PDF and print it from there.",
    };

    private static string Quote(string path) => $"\"{path}\"";

    /// <summary>
    /// True when something started. A missing program, a file type nobody has claimed and a
    /// refused verb all arrive here as exceptions, and all three mean the same thing to the caller.
    /// </summary>
    private static bool Start(ProcessStartInfo info)
    {
        try
        {
            using Process? process = Process.Start(info);
            return process is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or PlatformNotSupportedException
            or IOException)
        {
            return false;
        }
    }
}
