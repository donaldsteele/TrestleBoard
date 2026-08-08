using System;
using System.Collections.Generic;
using System.Linq;
using TrestleBoard.App.Integration;
using TrestleBoard.Roster;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M56's mail hand-off (PLAN.md §11 M56).
///
/// <para>The test that matters most here is the one about BCC. Sixty brothers' addresses in a To:
/// line is a disclosure to sixty people who never agreed to it, and it cannot be taken back — §0
/// rule 7 names it. Everything else in this file is about degrading honestly.</para>
///
/// <para>§0 rule 2: every address below is at <c>example.invalid</c>, a domain reserved by the
/// IETF precisely so that it can never belong to anybody.</para>
/// </summary>
public sealed class SendItTests
{
    private static Member Brother(int n, bool byEmail = true, bool passed = false) => (new Member
    {
        Id = $"person-{n}",
        DisplayName = $"Placeholder {n}",
        Email = $"placeholder{n}@example.invalid",
        Groups = byEmail ? [MemberGroups.ByEmail] : [MemberGroups.Printed],
        PassedOn = passed ? "2026-09-14" : null,
    }).Normalised();

    private static List<Member> Lodge(int howMany) =>
        [.. Enumerable.Range(1, howMany).Select(n => Brother(n))];

    // ---- the rule that cannot bend ---------------------------------------------------------------

    /// <summary>
    /// Every address goes in BCC. Not "by default" — there is no path in <see cref="MailHandoff"/>
    /// that writes one anywhere else, and this is the test that says so.
    /// </summary>
    [Fact]
    public void EveryAddressGoesInTheBlindCopyLineAndNowhereElse()
    {
        IReadOnlyList<string> addresses = MailHandoff.AddressesIn(Lodge(3), MemberGroups.ByEmail);

        string uri = MailHandoff.BuildUri(addresses, "Subject", "Body")!;

        Assert.StartsWith("mailto:?", uri, StringComparison.Ordinal);
        Assert.Contains("&bcc=", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("&to=", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("&cc=", uri, StringComparison.Ordinal);

        // And no address appears before the bcc parameter begins.
        int bcc = uri.IndexOf("&bcc=", StringComparison.Ordinal);
        Assert.All(addresses, a => Assert.True(
            uri.IndexOf(Uri.EscapeDataString(a), StringComparison.Ordinal) > bcc,
            $"{a} appears outside the blind copy line."));
    }

    [Fact]
    public void ABrotherWhoHasPassedIsNeverEmailed()
    {
        List<Member> lodge = [Brother(1), Brother(2, passed: true)];

        IReadOnlyList<string> addresses = MailHandoff.AddressesIn(lodge, MemberGroups.ByEmail);

        Assert.Equal(["placeholder1@example.invalid"], addresses);
    }

    [Fact]
    public void SomebodyInTheGroupWithNoAddressIsSkippedRatherThanCounted()
    {
        List<Member> lodge = [Brother(1), Brother(2) with { Email = null }];

        Assert.Single(MailHandoff.AddressesIn(lodge, MemberGroups.ByEmail));
    }

    [Fact]
    public void TheSameAddressTwiceIsOneAddress()
    {
        List<Member> lodge = [Brother(1), Brother(2) with { Email = "placeholder1@example.invalid" }];

        Assert.Single(MailHandoff.AddressesIn(lodge, MemberGroups.ByEmail));
    }

    [Fact]
    public void OnlyTheEmailGroupIsEmailed()
    {
        List<Member> lodge = [Brother(1), Brother(2, byEmail: false)];

        Assert.Equal(["placeholder1@example.invalid"],
            MailHandoff.AddressesIn(lodge, MemberGroups.ByEmail));
    }

    // ---- degrading honestly ----------------------------------------------------------------------

    /// <summary>
    /// A truncated BCC list is the failure that matters: it sends the newsletter to some of the
    /// lodge and tells nobody. Better to refuse the link and paste instead.
    /// </summary>
    [Fact]
    public void TooManyAddressesForOneLinkRefusesTheLinkRatherThanRiskingATruncatedOne()
    {
        IReadOnlyList<string> plenty = MailHandoff.AddressesIn(Lodge(80), MemberGroups.ByEmail);

        Assert.Null(MailHandoff.BuildUri(plenty, "Subject", "Body"));
    }

    [Fact]
    public void ALodgeSizedListStillFitsInOneLink()
    {
        IReadOnlyList<string> sixty = MailHandoff.AddressesIn(Lodge(30), MemberGroups.ByEmail);

        string? uri = MailHandoff.BuildUri(sixty, "Subject", "Body");

        Assert.NotNull(uri);
        Assert.True(uri.Length <= MailHandoff.LongestUri);
    }

    [Fact]
    public void TheClipboardFallbackComesInBatchesAPersonCanCheck()
    {
        IReadOnlyList<string> plenty = MailHandoff.AddressesIn(Lodge(60), MemberGroups.ByEmail);

        string text = MailHandoff.ClipboardBatches(plenty);

        Assert.Contains("Batch 1 of 3:", text, StringComparison.Ordinal);
        Assert.Contains("Batch 3 of 3:", text, StringComparison.Ordinal);
        Assert.All(plenty, a => Assert.Contains(a, text, StringComparison.Ordinal));
    }

    [Fact]
    public void AShortListIsNotChoppedIntoBatchesForNoReason()
    {
        IReadOnlyList<string> few = MailHandoff.AddressesIn(Lodge(3), MemberGroups.ByEmail);

        Assert.DoesNotContain("Batch", MailHandoff.ClipboardBatches(few), StringComparison.Ordinal);
    }

    // ---- what the message says --------------------------------------------------------------------

    [Fact]
    public void TheSubjectNamesTheLodgeAndTheMonth()
    {
        string subject = MailHandoff.Subject("Indian Land Lodge 414", "Trestle Board", 2026, 9);

        Assert.Equal("Indian Land Lodge 414 Trestle Board — September 2026", subject);
    }

    [Fact]
    public void TheSubjectStillReadsWhenTheLodgeNameIsBlank() =>
        Assert.Equal("Trestle Board — September 2026", MailHandoff.Subject("", "", 2026, 9));

    /// <summary>
    /// mailto: cannot attach a file — no mail program accepts one, and rightly so. The message must
    /// therefore say which file to attach, or sixty people get an email about a newsletter that is
    /// not there.
    /// </summary>
    [Fact]
    public void TheMessageSaysWhichFileToAttachBecauseItCannotAttachIt()
    {
        string body = MailHandoff.Body("September 2026.pdf");

        Assert.Contains("attach the file named September 2026.pdf", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAddressesAreEscapedSoAnOddOneCannotBreakTheLink()
    {
        string uri = MailHandoff.BuildUri(["a b@example.invalid"], "Sub ject", "Bo dy")!;

        Assert.DoesNotContain(" ", uri, StringComparison.Ordinal);
    }
}
