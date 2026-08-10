using System.Reflection;
using Avalonia.Headless;
using TrestleBoard.App.Actions;
using TrestleBoard.App.Integration;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// **The general version of M75's defect** (PLAN.md §12 gate 28, §11 M75).
///
/// <para>The milestone found the same shape three times. <c>DocumentMetadata</c> holds a fact about
/// the newsletter; a widget the user edits holds its <i>own</i> copy of that fact; the widget's
/// answer never flows back — so the metadata keeps its model default forever and everything
/// downstream reads a lie. <c>IssueMonth</c>/<c>IssueYear</c> made every template-started newsletter
/// permanently January 2000 and drew the birthday list from January. <c>MeetingRule</c> made
/// <c>CarryForward.RecomputeMeetingDates</c> fail to parse and <b>blank the cover date</b>.
/// <c>LodgeName</c> sent an email to the whole lodge with no lodge name in the subject.</para>
///
/// <para><b>So the third instance is not what this file is for.</b> It is for making the class of
/// bug hard to reintroduce: every field on <c>DocumentMetadata</c> is enumerated by reflection and
/// must be classified as one of two kinds, and the classification is checked, not trusted —
/// <see cref="EveryFieldSaidToBeAskedForIsActuallyWritten"/> starts a real newsletter down a real
/// route and looks. It is default-deny in the manner of gate 24 and gate 26's
/// <c>OfferIntegrityTests.Classified</c>: a <i>new</i> metadata field fails the build until somebody
/// says which kind it is, which is the question nobody was asked three times running.</para>
///
/// <para>Every person and lodge named here is fictional (PLAN.md §0).</para>
/// </summary>
public sealed class MetadataCensusTests
{
    private const string ClassicTemplate = "classic-414";
    private const int July = 7;

    /// <summary>Fictional (PLAN.md §0). A shipped template carries no lodge name; the wizard asks.</summary>
    private const string Lodge = "Placeholder Lodge No. 000";

    private const string Rule = "1st Tuesday";

    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    // ---- the census ------------------------------------------------------------------------------

    /// <summary>The two kinds a metadata field may be. A field that is neither is the M75 shape.</summary>
    private const string AskedFor = "AskedFor";

    private const string NeverAsked = "NeverAsked";

    /// <summary>
    /// Every settable property of <see cref="DocumentMetadata"/>, and which kind it is.
    ///
    /// <para><b>AskedFor</b>: some production path puts a person's answer in it, and
    /// <see cref="EveryFieldSaidToBeAskedForIsActuallyWritten"/> proves that by running the app.
    /// <b>NeverAsked</b>: nobody is ever asked for it, on purpose, and the reason is written down
    /// here — the same discipline <c>ActionAvailability</c> imposes on a refusal, which may not be
    /// constructed without a reason.</para>
    ///
    /// <para><see cref="DocumentMetadata.HasIssueDate"/> is absent because it has no setter: it is a
    /// reading of two other fields, not a fact of its own, so there is nothing for anyone to
    /// forget to write.</para>
    /// </summary>
    public static readonly (string Field, string Kind, string Why)[] Classified =
    [
        (nameof(DocumentMetadata.LodgeName), AskedFor,
            "M75: the cover heading's wizard asks for it, and — from this change — the answer reaches "
            + "the metadata as well as the banner. Before that it was set only by the two samples, so "
            + "a template-started newsletter emailed the whole lodge with no lodge name in the subject "
            + "and put no author on the PDF"),

        (nameof(DocumentMetadata.IssueMonth), AskedFor,
            "M75 (a): the cover wizard's first screen. The defect this milestone is named after"),

        (nameof(DocumentMetadata.IssueYear), AskedFor, "M75 (a), as IssueMonth"),

        (nameof(DocumentMetadata.IssueDateChosen), AskedFor,
            "M75 (c): set true by the same answer. It is not asked for in its own right — it records "
            + "that the question above was put to somebody, which is the whole of its job"),

        (nameof(DocumentMetadata.MeetingRule), AskedFor,
            "M75 (d): the rule typed into the cover banner now reaches the metadata, so "
            + "CarryForward.RecomputeMeetingDates can parse it instead of blanking the cover date"),

        (nameof(DocumentMetadata.Title), NeverAsked,
            "M75 (g), settled deliberately: every one of these files is a trestle board, so the app "
            + "can get the title right without asking. IssueNaming.DefaultTitle is the one fallback "
            + "and every place that shows a title goes through IssueNaming.Title, which is never "
            + "blank. A second question on the way into a newsletter is a second question to get "
            + "wrong. Somebody who wants “Trestle Board Extra” can still type it and have it kept"),

        (nameof(DocumentMetadata.ExtraProperties), NeverAsked,
            "not a fact about the newsletter at all: it is where System.Text.Json parks properties "
            + "written by a newer build, so an old TrestleBoard does not throw a future field away. "
            + "The serializer writes it and the serializer reads it; no person is involved"),
    ];

