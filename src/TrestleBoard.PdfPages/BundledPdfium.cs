using System;
using System.IO;

namespace TrestleBoard.PdfPages;

/// <summary>
/// The notice that has to travel with PDFium (PLAN.md §12 gate 22, §11 M74 (f)).
///
/// <para>PDFium is the one native library in TrestleBoard that is neither Microsoft's nor written
/// here. It arrives inside <c>Docnet.Core</c>, it is used only by
/// <see cref="PdfPageRasterizer"/> and only at import time, and it is BSD-3-Clause over eleven
/// bundled third-party notices — libpng, LibTIFF, agg23, FreeType, lcms, OpenJPEG, zlib,
/// libjpeg-turbo, the IJG notices and ICU among them. BSD-3-Clause and Apache-2.0 both require
/// the notice to accompany a binary distribution, so this is a condition of shipping the app.</para>
///
/// <para><b>Why the text is embedded rather than left where the package puts it.</b> Docnet does
/// ship a file called <c>LICENSE</c> in <c>runtimes/&lt;rid&gt;/native/</c>, and a self-contained
/// publish does copy it — into the publish root, as a bare file named <c>LICENSE</c>, beside the
/// executable, with nothing saying which package it belongs to and nothing stopping another
/// package's native <c>LICENSE</c> from overwriting it. A notice nobody can attribute and nobody is
/// ever shown does not discharge an attribution clause. Read out of the assembly and put under
/// Help → "Fonts and licences", beside the fonts' and the dictionary's, it does — the M14 and M68
/// precedent exactly. <c>assets-src/natives/natives.json</c> records its SHA-256 and its
/// provenance, and <c>PdfiumLicenceTests</c> fails if either drifts.</para>
/// </summary>
public static class BundledPdfium
{
    /// <summary>What the About-style surfaces call it.</summary>
    public const string Name = "PDFium";

    /// <summary>Resource name of the notice that ships inside the assembly.</summary>
    public const string LicenceResource = "TrestleBoard.PdfPages.Natives.LICENSE-pdfium.txt";

    /// <summary>Which package the library itself comes from, as the manifest records it.</summary>
    public const string ShippedBy = "Docnet.Core 2.6.0";

    /// <summary>PDFium's licence text, as shown by Help → "Fonts and licences".</summary>
    public static string ReadLicenceText()
    {
        using Stream stream = typeof(BundledPdfium).Assembly.GetManifestResourceStream(LicenceResource)
            ?? throw new InvalidOperationException(
                $"The PDFium licence ({LicenceResource}) is not in this build. TrestleBoard may not "
                + "be distributed without it — see assets-src/natives/natives.json.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
