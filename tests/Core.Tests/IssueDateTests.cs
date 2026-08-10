using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Templates;
using TrestleBoard.Core.Workflow;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// M75: the newsletter knows which issue it is — the Core half.
///
/// <para>The defect this milestone exists for was invisible from here: <c>DocumentMetadata</c> has
/// always had an <c>IssueMonth</c>, and it was always readable, and nothing in the app ever wrote
/// one. These tests pin down the three-state flag that finally lets "nobody has said" be a state
/// rather than an inference from two magic numbers, and the two workflow paths that must leave it
/// unanswered rather than guessing.</para>
///
/// <para>All data fictional (PLAN.md §0).</para>
/// </summary>
public sealed class IssueDateTests
{
    /// <summary><b>Guard.</b> <c>HasIssueDate</c> is new API, so this could not have failed before
    /// M75 — there was nothing to ask. It pins the reading down against a later change.</summary>
    [Fact]
    public void ABrandNewDocumentHasNotBeenToldWhichIssueItIs() =>
        Assert.False(new DocumentMetadata().HasIssueDate);

    /// <summary>
    /// <b>Guard</b>, on new API. The compatibility rule, and the whole reason the flag is a <c>bool?</c> rather than a
    /// <c>bool</c>: a file written before M75 carries no flag at all, and interrogating a perfectly
    /// good July 2026 issue about its date on open would be a worse bug than the one being fixed.
    /// </summary>
    [Fact]
    public void AFileWrittenBeforeThisFixIsJudgedByItsDateAlone()
    {
        Assert.True(new DocumentMetadata { IssueMonth = 7, IssueYear = 2026 }.HasIssueDate);
        Assert.False(new DocumentMetadata { IssueMonth = 1, IssueYear = 2000 }.HasIssueDate);

        // January of a real year is a real issue; only the model's own default pair is the sentinel.
        Assert.True(new DocumentMetadata { IssueMonth = 1, IssueYear = 2027 }.HasIssueDate);
    }

    /// <summary><b>Guard</b>, on new API.</summary>
    [Fact]
    public void AnAnswerWrittenDownBeatsTheFallbackBothWays()
    {
        Assert.True(new DocumentMetadata { IssueMonth = 1, IssueYear = 2000, IssueDateChosen = true }.HasIssueDate);
        Assert.False(new DocumentMetadata { IssueMonth = 7, IssueYear = 2026, IssueDateChosen = false }.HasIssueDate);
    }

    /// <summary><b>Guard</b>, on new API. The flag is additive and JSON-optional: it survives a
    /// save/load round trip, and a container written without it reads back as the fallback rather
    /// than as false.</summary>
    [Fact]
    public void TheFlagSurvivesTheContainerRoundTrip()
    {
        TboardPackage package = Fixtures.BuildPackage();
        package.Document.Metadata.IssueDateChosen = false;

        using var stream = new MemoryStream();
        TboardContainer.Save(package, stream);
        stream.Position = 0;
        TboardPackage reloaded = TboardContainer.Load(stream);

        Assert.False(reloaded.Document.Metadata.IssueDateChosen);
        Assert.False(reloaded.Document.Metadata.HasIssueDate);
    }

    // ---- the two workflow paths --------------------------------------------------------------

    /// <summary>
    /// M75 (b). Carry-forward still bumps — the numbers are the pre-fill the wizard shows — but the
    /// bump no longer counts as the user having said so. "The month after the last one" overruled a
    /// committee that skips August or builds December early, without ever asking them.
    /// </summary>
    [Fact]
    public void CarryForwardPreFillsNextMonthAndStillCountsAsUnanswered()
    {
        TboardPackage previous = Fixtures.BuildPackage();
        previous.Document.Metadata.IssueMonth = 7;
        previous.Document.Metadata.IssueYear = 2026;
        previous.Document.Metadata.IssueDateChosen = true;

        TboardPackage next = CarryForward.NextIssue(previous);

        Assert.Equal(8, next.Document.Metadata.IssueMonth);
        Assert.Equal(2026, next.Document.Metadata.IssueYear);
        Assert.False(next.Document.Metadata.HasIssueDate);

        // And the source is untouched, which has been carry-forward's promise since M9.
        Assert.True(previous.Document.Metadata.HasIssueDate);
    }

    [Fact]
    public void ATemplateSaysOutLoudThatItHasNoIssueDate()
    {
        TboardPackage issue = Fixtures.BuildPackage();
        issue.Document.Metadata.IssueMonth = 7;
        issue.Document.Metadata.IssueYear = 2026;
        issue.Document.Metadata.IssueDateChosen = true;

        TboardPackage template = NewsletterTemplate.From(issue);

        Assert.False(template.Document.Metadata.IssueDateChosen);
        Assert.False(template.Document.Metadata.HasIssueDate);
    }

