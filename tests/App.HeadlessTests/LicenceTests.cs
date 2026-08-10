using System;
using System.IO;
using System.Linq;
using TrestleBoard.Editing.Actions;
using TrestleBoard.PdfPages;
using TrestleBoard.Spelling;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M68's acceptance, machine-checked. §13 carried "the application has no licence file" from M15
/// until the owner decided on 2026-08-08; the risk now is not that the licence is missing but that
/// it quietly stops travelling with the software — the same failure the OFL gate found at M14,
/// where the fonts shipped and their licences did not.
///
/// <para>
/// These are plain unit tests rather than shell tests: none of them opens a window, so none needs
/// the headless session. What they assert is that the file on disk, the copy inside the assembly
/// and the two sentences the user reads all still say the same thing.
/// </para>
/// </summary>
public sealed class LicenceTests
{
    /// <summary>The heading PolyForm's own text carries. If this moves, we changed their words.</summary>
    private const string PolyFormHeading = "# PolyForm Noncommercial License 1.0.0";

    [Fact]
    public void TheLicenceShipsInsideTheAssemblyRatherThanBesideTheRepository()
    {
        string[] resources = typeof(MainWindow).Assembly.GetManifestResourceNames();

        Assert.Contains(AppLicence.Resource, resources);
    }

    [Fact]
    public void TheEmbeddedLicenceIsTheFileAtTheRootOfTheRepository()
    {
        string onDisk = File.ReadAllText(Path.Combine(RepositoryRoot(), "LICENSE"));

        Assert.Equal(Normalise(onDisk), Normalise(AppLicence.ReadText()));
    }

    [Fact]
    public void TheLicenceCarriesPolyFormsTextUnchanged()
    {
        string text = AppLicence.ReadText();

        Assert.Contains(PolyFormHeading, text, StringComparison.Ordinal);
        Assert.Contains("Any noncommercial purpose is a permitted purpose.", text, StringComparison.Ordinal);
        Assert.Contains(
            "Use by any charitable organization, educational institution,",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheLicenceSaysInPlainLanguageWhoMayUseItBeforeItSaysItInLegalLanguage()
    {
        string text = AppLicence.ReadText();
        int plain = text.IndexOf("TrestleBoard is free for non-profit use.", StringComparison.Ordinal);
        int legal = text.IndexOf(PolyFormHeading, StringComparison.Ordinal);

        Assert.True(plain >= 0, "The plain-language preamble is gone from LICENSE.");
        Assert.True(
            plain < legal,
            "The formal terms come first, so the reader this app is written for meets them cold.");
    }

    /// <summary>
    /// PolyForm §Notices: whoever receives the software must also receive any plain-text line
    /// beginning "Required Notice:". Ours has to be in the shipped text, not only in the repository.
    /// </summary>
    [Fact]
    public void TheRequiredNoticeTravelsWithTheSoftware()
    {
        string[] lines = AppLicence.ReadText().Split('\n').Select(l => l.Trim()).ToArray();

        Assert.Contains(lines, l => l.StartsWith("Required Notice: Copyright", StringComparison.Ordinal));
    }

    [Fact]
    public void SomebodyWhoWantsToPayForACommercialLicenceIsToldWhereToAsk()
    {
        Assert.Contains(AppLicence.CommercialContact, AppLicence.ReadText(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheAboutWindowNamesTheLicenceAndPointsAtTheWholeThing()
    {
        string about = MainWindow.AboutText();

        Assert.Contains(AppLicence.Name, about, StringComparison.Ordinal);
        Assert.Contains("Help, \"Licence\"", about, StringComparison.Ordinal);
    }

    /// <summary>
    /// The app's own terms and the fonts' terms are two different grants from two different people,
    /// and M68 kept them on two menu items so nobody reads one as covering the other.
    /// </summary>
    [Fact]
    public void TheAppsLicenceAndTheFontsLicencesAreSeparateCommands()
    {
        Assert.NotEqual(ActionId.Licence, ActionId.FontLicences);
        Assert.NotNull(ActionCatalog.Get(ActionId.Licence));
        Assert.NotNull(ActionCatalog.Get(ActionId.FontLicences));
    }

    [Fact]
    public void TheReadmeGrantsThePermissionTheLicenceGrants()
    {
        string readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));

        Assert.Contains("PolyForm Noncommercial License", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("no licence file yet", readme, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "no permission to\ncopy, modify or redistribute",
            Normalise(readme),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// M74 (f), gate 22. Help → "Fonts and licences" is the one place in the app where somebody can
    /// read what came with TrestleBoard and whose terms are somebody else's. It listed the fonts and
    /// the spelling dictionary; PDFium — a whole native library, named in gate 22 since gate 22 was
    /// written — was not there, and neither was any other notice for it. BSD-3-Clause §2 requires
    /// the notice to accompany the binary, and the binary goes out in every installer.
    ///
    /// <para>All three are asserted together on purpose: this is the list, and the way it fails is
    /// by something joining the installer and not joining the list.</para>
    /// </summary>
    [Fact]
    public void EverythingBundledWithSomebodyElsesTermsIsUnderFontsAndLicences()
    {
        string shown = MainWindow.BundledLicences();

        // The fonts (M14) — the SIL Open Font Licence, by its own heading.
        Assert.Contains("SIL OPEN FONT LICENSE", shown, StringComparison.OrdinalIgnoreCase);

        // The spelling dictionary (M52).
        Assert.Contains(BundledDictionary.Language, shown, StringComparison.Ordinal);

        // PDFium (M67, listed here from M74) — named, attributed to where it came from, and
        // carrying its own text rather than a reference to it.
        Assert.Contains(BundledPdfium.Name, shown, StringComparison.Ordinal);
        Assert.Contains(BundledPdfium.ShippedBy, shown, StringComparison.Ordinal);
        Assert.Contains("Copyright 2014 The PDFium Authors", shown, StringComparison.Ordinal);
        Assert.Contains("# BEGIN ICU (International Components for Unicode) license file", shown, StringComparison.Ordinal);
    }

    /// <summary>
    /// And it is said in words a committee member can follow, not just reproduced. PLAN.md §6: this
    /// window is read by somebody who wondered what all this is; the notice is the law, the sentence
    /// above it is the explanation.
    /// </summary>
    [Fact]
    public void ThePdfiumNoticeIsIntroducedInPlainLanguage()
    {
        string shown = MainWindow.BundledLicences();

        Assert.Contains(
            "turns one page of somebody else's PDF into a picture",
            shown,
            StringComparison.Ordinal);
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TrestleBoard.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
