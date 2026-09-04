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

            // M88.
            nameof(Member.MemberNumber), nameof(Member.BirthYear), nameof(Member.Degree),
            nameof(Member.MasonicTitle), nameof(Member.AddressLine1), nameof(Member.AddressLine2),
            nameof(Member.City), nameof(Member.State), nameof(Member.Zip),
            nameof(Member.AddressUndeliverable), nameof(Member.HomePhone),
            nameof(Member.MobilePhone), nameof(Member.WorkPhone), nameof(Member.SpouseName),
            nameof(Member.SpouseEmail), nameof(Member.SpousePhone),
        ];

        string[] stored = [.. typeof(Member).GetProperties()
            .Where(p => !Attribute.IsDefined(p, typeof(System.Text.Json.Serialization.JsonIgnoreAttribute)))
            .Select(p => p.Name)];

        Assert.Equal([.. stored.OrderBy(n => n, StringComparer.Ordinal)],
            [.. compared.OrderBy(n => n, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// A spouse is fields on her husband's card and never a person in the book (M88), so a mailing
    /// group counts one recipient here, not two.
    ///
    /// <para>The guarantee is structural — there is no <see cref="Member"/> row for her, so there is
    /// nothing for a group, a projection or M56's BCC line to find — and this test is the corollary
    /// written down. The failure it guards against is the one that would be noticed last: a wife
    /// quietly receiving the newsletter twice, or appearing in the lodge's birthday list.</para>
    /// </summary>
    [Fact]
    public void ASpouseIsNotAPersonInTheBook()
    {
        Member married = Someone() with
        {
            Groups = [MemberGroups.ByEmail],
            Email = "a.placeholder@example.invalid",
            SpouseName = "B. Fictitious",
            SpouseEmail = "b.fictitious@example.invalid",
            SpousePhone = "555-0904",
        };

        IReadOnlyList<Member> recipients = MemberGroups.Members([married], MemberGroups.ByEmail);

        Assert.Single(recipients);
        Assert.Equal("a.placeholder@example.invalid", recipients[0].Email);
    }

    /// <summary>
    /// The census above checks that every property is <em>named</em> in the list beside it. This
    /// checks that every property is actually <em>compared</em> — by changing one at a time and
    /// insisting the two members stop being equal (M88).
    ///
    /// <para>The two are different failures. A property can be added to the list and forgotten in
    /// <c>Member.Equals</c>, and the symptom is the quiet one M12 fears most: an import that reports
    /// "nothing changed" for a row that changed something, and drops the edit on the floor
    /// (<c>RosterMerge.Plan</c> writes the member only when <c>changed</c>). With thirty properties
    /// and a hand-written comparison, a name-only census is no longer enough.</para>
    ///
    /// <para><c>bool</c> and <c>int?</c> properties are set to a value the fixture does not have;
    /// strings to a distinctive one. Nothing here needs to be a plausible person — it needs to be
    /// different.</para>
    /// </summary>
    [Fact]
    public void ChangingAnyOneStoredPropertyMakesADifferentMember()
    {
        Member original = Someone();

        foreach (System.Reflection.PropertyInfo property in typeof(Member).GetProperties())
        {
            if (Attribute.IsDefined(property, typeof(System.Text.Json.Serialization.JsonIgnoreAttribute))
                || property.Name == nameof(Member.ExtraProperties)
                || property.Name == nameof(Member.Groups))
            {
                // ExtraProperties and Groups are compared by their own rules, each with a test of
                // its own above; what is being hunted here is a plain field left out of Equals.
                continue;
            }

            object? changed = property.PropertyType switch
            {
                Type t when t == typeof(string) => "different",
                Type t when t == typeof(bool) => !(bool)property.GetValue(original)!,
                Type t when t == typeof(int?) => Different((int?)property.GetValue(original)),
                _ => throw new Xunit.Sdk.XunitException(
                    $"{property.Name} is a {property.PropertyType.Name}, which this test does not "
                    + "know how to change. Teach it, rather than skipping the property."),
            };

            Member mutated = Rebuild(original, property.Name, changed);

            Assert.False(
                original.Equals(mutated),
                $"changing {property.Name} left two members comparing equal — it is missing from "
                + "Member.Equals, and an import will silently drop edits to it");
        }
    }

    /// <summary>A value the original does not have, for the nullable-int properties.</summary>
    private static int? Different(int? current) => current == 7 ? 8 : 7;

    /// <summary>
    /// A copy with one property changed. <c>with</c> needs the property name at compile time, which
    /// is the one thing a census like this does not have, so the copy is made property by property
    /// through reflection — <c>init</c> only binds the C# compiler's hands, not the runtime's.
    /// </summary>
    private static Member Rebuild(Member original, string property, object? value)
    {
        var mutated = new Member();
        foreach (System.Reflection.PropertyInfo p in typeof(Member).GetProperties())
        {
            if (!p.CanWrite)
            {
                continue;
            }

            p.SetValue(mutated, p.Name == property ? value : p.GetValue(original));
        }

        return mutated;
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
