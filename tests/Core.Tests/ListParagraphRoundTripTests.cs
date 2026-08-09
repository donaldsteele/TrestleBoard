using System;
using System.IO;
using System.Linq;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Samples;
using TrestleBoard.Core.Workflow;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// M61's format promises (PLAN.md §11 M61): the setting survives a round trip, carry-forward keeps
/// it, and — the one that protects every existing lodge — a newsletter written before M61 opens and
/// saves back byte-unchanged.
/// </summary>
public sealed class ListParagraphRoundTripTests
{
    /// <summary>
    /// The acceptance criterion in as many words: <i>a pre-M61 document opens and saves back
    /// byte-unchanged if untouched</i>.
    ///
    /// <para>It holds because <c>ListKind</c> is nullable and null is not written. A property with a
    /// non-null default would have added <c>"listKind": "none"</c> to every paragraph of every
    /// newsletter the committee has ever saved, the first time they opened one.</para>
    /// </summary>
    [Fact]
    public void ANewsletterWrittenBeforeThisMilestoneSavesBackByteUnchanged()
    {
        TboardPackage original = SampleIssue.CreatePackage();
        Assert.All(
            original.Document.Stories.SelectMany(s => s.Paragraphs),
            p => Assert.Null(p.ListKind));

        byte[] first = Write(original);
        byte[] second = Write(Read(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ThePropertyIsNotWrittenAtAllForOrdinaryWriting()
    {
        string json = System.Text.Encoding.UTF8.GetString(Write(SampleIssue.CreatePackage()));

        Assert.DoesNotContain("listKind", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AListSurvivesBeingSavedAndOpened()
    {
        TboardPackage package = SampleIssue.CreatePackage();
        package.Document.Stories[0].Paragraphs[0].ListKind = ListKinds.Bullet;

        TboardPackage reopened = Read(Write(package));

        Assert.Equal(ListKinds.Bullet, reopened.Document.Stories[0].Paragraphs[0].ListKind);
    }

    [Fact]
    public void CarryForwardKeepsNothingOfTheOldWritingIncludingItsLists()
    {
        TboardPackage package = SampleIssue.CreatePackage();
        package.Document.Stories[0].Paragraphs[0].ListKind = ListKinds.Number;

        TboardPackage next = CarryForward.NextIssue(package);

        // The prose is reset to a prompt, and a prompt is not a list.
        Assert.All(next.Document.Stories.SelectMany(s => s.Paragraphs), p => Assert.Null(p.ListKind));
    }

    [Fact]
    public void CopyingAParagraphCopiesWhetherItIsAList()
    {
        var paragraph = new StoryParagraph
        {
            ParagraphStyleRef = "body",
            ListKind = ListKinds.Number,
            Runs = [new StoryRun { Text = "A point." }],
        };

        Assert.Equal(ListKinds.Number, paragraph.Clone().ListKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ListKinds.Bullet)]
    [InlineData(ListKinds.Number)]
    public void TheTwoKindsAndNothingElseAreKnown(string? kind) => Assert.True(ListKinds.IsKnown(kind));

    [Fact]
    public void ANestedListIsNotAKindThisAppKnows() => Assert.False(ListKinds.IsKnown("bullet-level-2"));

    private static byte[] Write(TboardPackage package)
    {
        using var stream = new MemoryStream();
        TboardContainer.Save(package, stream);
        return stream.ToArray();
    }

    private static TboardPackage Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return TboardContainer.Load(stream);
    }
}
