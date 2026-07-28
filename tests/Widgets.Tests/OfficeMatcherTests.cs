using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Roster;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// The matcher M13's deferral was written about (PLAN.md §11 M19). It is tested in BOTH directions:
/// every string it claims to recognise resolves to one of the twelve offices, and every string a
/// lodge might plausibly write that is NOT one of the twelve comes back as "we don't know".
///
/// The second half is the important one. A matcher that is generous is a matcher that prints the
/// wrong man as Worshipful Master on page 2.
/// </summary>
public sealed class OfficeMatcherTests
{
    [Theory]
    [InlineData("Worshipful Master", "Worshipful Master")]
    [InlineData("worshipful master", "Worshipful Master")]
    [InlineData("WM", "Worshipful Master")]
    [InlineData("W.M.", "Worshipful Master")]
    [InlineData("Master", "Worshipful Master")]
    [InlineData("SW", "Senior Warden")]
    [InlineData("S.W.", "Senior Warden")]
    [InlineData("Sr. Warden", "Senior Warden")]
    [InlineData("  senior   warden  ", "Senior Warden")]
    [InlineData("JW", "Junior Warden")]
    [InlineData("Jr. Warden", "Junior Warden")]
    [InlineData("SD", "Senior Deacon")]
    [InlineData("Sr Deacon", "Senior Deacon")]
    [InlineData("JD", "Junior Deacon")]
    [InlineData("Jr Deacon", "Junior Deacon")]
    [InlineData("SS", "Senior Steward")]
    [InlineData("JS", "Junior Steward")]
    [InlineData("Treas.", "Treasurer")]
    [InlineData("Sec'y", "Secretary")]
    [InlineData("Chap.", "Chaplain")]
    [InlineData("Tyler", "Tiler")]
    [InlineData("Marshal", "Marshall")]
    public void TheOfficesALodgeActuallyWritesAreRecognised(string written, string expected) =>
        Assert.Equal(expected, OfficeMatcher.Match(written));

    [Theory]
    [InlineData("Past Master")]
    [InlineData("PM")]
    [InlineData("Historian")]
    [InlineData("Organist")]
    [InlineData("Lodge Education Officer")]
    [InlineData("Senior")]
    [InlineData("Warden")]
    [InlineData("Deacon")]
    [InlineData("Steward")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnythingElseIsAnHonestDontKnow(string? written) =>
        Assert.Null(OfficeMatcher.Match(written));

    /// <summary>
    /// "Past Master" is the one that would hurt most, so it gets its own test with the reason in
    /// the name: a Past Master is not the Master, and no amount of substring cleverness may make
    /// him one.
    /// </summary>
    [Fact]
    public void APastMasterIsNeverPrintedAsTheMaster()
    {
        Assert.Null(OfficeMatcher.Match("Past Master"));
        Assert.Null(OfficeMatcher.Match("P.M."));
        Assert.Equal("Worshipful Master", OfficeMatcher.Match("Master"));
    }

    [Fact]
    public void EveryAbbreviationInTheTableNamesOneOfTheTwelve()
    {
        Assert.All(
            OfficeMatcher.Abbreviations,
            pair => Assert.Contains(pair.Value, OfficersTableData.StandardPositions));
    }

    [Fact]
    public void EveryAbbreviationKeyIsAlreadyNormalisedSoTheTableCanBeReadByEye()
    {
        Assert.All(
            OfficeMatcher.Abbreviations.Keys,
            key => Assert.Equal(key, OfficeMatcher.Normalise(key)));
    }

    [Fact]
    public void EveryOneOfTheTwelveMatchesItsOwnName()
    {
        Assert.All(
            OfficersTableData.StandardPositions,
            position => Assert.Equal(position, OfficeMatcher.Match(position)));
    }

    [Fact]
    public void NoAbbreviationRepeatsAPositionNameSoOneSpellingLivesInOnePlace()
    {
        foreach (string position in OfficersTableData.StandardPositions)
        {
            Assert.DoesNotContain(OfficeMatcher.Normalise(position), OfficeMatcher.Abbreviations.Keys);
        }
    }

    [Fact]
    public void AnUnrecognisedOfficeIsDescribedInTheUsersOwnWords()
    {
        Assert.Equal("Historian", OfficeMatcher.Describe(" Historian "));
        Assert.Equal("Senior Warden", OfficeMatcher.Describe("SW"));
        Assert.Equal("no office", OfficeMatcher.Describe("   "));
    }
}
