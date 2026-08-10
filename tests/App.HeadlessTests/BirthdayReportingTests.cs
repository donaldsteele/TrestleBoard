using System.Text.Json;
using Avalonia.Headless;
using TrestleBoard.App.Integration;
using TrestleBoard.App.Settings;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Roster;
using TrestleBoard.Widgets.Builtins.BirthdayList;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// PLAN.md §12 gate 28 (M75 (e), (f) and (g)), through the real shell: what the app <b>says</b> when
/// it brings in birthdays, and what it says when it does not.
///
/// <para>Every sentence here is asserted whole rather than by keyword. These are the words the owner
/// was reading while he hunted a bug for two agents' worth of work, and a test that only checks a
/// sentence is non-empty is how they stayed wrong for sixty milestones.</para>
///
/// <para>Every person here is fictional and lives in a book this test made (PLAN.md §0 rule 5).</para>
/// </summary>
public sealed class BirthdayReportingTests
{
    private const string BirthdayBlockId = "w-birthdays";
    private const string ClassicTemplate = "classic-414";
    private const int July = 7;
    private const string Lodge = "Placeholder Lodge No. 000";

    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>
    /// M75 (e) defect 3: "There is no birthday list on this newsletter yet" — said with a birthday
    /// list on page three.
    ///
    /// <para>Failed before the fix: the search that precedes this sentence looks for a list worth
    /// acting on, found none, and reported its own emptiness as the newsletter's. The user is sent
    /// to the Insert menu to add a second birthday list.</para>
    /// </summary>
    [Fact]
    public async Task WithABirthdayListOnThePageItDoesNotSayThereIsNoBirthdayList()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(BookBornIn(11));
            window.FramesForTest!.Select(null);

            Assert.False(window.SyncBirthdaysAsync().GetAwaiter().GetResult());