    /// <summary>
    /// **The default-deny half.** Every settable field on <c>DocumentMetadata</c> is classified, and
    /// nothing is classified that is no longer there.
    ///
    /// <para>This proves nothing about whether a given field is handled <i>correctly</i>. It proves
    /// that the question was asked — and the question going unasked is exactly how a newsletter came
    /// to be permanently January 2000 for sixty milestones. The next person to add a field to this
    /// class has to say which kind it is before the suite will go green.</para>
    /// </summary>
    [Fact]
    public void EveryFieldOnDocumentMetadataIsClassified()
    {
        List<string> fields = [.. SettableFields().Select(p => p.Name)];

        // Anti-vacuity, gate 24's habit: a reflection query that quietly stopped finding anything
        // would pass this test while checking nothing at all.
        Assert.True(fields.Count >= 6, $"only {fields.Count} settable metadata fields were found");

        List<string> unclassified = [.. fields.Where(f => !Classified.Any(c => c.Field == f))];
        Assert.True(
            unclassified.Count == 0,
            "these facts live on the newsletter and nobody has said whether a person ever sets them:\n  "
            + string.Join("\n  ", unclassified)
            + "\n\nAdd each to MetadataCensusTests.Classified as AskedFor — some production path "
            + "writes a person's answer into it, and the test below will check that it really does — "
            + "or NeverAsked, with the reason nobody is ever asked. A field nobody writes keeps its "
            + "model default forever and everything downstream reads it as fact. PLAN.md §12 gate 28.");

        List<string> stale = [.. Classified.Select(c => c.Field).Where(f => !fields.Contains(f))];
        Assert.True(
            stale.Count == 0,
            "the classification names fields that are no longer on DocumentMetadata: "
            + string.Join(", ", stale)
            + " — a record that has stopped matching the code is the thing this milestone is about.");

        List<string> duplicated = [.. Classified
            .GroupBy(c => c.Field)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)];
        Assert.True(duplicated.Count == 0, "classified twice, with two different answers: " + string.Join(", ", duplicated));

        List<string> silent = [.. Classified
            .Where(c => string.IsNullOrWhiteSpace(c.Why))
            .Select(c => c.Field)];
        Assert.True(
            silent.Count == 0,
            "these are classified with no reason given: " + string.Join(", ", silent)
            + " — a field is not allowed to go unwritten without saying why, for the same reason "
            + "ActionAvailability refuses to refuse without one.");

