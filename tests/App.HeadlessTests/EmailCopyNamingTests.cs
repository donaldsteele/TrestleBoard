using TrestleBoard.App;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M87: what the smaller copy is called.
///
/// <para>Both files sit in the same folder and will be looked at months later, so the name has to
/// say which is which — "September 2026.pdf" and "September 2026 (for email).pdf".</para>
/// </summary>
public sealed class EmailCopyNamingTests
{
    [Fact]
    public void TheSmallerCopyIsNamedForWhatItIs()
    {
        string full = Path.Combine("C:", "issues", "September 2026.pdf");

        Assert.Equal(
            Path.Combine("C:", "issues", "September 2026 (for email).pdf"),
            MainWindow.EmailCopyPathFor(full));
    }

    /// <summary>It stays beside the original, whatever folder that is.</summary>
    [Fact]
    public void ItIsWrittenBesideTheOriginal()
    {
        string full = Path.Combine("D:", "Lodge", "2026", "October 2026.pdf");

        Assert.Equal(
            Path.GetDirectoryName(full),
            Path.GetDirectoryName(MainWindow.EmailCopyPathFor(full)));
    }

    /// <summary>A name with dots in it keeps all of them, and only the extension is treated as one.</summary>
    [Fact]
    public void ANameWithDotsInItSurvives()
    {
        Assert.Equal(
            "Trestle board vol. 2 no. 9 (for email).pdf",
            Path.GetFileName(MainWindow.EmailCopyPathFor("Trestle board vol. 2 no. 9.pdf")));
    }
}
