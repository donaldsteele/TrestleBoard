using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TrestleBoard.Roster.Tests;

/// <summary>
/// M55's status and groups (PLAN.md §11 M55).
///
/// <para><b>§0 rule 7.</b> Every person here is fictional, as everywhere in this project. The real
/// version of this data is the most sensitive the app holds: whether a named man is alive.</para>
/// </summary>
public sealed class MemberStatusAndGroupsTests
{
    private static Member Someone(string id = "person-1") => new()
    {
        Id = id,
        DisplayName = "A. Placeholder",
        BirthMonth = 9,
        BirthDay = 14,
    };

    // ---- status ---------------------------------------------------------------------------------

    [Fact]
    public void SomebodyWithNoDateIsAlive()
    {
        Member member = Someone();

        Assert.False(member.HasPassed);
        Assert.True(member.IsInTheNewsletter);
    }

    [Fact]
    public void RecordingTheDateIsRecordingTheStatus()
    {
        Member member = Someone() with { PassedOn = "2026-09-14" };

        Assert.True(member.HasPassed);
        Assert.False(member.IsInTheNewsletter);
    }

    /// <summary>
    /// The two fields must never be able to disagree: the first disagreement puts a deceased
    /// brother back in the birthday list.
    /// </summary>
    [Fact]
    public void AHandEditedFileCannotLeaveHimBothPassedAndOnTheRolls()
    {
        Member member = (Someone() with { PassedOn = "2026-09-14", IsActive = true }).Normalised();

        Assert.False(member.IsActive);
        Assert.False(member.IsInTheNewsletter);
    }

    [Fact]
    public void SomebodyOffTheRollsIsNotTheSameAsSomebodyWhoHasDied()
    {
        Member member = (Someone() with { IsActive = false }).Normalised();

        Assert.False(member.IsInTheNewsletter);
        Assert.False(member.HasPassed);
        Assert.Null(member.PassedOn);
    }

    [Fact]
    public void AWhitespaceDateIsNoDate()
    {
        Member member = (Someone() with { PassedOn = "   " }).Normalised();

        Assert.False(member.HasPassed);
        Assert.True(member.IsInTheNewsletter);
    }

    // ---- groups ---------------------------------------------------------------------------------

    [Fact]
    public void GroupsAreTidiedWithoutBeingReordered()
    {
        Member member = (Someone() with
        {
            Groups = ["  Officers ", "", "Gets a printed copy", "officers"],
        }).Normalised();

        Assert.Equal(["Officers", "Gets a printed copy"], member.Groups);
    }

    [Fact]
    public void BeingInAGroupIgnoresCaseAndSpace()
    {
        Member member = Someone() with { Groups = ["Gets a printed copy"] };

        Assert.True(member.IsIn("  gets a PRINTED copy "));
        Assert.False(member.IsIn(MemberGroups.ByEmail));
    }

    /// <summary>
    /// A mailing group is for people who are still going to receive something. A deceased brother
    /// left in the printed-copy group would otherwise be posted a newsletter.
    /// </summary>
    [Fact]
    public void AGroupNeverHandsBackSomebodyWhoHasPassed()
    {
        Member[] members =
        [
            Someone("person-1") with { Groups = [MemberGroups.Printed] },
            (Someone("person-2") with
            {
                DisplayName = "B. Sample",
                Groups = [MemberGroups.Printed],
                PassedOn = "2026-09-14",
            }).Normalised(),
        ];

        IReadOnlyList<Member> printed = MemberGroups.Members(members, MemberGroups.Printed);

        Assert.Single(printed);
        Assert.Equal("person-1", printed[0].Id);
    }

    [Fact]
    public void TheGroupsOfferedAreTheKnownOnesThenTheLodgesOwn()
    {
        Member[] members = [Someone() with { Groups = ["Widows", MemberGroups.Printed] }];

        IReadOnlyList<string> offered = MemberGroups.InUse(members);

        Assert.Equal(MemberGroups.Suggested, offered.Take(MemberGroups.Suggested.Count));
        Assert.Contains("Widows", offered);
        Assert.Single(offered, g => string.Equals(g, MemberGroups.Printed, StringComparison.Ordinal));
    }

    // ---- equality, which the merge depends on ---------------------------------------------------

    /// <summary>
    /// The defect gate 9 caught within a minute of <c>Groups</c> being added: a record compares an
    /// <c>IReadOnlyList</c> by reference, so two members with identical groups in different arrays
    /// compared as different — and the import merge reported every person as edited on a re-import.
    /// </summary>
    [Fact]
    public void TwoMembersWithTheSameGroupsInDifferentArraysAreTheSameMember()
    {
        Member a = Someone() with { Groups = ["Officers", MemberGroups.Printed] };
        Member b = Someone() with { Groups = ["Officers", MemberGroups.Printed] };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentGroupsMakeDifferentMembers() =>
        Assert.NotEqual(
            Someone() with { Groups = ["Officers"] },
            Someone() with { Groups = [MemberGroups.Printed] });

    [Fact]
    public void ADifferentDateOfPassingMakesADifferentMember() =>
        Assert.NotEqual(
            Someone() with { PassedOn = "2026-09-14" },
            Someone() with { PassedOn = "2026-09-15" });

    /// <summary>
    /// <c>Member.Equals</c> is written out by hand, so a property added later could silently be
    /// left out of it — and the symptom would be an import that reports nothing changed when
    /// something did. This fails the moment somebody adds a property without comparing it.
    /// </summary>
    [Fact]
    public void EveryStoredPropertyIsPartOfBeingTheSameMember()
    {
        string[] compared =
        [
            nameof(Member.Id), nameof(Member.DisplayName), nameof(Member.SortName),
            nameof(Member.BirthMonth), nameof(Member.BirthDay), nameof(Member.Phone),
            nameof(Member.Email), nameof(Member.Office), nameof(Member.DegreeDate),
            nameof(Member.DegreeKind), nameof(Member.IsActive), nameof(Member.PassedOn),
            nameof(Member.Notes), nameof(Member.Groups), nameof(Member.ExtraProperties),
        ];

        string[] stored = [.. typeof(Member).GetProperties()
            .Where(p => !Attribute.IsDefined(p, typeof(System.Text.Json.Serialization.JsonIgnoreAttribute)))
            .Select(p => p.Name)];

        Assert.Equal([.. stored.OrderBy(n => n, StringComparer.Ordinal)],
            [.. compared.OrderBy(n => n, StringComparer.Ordinal)]);
    }

    /// <summary>A newer TrestleBoard's unknown fields count towards sameness, or a plain round trip
    /// through this version would look like an edit to a file it did not change.</summary>
    [Fact]
    public void UnknownPropertiesFromANewerVersionCountTowardsSameness()
    {
        Member withExtra = Someone();
        withExtra.ExtraProperties = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
        {
            ["lodgeNumber"] = System.Text.Json.JsonDocument.Parse("414").RootElement,
        };

        Assert.NotEqual(Someone(), withExtra);
    }
}