        List<string> odd = [.. Classified
            .Where(c => c.Kind is not (AskedFor or NeverAsked))
            .Select(c => $"{c.Field} is “{c.Kind}”")];
        Assert.True(odd.Count == 0, "there are two kinds: " + string.Join(", ", odd));
    }

    /// <summary>
    /// **The half that is proved rather than held.** Start a newsletter the way a real user does —
    /// from a shipped template, answering the cover wizard — and every field classified
    /// <see cref="AskedFor"/> must have moved off the value <c>new DocumentMetadata()</c> gives it.
    ///
    /// <para>That is the whole defect stated as an assertion. Each of the three holes M75 found was a
    /// field whose model default survived a full trip through the app with a person answering
    /// questions the entire way.</para>
    /// </summary>
    [Fact]
    public async Task EveryFieldSaidToBeAskedForIsActuallyWritten()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(July, 2026, Rule, Lodge);
            Assert.True(window.OpenTemplateAsync(ClassicTemplate).Result);

            DocumentMetadata started = window.SessionForTest!.Document.Metadata;
            var untouched = new DocumentMetadata();

            List<string> stillDefault = [];
            foreach (PropertyInfo property in SettableFields())
            {
                if (Classified.FirstOrDefault(c => c.Field == property.Name).Kind != AskedFor)
                {
                    continue;
                }

                if (Equals(property.GetValue(started), property.GetValue(untouched)))
                {
                    stillDefault.Add($"{property.Name} is still {Describe(property.GetValue(untouched))}");
                }
            }

            Assert.True(
                stillDefault.Count == 0,
                "a person answered the cover wizard from end to end and these facts about the "
                + "newsletter never heard about it:\n  " + string.Join("\n  ", stillDefault)
                + "\n\nEither the answer is not written through (the M75 defect — fix it, following "
                + "SetMetadataCommand composed into the wizard's own undo step), or nobody is in fact "
                + "asked and the field belongs on the NeverAsked list with a reason.");

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- the third instance: LodgeName -----------------------------------------------------------

    /// <summary>
    /// **Guard (M75, the LodgeName hole).** The lodge name typed into the cover heading now reaches
    /// the newsletter itself, not only the banner drawn on page one.
    ///
    /// <para>Verified failing before the fix: the assertions on <c>Metadata.LodgeName</c> and on the
    /// mail subject failed, because the wizard's answer stopped at <c>CoverBannerData.LodgeName</c>.
    /// The lodge that emails sixty brothers a newsletter got a subject line reading
    /// <c>"Trestle Board — July 2026"</c> with no lodge in it, and a PDF whose Author was blank.</para>
    /// </summary>
    [Fact]
    public async Task TheLodgeNameTypedOnTheCoverReachesTheNewsletterItself()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(July, 2026, Rule, Lodge);
            Assert.True(window.OpenTemplateAsync(ClassicTemplate).Result);

            DocumentMetadata meta = window.SessionForTest!.Document.Metadata;
            Assert.Equal(Lodge, meta.LodgeName);

            // What sixty brothers see in their inbox, and what the PDF says about itself.
            Assert.Equal(
                $"{Lodge} Trestle Board — July 2026",
                MailHandoff.Subject(meta.LodgeName, meta.Title, meta.IssueYear, meta.IssueMonth));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// **Guard (M75, the LodgeName hole — the later-answer path).** Re-opening the cover wizard to
    /// correct the lodge name corrects the newsletter too, in the same one undo step as the rest of
    /// the answer, and Ctrl+Z takes the old name back.
    ///
    /// <para>Verified failing before the fix: the corrected name never left the banner.</para>
    /// </summary>
    [Fact]
    public async Task CorrectingTheLodgeNameLaterCorrectsTheNewsletterInOneUndoStep()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(July, 2026, Rule, Lodge);
            Assert.True(window.OpenTemplateAsync(ClassicTemplate).Result);

            const string Corrected = "Example Lodge No. 111";
            window.AnswerTheIssueWizardForTest =
                new MainWindow.IssueAnswerForTest(July, 2026, Rule, Corrected);
            Assert.Equal(ActionOutcome.Did, window.ActionsForTest.RunAsync(ActionId.SetIssueDate).Result);

            Assert.Equal(Corrected, window.SessionForTest!.Document.Metadata.LodgeName);

            window.Undo();
            Assert.Equal(Lodge, window.SessionForTest.Document.Metadata.LodgeName);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- Title: NeverAsked, but never blank on screen either --------------------------------------

    /// <summary>
    /// **Guard (M75, the census pass).** The title bar of the window names the newsletter even when
    /// nobody typed a title — because nobody is ever asked for one.
    ///
    /// <para>Verified failing before the fix: a template-started newsletter left
    /// <c>Metadata.Title</c> empty, and the window read <b>"TrestleBoard —  — not saved yet"</b>,
    /// with the blank where the name should be. The decision that the title is defaulted rather than
    /// asked for was taken in M75 (g) and put in <c>IssueNaming.Title</c>; this was the one place
    /// still reading the raw field, so the decision held everywhere the name is printed and failed on
    /// the one line the user looks at all day.</para>
    /// </summary>
    [Fact]
    public async Task TheWindowNamesTheNewsletterEvenWhenNobodyTypedATitle()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(July, 2026, Rule, Lodge);
            Assert.True(window.OpenTemplateAsync(ClassicTemplate).Result);

            // The trap: the shipped template carries no title, and nothing ever asks for one.
            Assert.Equal(string.Empty, window.SessionForTest!.Document.Metadata.Title);

            Assert.Contains(IssueNaming.DefaultTitle, window.Title!, StringComparison.Ordinal);
            Assert.DoesNotContain("—  —", window.Title!, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- scaffolding -----------------------------------------------------------------------------

    /// <summary>Every fact a newsletter can carry that somebody could forget to write.</summary>
    private static IEnumerable<PropertyInfo> SettableFields() =>
        typeof(DocumentMetadata)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetSetMethod() is not null)
            .OrderBy(p => p.Name, StringComparer.Ordinal);

    private static string Describe(object? value) => value switch
    {
        null => "unset",
        string text when text.Length == 0 => "blank",
        _ => $"“{value}”",
    };
}
