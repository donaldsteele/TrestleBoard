using System;
using System.IO;
using System.Linq;
using TrestleBoard.Editing.Actions;
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
