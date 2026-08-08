using System;
using System.IO;

namespace TrestleBoard.App;

/// <summary>
/// The application's own licence, read out of the assembly (PLAN.md §11 M68).
///
/// <para>
/// The font licences ship as an embedded resource in <c>TrestleBoard.Layout</c> because the OFL
/// requires the text to accompany the faces (M14). The same reasoning applies here one level up:
/// PolyForm Noncommercial's <i>Notices</i> section requires that anyone who receives a copy of the
/// software also receives these terms, and a <c>LICENSE</c> file that exists only in the git
/// repository would never reach somebody who installed the app from a release. So the root
/// <c>LICENSE</c> is compiled into this assembly, and <c>LicenceTests</c> fails if the embedded copy
/// and the file on disk ever drift apart.
/// </para>
/// </summary>
internal static class AppLicence
{
    /// <summary>The logical name the csproj gives the embedded root <c>LICENSE</c>.</summary>
    internal const string Resource = "TrestleBoard.App.LICENSE";

    /// <summary>
    /// What the licence is called, in the one line the About window has room for. The owner chose
    /// it on 2026-08-08: free for non-profit use, a separate licence for profit-making use.
    /// </summary>
    internal const string Name = "PolyForm Noncommercial License 1.0.0";

    /// <summary>Where somebody who wants a commercial licence asks for one.</summary>
    internal const string CommercialContact =
        "https://github.com/donaldsteele/TrestleBoard/issues";

    /// <summary>The full licence text, as shown by Help → "Licence".</summary>
    internal static string ReadText()
    {
        using Stream stream = typeof(AppLicence).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Embedded licence resource missing: {Resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