            Assert.Equal(
                "Nobody in your address book has a birthday in July, which is the month this issue "
                + "is for, so the birthday list was left as it is.",
                window.StatusLabelTextForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The other half of defect 3: with somebody born this month and a list somebody typed, the app
    /// says what is true about that list rather than calling it up to date. "Already up to date" is
    /// reserved for a list that was actually filled in from the address book.
    /// </summary>
    [Fact]
    public async Task AHandTypedListIsNotDescribedAsUpToDateWithABookItWasNeverAsked()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(BookBornIn(July));
            window.FramesForTest!.Select(null);

            Assert.False(window.SyncBirthdaysAsync().GetAwaiter().GetResult());

            Assert.Equal(
                "The birthday list on this newsletter was typed in by hand, so TrestleBoard has "
                + "left it alone. Choose it on the page first if you would like the July birthdays "
                + "brought in.",
                window.StatusLabelTextForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M75 (e) defect 4: "The birthday list already matches your address book", having matched
    /// nobody at all.
    ///
    /// <para>Failed before the fix: the sentence was the same whether the address book had put
    /// twelve names on the page or none, which is exactly the reassurance that kept the owner
    /// looking somewhere else.</para>
    /// </summary>
    [Fact]
    public async Task MatchingNobodySaysSoAndNamesTheMonth()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(BookBornIn(11));
            window.BirthdayConfirmForTest = _ => true;
            window.FramesForTest!.Select(BirthdayBlockId);

            Assert.True(window.SyncBirthdaysAsync().GetAwaiter().GetResult());

            Assert.Equal(
                "Nobody from your address book has a birthday in July, which is the month this "
                + "issue is for, so there was nothing to bring in. Anything you typed in yourself "
                + "has been left alone.",
                window.StatusLabelTextForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// And the distinction the sentence now carries: a list that really does hold this month's
    /// people is told so, with the month named.
    /// </summary>
    [Fact]
    public async Task MatchingTheRightPeopleSaysSoAndNamesTheMonthToo()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(BookBornIn(July));
            window.BirthdayConfirmForTest = _ => true;
            window.FramesForTest!.Select(BirthdayBlockId);

            // The first run brings him in; the second finds nothing to change.
            Assert.True(window.SyncBirthdaysAsync().GetAwaiter().GetResult());
            Assert.True(window.SyncBirthdaysAsync().GetAwaiter().GetResult());

            Assert.Equal(
                "The birthday list already matches the July birthdays in your address book.",
                window.StatusLabelTextForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M75 (e) defect 5: the Insert path said <b>nothing at all</b>. It returned null and the plain
    /// empty wizard opened, leaving somebody who had just pressed "Birthdays" to work out for
    /// himself whether the address book, the month or the program was at fault.
    ///
    /// <para>Failed before the fix: the status bar was empty.</para>
    /// </summary>
    [Fact]
    public async Task TheInsertPathSaysWhyTheWizardIsAboutToOpenEmpty()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(BookBornIn(11));
            window.Announce(string.Empty);

            Assert.Null(window.OfferBirthdaysFromRosterAsync().GetAwaiter().GetResult());

            Assert.Equal(
                "Nobody in your address book has a birthday in July, which is the month this issue "
                + "is for, so the birthday list opens empty for you to type into.",
                window.StatusLabelTextForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>An empty address book is a different fact, and gets a different sentence.</summary>
    [Fact]
    public async Task AndSaysSomethingElseWhenTheAddressBookIsEmpty()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(NewRoster());
            window.Announce(string.Empty);

            Assert.Null(window.OfferBirthdaysFromRosterAsync().GetAwaiter().GetResult());

            Assert.Equal(
                "Your address book is empty, so there are no birthdays to bring in. The birthday "
                + "list opens empty for you to type into.",
                window.StatusLabelTextForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M75 (e) defect 2, end to end and the way a real user meets it: a newsletter started from a
    /// shipped template, an empty birthday list on the page, nothing selected — and the card offers
    /// to fill it in.
    ///
    /// <para>Failed before the fix: <c>IsStale</c> answers false at its first line for a manual
    /// list, a template's list is manual, so the card said nothing and the feature never advertised
    /// itself on a new newsletter — the one moment it is most wanted.</para>
    /// </summary>
    [Fact]
    public async Task ANewNewsletterOffersToFillInItsEmptyBirthdayList()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.UseRosterForTest(BookBornIn(July));
            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(July, 2026, LodgeName: Lodge);
            Assert.True(window.OpenTemplateAsync(ClassicTemplate).Result);

            window.FramesForTest!.Select(null);
            window.RefreshActions();

            NextStep step = Assert.Single(
                WhatsNext.Suggestions(window.CurrentActionContext),
                s => s.ActionId == ActionId.SyncBirthdays);
            Assert.Equal("Fill in the birthday list", step.Title);
            Assert.Contains("born in July", step.Why, StringComparison.Ordinal);

            // Gate 24: the row leads to a button that can actually do it, from where the user is
            // standing — with nothing chosen at all.
            window.BirthdayConfirmForTest = _ => true;
            Assert.True(window.SyncBirthdaysAsync().GetAwaiter().GetResult());

            BirthdayListData list = ReadBirthdays(window);
            Assert.Equal(BirthdayListSource.Roster, list.Source);
            Assert.Equal(July, list.SourceMonth);
            Assert.Contains(list.Entries, e => e.Name == "A. Placeholder");

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M75 (f), the plumbing: <c>RosterService</c> has known since M24 that the address book file
    /// could not be read, and the answer stopped there. It now reaches the action surface, and the
    /// refusal the user meets is about a file that would not open rather than a book he never filled
    /// in.
    ///
    /// <para>Failed before the fix — <c>ActionContext</c> had no such fact to carry, and the sync
    /// was refused with "Your address book is empty, so there are no birthdays to bring in. Import
    /// your member list first", which over a locked file invites him to overwrite the very list that
    /// could not be read.</para>
    /// </summary>
    [Fact]
    public async Task AnAddressBookThatWouldNotOpenIsNotReportedAsAnEmptyOne()
    {
        await Session.Dispatch(() =>
        {
            // Damaged from outside the app — a truncated write from a sync client is the realistic
            // shape of this, and it is the shape RosterUnreadableTests already uses.
            string folder = Path.Combine(AppPaths.Root, "birthday-reporting-tests", "unreadable");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "roster.json");
            File.WriteAllText(path, "{\"members\": [ truncated");

            var roster = new RosterService(new RosterStore(path));
            Assert.True(roster.CouldNotBeRead);

            var window = new MainWindow();
            window.OpenIssueSample();
            window.UseRosterForTest(roster);
            window.FramesForTest!.Select(BirthdayBlockId);
            window.RefreshActions();

            Assert.True(window.CurrentActionContext.RosterCouldNotBeRead);

            ActionAvailability availability =
                ActionCatalog.Evaluate(ActionId.SyncBirthdays, window.CurrentActionContext);
            Assert.Equal(ActionCatalog.CouldNotReadTheAddressBook, availability.Reason);
            Assert.DoesNotContain("is empty", availability.Reason, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- M75 (g): the blast radius, pinned -------------------------------------------------------

    /// <summary>
    /// The five things a wrong issue date reached, checked against a newsletter that has been asked:
    /// the suggested PDF name, the PDF's own Subject, the Save-as name, the mail subject to the
    /// whole lodge, and the archive lookup for "what we said last year".
    ///
    /// <para><b>The leading space was not the issue date.</b> <c>" 2000-01.pdf"</c> begins with a
    /// space because a shipped template leaves <c>Metadata.Title</c> empty and the name was built as
    /// title-space-date. Fixing (a)–(d) fixes the date and leaves the space, which is why this test
    /// starts from a template rather than from a sample: the sample has a title and would have hidden
    /// it. See <c>IssueNaming</c> for why the title is defaulted rather than asked for.</para>
    /// </summary>
    [Fact]
    public async Task EverythingNamedAfterTheIssueIsNamedAfterTheRightIssue()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(July, 2026, LodgeName: Lodge);
            Assert.True(window.OpenTemplateAsync(ClassicTemplate).Result);

            DocumentMetadata meta = window.SessionForTest!.Document.Metadata;
            Assert.Equal(string.Empty, meta.Title); // the template ships without one — the trap

            // The suggested PDF name, the draft copy and the Save-as name all come from this stem.
            Assert.Equal("Trestle Board 2026-07", IssueNaming.FileStem(meta));
            Assert.False(
                IssueNaming.FileStem(meta).StartsWith(' '),
                "the suggested file name still begins with a space");

            // What every distributed copy of the PDF says about itself.
            Assert.Equal("Trestle board 2026-07", IssueNaming.PdfSubject(meta));
            Assert.Equal("Trestle Board", IssueNaming.Title(meta));

            // The subject line on an email to sixty people. It reads "July 2026", which is the whole
            // point of (g): before M75 this said "January 2000".
            //
            // The lodge NAME is absent, and that is a separate write-back gap of exactly M75 (d)'s
            // shape, recorded here rather than quietly asserted away: the wizard's lodge-name answer
            // reaches the cover banner's own data and never reaches Metadata.LodgeName, so a
            // newsletter not descended from a sample has none. It is not in M75's deliverables and
            // is not fixed here; the sentence stays true and plain either way, which is why it can
            // wait. If somebody closes that gap, this line is where it will be noticed.
            Assert.Equal(string.Empty, meta.LodgeName);
            Assert.Equal(
                "Trestle Board — July 2026",
                MailHandoff.Subject(meta.LodgeName, meta.Title, meta.IssueYear, meta.IssueMonth));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// "What we said last year" searches the archive with the newsletter's own issue date. Before
    /// M75 that was January 1999, and the user was told his archive folder held nothing —
    /// <i>after</i> being made to choose it.
    /// </summary>
    [Fact]
    public async Task WhatWeSaidLastYearLooksForTheIssueTheNewsletterActuallyIs()
    {
        await Session.Dispatch(() =>
        {
            string folder = Path.Combine(AppPaths.Root, "birthday-reporting-tests", "archive");
            Directory.CreateDirectory(folder);
            foreach (string old in Directory.GetFiles(folder, "*.tboard"))
            {
                File.Delete(old);
            }

            Core.Container.TboardPackage lastYear = Core.Samples.SampleIssue.CreatePackage();
            lastYear.Document.Metadata.IssueMonth = July;
            lastYear.Document.Metadata.IssueYear = 2025;
            Core.Container.TboardContainer.SaveToFile(lastYear, Path.Combine(folder, "2025-07.tboard"));

            var window = new MainWindow();
            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(July, 2026, LodgeName: Lodge);
            Assert.True(window.OpenTemplateAsync(ClassicTemplate).Result);

            DocumentMetadata meta = window.SessionForTest!.Document.Metadata;
            PastIssue found = PastIssues.Find(folder, meta.IssueMonth, meta.IssueYear);

            Assert.True(found.Opened);
            Assert.Equal(2025, found.Package!.Document.Metadata.IssueYear);
            Assert.Equal(July, found.Package.Document.Metadata.IssueMonth);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- fictional scaffolding -------------------------------------------------------------------

    private static MainWindow Windowed(RosterService roster)
    {
        var window = new MainWindow();
        window.OpenIssueSample();
        window.UseRosterForTest(roster);
        return window;
    }

    private static RosterService NewRoster(
        [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        string folder = Path.Combine(AppPaths.Root, "birthday-reporting-tests", name);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "roster.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return new RosterService(new RosterStore(path));
    }

    /// <summary>One fictional brother, born in whichever month the test needs him.</summary>
    private static RosterService BookBornIn(
        int month, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        RosterService roster = NewRoster(name);
        roster.Save(
            new Member { Id = "m-a", DisplayName = "A. Placeholder", BirthMonth = month, BirthDay = 3 },
            "Add a person");
        return roster;
    }

    private static BirthdayListData ReadBirthdays(MainWindow window)
    {
        var definition = new BirthdayListDefinition();
        Assert.True(window.SessionForTest!.Document.TryFindBlock(BirthdayBlockId, out _, out Block? block));
        using JsonDocument document = JsonDocument.Parse(((WidgetBlock)block!).Data!.Value.GetRawText());
        Assert.True(definition.TryReadData(
            document.RootElement.Clone(), definition.CurrentDataVersion, out object typed));
        return Assert.IsType<BirthdayListData>(typed);
    }
}