    /// <summary><b>Guard</b>: it passes against the pre-M75 tree too, because the fallback rule is
    /// what carries it there. Every shipped template starts life not knowing, which is what makes
    /// the ask fire on the one path a real user takes.</summary>
    [Theory]
    [InlineData("classic-414")]
    [InlineData("simple-4-page")]
    [InlineData("six-page-photos")]
    public void NoShippedTemplateClaimsToKnowWhichIssueItIs(string templateId) =>
        Assert.False(TemplateLibrary.Create(templateId).Document.Metadata.HasIssueDate);

    // ---- the meeting date the wizard can now compute (M75 (d)) --------------------------------

    /// <summary><b>Guard</b>: <c>MeetingDateTextFor</c> is new API. The computation it exposes is
    /// the one <c>RecomputeMeetingDates</c> has performed since M9 — this pins the two together so
    /// the wizard's date and next month's carry-forward cannot drift.</summary>
    [Fact]
    public void TheMeetingDateIsWorkedOutFromTheRuleAndTheIssueMonth()
    {
        Assert.Equal("July 7th", CarryForward.MeetingDateTextFor("1st Tuesday", 2026, 7));
        Assert.Equal("August 4th", CarryForward.MeetingDateTextFor("1st Tuesday", 2026, 8));
        Assert.Equal("July 3rd", CarryForward.MeetingDateTextFor("1st Friday", 2026, 7));
    }

    /// <summary><b>Guard</b>, on new API.</summary>
    [Fact]
    public void AnUnreadableRuleWorksNoDateOutAtAll()
    {
        Assert.Null(CarryForward.MeetingDateTextFor("", 2026, 7));
        Assert.Null(CarryForward.MeetingDateTextFor("whenever we feel like it", 2026, 7));
        Assert.Null(CarryForward.MeetingDateTextFor("1st Tuesday", 2026, 0));
    }

    // ---- SetMetadataCommand, through a live session (M75 (a)) ----------------------------------

    /// <summary>
    /// <c>SetMetadataCommand</c> has been in Core since M2 with <b>zero production callers</b>. M75
    /// is its first, so this is the first time anything has driven it through a real
    /// <see cref="DocumentSession"/> — the undo stack, the description the Edit menu prints, and the
    /// round trip back to the metadata the document started with.
    ///
    /// <para><b>Guard.</b> <c>SetMetadataCommand</c> and <c>DocumentSession</c> both predate M75, so
    /// this would have passed against the old tree as well — nothing was broken about the command,
    /// which is exactly the point: it worked all along and nothing called it.</para>
    /// </summary>
    [Fact]
    public void SayingWhichIssueItIsIsOneUndoStepAndCtrlZPutsItBack()
    {
        Document document = Fixtures.BuildDocument();
        document.Metadata.IssueMonth = 1;
        document.Metadata.IssueYear = 2000;
        document.Metadata.IssueDateChosen = false;
        var session = new DocumentSession(document);

        DocumentMetadata answered = document.Metadata.Clone();
        answered.IssueMonth = 7;
        answered.IssueYear = 2026;
        answered.IssueDateChosen = true;
        session.Execute(new SetMetadataCommand(answered));

        Assert.True(document.Metadata.HasIssueDate);
        Assert.Equal(7, document.Metadata.IssueMonth);
        Assert.Equal(2026, document.Metadata.IssueYear);
        Assert.True(session.CanUndo);
        Assert.Equal("Edit newsletter details", session.UndoDescription);

        session.Undo();

        Assert.False(document.Metadata.HasIssueDate);
        Assert.Equal(1, document.Metadata.IssueMonth);
        Assert.Equal(2000, document.Metadata.IssueYear);

        session.Redo();
        Assert.Equal(7, document.Metadata.IssueMonth);
    }

    /// <summary>
    /// <c>Clone</c> exists because <c>SetMetadataCommand</c> replaces the whole object, so anything
    /// the caller does not carry across is silently lost — the lodge's name off the cover, the
    /// meeting rule next month's date is computed from, a property a future version wrote.
    ///
    /// <para><b>Guard</b>, on new API.</para>
    /// </summary>
    [Fact]
    public void CopyingTheDetailsCarriesEveryFieldAcross()
    {
        var original = new DocumentMetadata
        {
            LodgeName = "Placeholder Lodge No. 000",
            IssueMonth = 9,
            IssueYear = 2026,
            IssueDateChosen = true,
            Title = "Sample Trestle Board",
            MeetingRule = "1st Tuesday",
        };

        DocumentMetadata copy = original.Clone();

        Assert.Equal(original.LodgeName, copy.LodgeName);
        Assert.Equal(original.IssueMonth, copy.IssueMonth);
        Assert.Equal(original.IssueYear, copy.IssueYear);
        Assert.Equal(original.IssueDateChosen, copy.IssueDateChosen);
        Assert.Equal(original.Title, copy.Title);
        Assert.Equal(original.MeetingRule, copy.MeetingRule);
        Assert.NotSame(original, copy);
    }
}
