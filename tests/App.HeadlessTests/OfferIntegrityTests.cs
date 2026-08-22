using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TrestleBoard.App.Actions;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Help;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Editing.Help;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// **Verification gate 26 — offer integrity** (PLAN.md §12.26, §11 M73 (h)).
///
/// <para>The rule: <i>for every control with a handler, the predicate that gates its rendering,
/// visibility or enablement must BE the handler's precondition — obtained from it, not restated
/// beside it.</i> The three defects behind M73 were all the same shape. The cover-date suggestion
/// was drawn on "the cover has no date" while its handler needed a selection. The memorial was
/// offered on "he is recorded as passed" while its handler needed a caret. "Put them all back" was
/// drawn on a document-wide count while its handler worked on one story.</para>
///
/// <para><b>What is mechanically provable here, and what is not.</b> Gate 24 could be a real proof
/// because <see cref="ActionCatalog.Evaluate"/> is a pure function of an
/// <see cref="ActionContext"/> — luck the dialogs do not share. So this gate is three different
/// strengths of check, and they are labelled rather than blended:</para>
///
/// <list type="number">
///   <item><b>Proved, by running the app.</b> The menu bar, the toolbar and the help window are
///   driven headlessly through real contexts and their drawn state is compared with the catalog's
///   answer. Nothing is trusted; every offer on those three surfaces is enumerated.</item>
///   <item><b>Proved elsewhere.</b> The action panel is gate 24's and
///   <see cref="ActionSurfaceTests"/>'; the memorial card is
///   <c>PassedBrotherShellTests.WithNoNewsletterOpenTheCardDoesNotOfferAMemorial</c>. This file
///   asserts the wiring those tests depend on rather than repeating them.</item>
///   <item><b>Not proved — held.</b> For a dialog that gates a button on something other than the
///   catalog there is no pure function to compare against, so the check is that <i>every such
///   control is enumerated from the source and classified</i>. It cannot tell a correct predicate
///   from an incorrect one. It can, and does, refuse to let a new gated control appear without
///   somebody saying which kind it is — which is the step that was skipped each of the three
///   times.</item>
/// </list>
///
/// <para><b>What this must NOT flag</b>, the guard gate 24 carries for the same reason: a control
/// that is <i>sometimes</i> unavailable and explains itself is M11 working exactly as designed.
/// This gate is about <i>agreement</i> between the offer and the handler, never about availability.
/// A greyed menu item with a plain-English reason must sail through it, and
/// <see cref="TheGateDoesNotObjectToAnHonestRefusal"/> asserts that it does.</para>
/// </summary>
public sealed class OfferIntegrityTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    // ---- 1. the surfaces that CAN be checked against a pure function ---------------------------

    /// <summary>
    /// **The gate, on the three surfaces where it is a proof.** Every menu item and every toolbar
    /// button that carries an action id is drawn from the catalog's answer for the live context —
    /// so the predicate is not merely equivalent to the handler's precondition, it is the same call.
    ///
    /// <para>Four real states are walked, because a predicate that happens to agree in one state is
    /// exactly the bug: "Fill in the meeting date" agreed with its handler in every state but the
    /// one it was drawn in.</para>
    /// </summary>
    [Fact]
    public async Task EverySurfaceThatDrawsACatalogActionAsksTheCatalog()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;

            var disagreed = new List<string>();
            var silent = new List<string>();
            int offersChecked = 0;
            int refusalsSeen = 0;

            foreach ((string situation, Action arrange) in Situations(window))
            {
                arrange();
                window.RefreshActions();
                Dispatcher.UIThread.RunJobs();

                foreach ((string actionId, Control control) in TaggedControls(window))
                {
                    ActionAvailability can =
                        ActionCatalog.Evaluate(actionId, window.CurrentActionContext);
                    offersChecked++;

                    if (control.IsEnabled != can.IsAvailable)
                    {
                        disagreed.Add(
                            $"{situation}: {actionId} is drawn "
                            + (control.IsEnabled ? "enabled" : "greyed")
                            + " and the catalog says " + (can.IsAvailable ? "yes" : "no"));
                    }

                    if (can.IsAvailable)
                    {
                        continue;
                    }

                    refusalsSeen++;
                    if (string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(control)))
                    {
                        silent.Add($"{situation}: {actionId}");
                    }
                }
            }

            Assert.True(
                disagreed.Count == 0,
                "a control's own predicate has drifted from the catalog's answer:\n  "
                + string.Join("\n  ", disagreed)
                + "\n\nEvery surface must draw from ActionCatalog.Evaluate rather than deciding for "
                + "itself. PLAN.md §12 gate 26.");

            // A refusal that reaches the screen with no words is the M11 half of the same rule: the
            // predicate came from the handler, and so must the sentence explaining it.
            Assert.True(
                silent.Count == 0,
                "greyed with no reason where a screen reader would find it: " + string.Join(", ", silent));

            Assert.True(offersChecked > 200, $"only {offersChecked} offers were inspected");
            Assert.True(refusalsSeen > 10, $"only {refusalsSeen} refusals were seen — nothing was tested");

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The help window is the surface M73 (g) caught: "Take me there" was decided once, when the
    /// answer opened, and went on being wrong afterwards. This walks **every answer in the index**
    /// and asserts the button's visibility is the catalog's answer for the live context — the whole
    /// index, not a sample, because the index is generated and grows on its own.
    /// </summary>
    [Fact]
    public async Task TheHelpWindowsOfferIsTheCatalogsAnswerForEveryAnswerItHolds()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenIssueSample();
            window.Measure(new Avalonia.Size(1280, 860));
            window.Arrange(new Avalonia.Rect(0, 0, 1280, 860));
            Dispatcher.UIThread.RunJobs();

            try
            {
                window.ShowHowDoI();
                HelpWindow help = window.HelpWindowForTest!;
                help.TypeForTest("");
                Dispatcher.UIThread.RunJobs();

                IReadOnlyList<HelpTopic> topics = help.ShowingForTest;
                var wrong = new List<string>();
                int offered = 0;
                int refused = 0;

                for (int i = 0; i < topics.Count; i++)
                {
                    help.ChooseForTest(i);
                    Dispatcher.UIThread.RunJobs();

                    HelpTopic topic = topics[i];
                    bool can = ActionCatalog
                        .Evaluate(topic.ActionId, window.CurrentActionContext).IsAvailable;

                    if (can)
                    {
                        offered++;
                    }
                    else
                    {
                        refused++;
                    }

                    if (help.TakeMeThereForTest.IsVisible != can)
                    {
                        wrong.Add($"{topic.ActionId}: offered={help.TakeMeThereForTest.IsVisible}, catalog={can}");
                    }

                    // M11's other half: when it is not offered, the answer says why in the catalog's
                    // own words. Silence here reads as the help being broken.
                    if (!can)
                    {
                        Assert.Contains(
                            ActionCatalog.Evaluate(topic.ActionId, window.CurrentActionContext).Reason,
                            help.WhereItIsTextForTest,
                            StringComparison.Ordinal);
                    }
                }

                Assert.True(
                    wrong.Count == 0,
                    "the help window offered a command the catalog would refuse, or hid one it would "
                    + "allow:\n  " + string.Join("\n  ", wrong));

                Assert.True(topics.Count > 60, $"only {topics.Count} answers were walked");
                Assert.True(offered > 5 && refused > 5,
                    $"the walk saw {offered} available and {refused} refused — one of the two branches "
                    + "was never exercised, so the comparison proved nothing");

                help.Close();
            }
            finally
            {
                window.Close();
            }

            await Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    // ---- 2. the wiring the behavioural tests depend on ------------------------------------------

    /// <summary>
    /// The memorial, which is the defect that made this gate a gate.
    ///
    /// <para>The behaviour is proved in
    /// <c>PassedBrotherShellTests.WithNoNewsletterOpenTheCardDoesNotOfferAMemorial</c>: with the
    /// precondition false the card offers Close instead of "Write a memorial". What that test cannot
    /// see is whether the predicate it is handed is <b>the same one the handler consults</b> — a
    /// second, equivalent-looking expression would pass it and would be exactly the defect. That is
    /// what is asserted here, and it is asserted on source text, so it proves the two names match
    /// and nothing about what either means.</para>
    /// </summary>
    [Fact]
    public void TheShellHandsThePeopleCardItsOwnPrecondition()
    {
        string shell = Source("MainWindow.axaml.cs");

        Assert.Matches(
            new Regex(@"new PeopleWindow\(\s*Roster,\s*\(\)\s*=>\s*CanWriteAMemorial\s*\)"),
            shell);

        // …and the handler refuses on that same member, rather than on a restatement of it.
        Assert.Matches(new Regex(@"if\s*\(\s*!CanWriteAMemorial\s*\)"), shell);

        // The window must not be able to invent its own answer: the only default is the harmless
        // one used by the tests that do not care.
        string card = Source("Dialogs/PeopleWindow.cs");
        Assert.Contains("private readonly Func<bool> _canWriteAMemorial;", card, StringComparison.Ordinal);
        Assert.Contains("bool canWrite = _canWriteAMemorial();", card, StringComparison.Ordinal);
    }

    // ---- 3. the part that is held rather than proved --------------------------------------------

    /// <summary>
    /// Every pressable control in the app whose visibility or enablement is decided in code, and
    /// what decides it. Enumerated from the source by <see cref="GatedControls"/>; the entries here
    /// are the classification, and the test below fails in **both** directions — an unclassified
    /// control, or an entry for a control that no longer exists.
    ///
    /// <para>The three kinds, from M73 (h). <b>FromTheCatalog</b>: the predicate is
    /// <c>ActionCatalog.Evaluate</c>, so it cannot disagree. <b>FromTheOwner</b>: the value is
    /// handed in by whoever carries the press out. <b>ItsOwnState</b>: the window that draws the
    /// control is also the object that performs it, so the predicate and the precondition read the
    /// same field and cannot drift apart. A control that fits none of the three is the M73 shape and
    /// must be re-cut until it does.</para>
    /// </summary>
    public static readonly (string Where, string Control, string Kind, string Why)[] Classified =
    [
        ("FindWindow.cs", "_replace", "ItsOwnState",
            "the replace row is a mode of this window, not an availability; FindController answers "
            + "its own refusals out loud when the press arrives"),
        ("FindWindow.cs", "_replaceAll", "ItsOwnState", "as _replace"),

        ("HelpWindow.cs", "_takeMeThere", "FromTheCatalog",
            "visibility is can.IsAvailable, from the ask delegate the shell handed in — and since "
            + "M73 (g) it is asked again whenever the answer could have changed"),

        ("PeopleWindow.cs", "_delete", "ItsOwnState",
            "this window holds the selection and performs the delete"),

        ("PhraseWindow.cs", "_back", "ItsOwnState", "the step this window is on, and it does the stepping"),
        ("PhraseWindow.cs", "_next", "ItsOwnState", "as _back"),
        ("ReviewWindow.cs", "_back", "ItsOwnState", "as PhraseWindow._back"),
        ("SpellingWindow.cs", "_back", "ItsOwnState", "as PhraseWindow._back"),
        ("SpellingWindow.cs", "_next", "ItsOwnState", "as PhraseWindow._back"),
        ("TourWindow.cs", "_back", "ItsOwnState", "as PhraseWindow._back"),

        ("ReadAloudWindow.cs", "_next", "FromTheOwner",
            "_session.Finished and _session.IsEmpty are read off the walk itself, which is the thing "
            + "the press advances"),

        ("RosterImportWindow.cs", "_back", "FromTheOwner", "_session.Step is the import's own position"),
        ("RosterImportWindow.cs", "_stop", "FromTheOwner",
            "M73 (b1): on the Done step the press could not undo the import it claimed to stop, so "
            + "the control is gone on that step rather than lying on it"),

        ("TextStylesWindow.cs", "_showOverrides", "FromTheOwner",
            "the count is handed in by the shell that owns the document"),
        ("TextStylesWindow.cs", "_clearOverrides", "FromTheOwner",
            "M73 (b2): the same count, and since that fix the handler puts back exactly the set the "
            + "count describes — before it, this was the offer and the handler disagreeing"),

        ("WizardWindow.cs", "_back", "FromTheOwner", "_session.IsFirstScreen belongs to the wizard session"),
        ("WizardWindow.cs", "_showAll", "FromTheOwner", "_session.CurrentRowIndex, as _back"),
    ];

    /// <summary>
    /// **The default-deny half of the gate.** A new gated button is a new chance to restate a
    /// precondition, and every one of the three defects got in because nobody was asked the
    /// question. This asks it at build time.
    ///
    /// <para>It proves nothing about whether a given predicate is <i>right</i>. It proves that the
    /// list of places where the question arises is complete and current, which is the thing PLAN.md
    /// M70 claimed to have and did not.</para>
    /// </summary>
    [Fact]
    public void EveryControlGatedInCodeIsClassified()
    {
        var found = GatedControls().ToList();

        var unclassified = found
            .Where(site => !Classified.Any(c => c.Where == site.Where && c.Control == site.Control))
            .Select(site => $"{site.Where}:{site.Line} {site.Control}.{site.Property} = {site.Expression}")
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            "these pressable controls are shown, hidden or greyed by a predicate written in code, and "
            + "nobody has said where that predicate comes from:\n  "
            + string.Join("\n  ", unclassified)
            + "\n\nAdd each to OfferIntegrityTests.Classified as FromTheCatalog, FromTheOwner or "
            + "ItsOwnState — or, if it is none of those, it is restating a precondition it should be "
            + "obtaining. PLAN.md §12 gate 26.");

        var stale = Classified
            .Where(c => !found.Any(site => site.Where == c.Where && site.Control == c.Control))
            .Select(c => $"{c.Where} {c.Control}")
            .ToList();

        Assert.True(
            stale.Count == 0,
            "the classification lists controls that are no longer gated anywhere: "
            + string.Join(", ", stale)
            + " — a record that has stopped matching the app is the thing this milestone is about.");
    }

    // ---- 4. the guard, and the anti-vacuity check ------------------------------------------------

    /// <summary>
    /// The guard from M71 (d), which gate 24 carries for the same reason and which this gate needs
    /// more, being wider: **a control that is sometimes unavailable and explains itself is correct**.
    /// If this ever fails, the gate above has become an objection to M11 itself and will be switched
    /// off by whoever it annoys.
    /// </summary>
    [Fact]
    public async Task TheGateDoesNotObjectToAnHonestRefusal()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenSample();
            window.FramesForTest!.ClearSelection();
            window.RefreshActions();
            Dispatcher.UIThread.RunJobs();

            // "Delete this" with nothing selected: refused, with a reason, and drawn greyed in the
            // menu bar carrying that reason. Three of the assertions above run over this very
            // control in this very state, and they pass — because the offer and the handler agree
            // that it cannot run right now, which is agreement, not a defect.
            ActionAvailability can =
                ActionCatalog.Evaluate(ActionId.DeleteFrame, window.CurrentActionContext);
            Assert.False(can.IsAvailable);
            Assert.False(string.IsNullOrWhiteSpace(can.Reason));

            Control greyed = TaggedControls(window)
                .First(c => c.ActionId == ActionId.DeleteFrame).Control;
            Assert.False(greyed.IsEnabled);
            Assert.Equal(can.Reason, AutomationProperties.GetHelpText(greyed));

            // And the same command, in the state where it CAN run, is offered. The gate's subject is
            // the agreement, and it is satisfied on both sides of the same control.
            string blockId = window.FramesForTest.AddTextFrame(0);
            window.FramesForTest.Select(blockId);
            window.RefreshActions();
            Dispatcher.UIThread.RunJobs();

            Assert.True(ActionCatalog.Evaluate(ActionId.DeleteFrame, window.CurrentActionContext).IsAvailable);
            Assert.True(TaggedControls(window)
                .First(c => c.ActionId == ActionId.DeleteFrame).Control.IsEnabled);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Anti-vacuity, as gate 24 has: if the enumeration ever comes back empty the gate passes by
    /// having nothing to check, which is how a test dies without anybody noticing. Each half of this
    /// gate counts what it saw, and this one guards the half that reads source — a renamed folder or
    /// a changed field convention would silently empty it.
    /// </summary>
    [Fact]
    public void TheControlsBeingCheckedAreRealOnes()
    {
        var found = GatedControls().ToList();

        Assert.True(
            found.Count >= 15,
            $"only {found.Count} gated controls were found in the source — the sweep has stopped "
            + "seeing the app, and an empty sweep passes every assertion above it");

        Assert.True(
            found.Select(f => f.Where).Distinct().Count() >= 6,
            "the sweep is finding gated controls in only one or two files, so it has stopped walking");

        // The two the gate exists for must be among them, whatever else changes.
        Assert.Contains(found, f => f is { Where: "HelpWindow.cs", Control: "_takeMeThere" });
        Assert.Contains(found, f => f is { Where: "TextStylesWindow.cs", Control: "_clearOverrides" });
    }

    // ---- plumbing ---------------------------------------------------------------------------------

    /// <summary>Four states the app is really in, chosen so no single predicate can agree by luck.</summary>
    private static IEnumerable<(string Situation, Action Arrange)> Situations(MainWindow window)
    {
        yield return ("no newsletter open", () => { });
        yield return ("a newsletter open, nothing selected", () =>
        {
            window.OpenSample();
            window.FramesForTest!.ClearSelection();
        });
        yield return ("a text frame selected", () =>
        {
            string blockId = window.FramesForTest!.AddTextFrame(0);
            window.FramesForTest.Select(blockId);
        });
        yield return ("typing in that frame", () =>
        {
            string blockId = window.FramesForTest!.AddTextFrame(0);
            window.EditorForTest!.TryBeginAt(
                0,
                window.SourceForTest!.GetEffectiveRect(blockId).X + 2f,
                window.SourceForTest.GetEffectiveRect(blockId).Y + 2f);
        });
    }

    /// <summary>
    /// Every menu item and toolbar button in the shell that carries an action id.
    ///
    /// <para>The action panel is deliberately not among them, and its absence is the M11 rule rather
    /// than an oversight: <b>nothing in the panel is ever greyed</b>. It hides what does not apply
    /// and draws what it offers pressable, carrying the refusal in words — so comparing its
    /// <c>IsEnabled</c> against the catalog would flag M11 working correctly, which is the one thing
    /// this gate must never do. The panel's own integrity is gate 24's
    /// (<c>Editing.Tests/ReachabilityTests</c>) and <see cref="ActionSurfaceTests"/>'.</para>
    ///
    /// <para><b>M76 adds a second surface in that same category, and it is excluded for the
    /// identical reason rather than a new one.</b> The page rail never greys either: a tile whose
    /// page cannot be jumped to, and a move button that cannot move, stay pressable and answer in
    /// words (docs/M76-spec.md §6). Left in the sweep it produced five "drawn enabled and the
    /// catalog says no" disagreements which were the rail obeying M11, not breaking it — the exact
    /// false positive the paragraph above exists to prevent. Its integrity is proved instead by
    /// <c>PageRailTests.NothingInTheRailIsEverGreyed</c>, which asserts the stronger thing this gate
    /// cannot: not merely that the refusal carries words, but that the words are the catalog's own
    /// sentences, and
    /// <c>PageRailTests.WithOnePageAllThreePageCommandsRefuseAndEachSentenceIsTrue</c>, which asserts
    /// each sentence is TRUE of the state that produced it.</para>
    /// </summary>
    private static IEnumerable<(string ActionId, Control Control)> TaggedControls(MainWindow window)
    {
        ActionPanel panel = window.PanelForTest;
        PageRail rail = window.RailForTest;

        return window.GetLogicalDescendants()
            .OfType<Control>()
            .Where(c => c is MenuItem or Button)
            .Where(c => !c.GetLogicalAncestors().Contains(panel))
            .Where(c => !c.GetLogicalAncestors().Contains(rail))
            .Where(c => c.Tag is string id && ActionCatalog.TryGet(id, out _))
            .Select(c => ((string)c.Tag!, c));
    }

    private sealed record GatedControl(string Where, int Line, string Control, string Property, string Expression);

    /// <summary>
    /// The source sweep: every assignment to <c>IsVisible</c> or <c>IsEnabled</c> on a field of a
    /// pressable type.
    ///
    /// <para>Its limits, stated so nobody reads more into a green run than is there. It sees fields
    /// only — a control held in a local or added straight to a panel is invisible to it. It matches
    /// on the declared type, so a <c>Control</c>-typed field holding a Button is missed. And it
    /// cannot read an expression's meaning at all; classification is a human act, recorded in
    /// <see cref="Classified"/>. What it does reliably is notice that a new one has appeared.</para>
    /// </summary>
    private static IEnumerable<GatedControl> GatedControls()
    {
        var field = new Regex(
            @"\b(?:private|internal|public|protected)\s+(?:(?:readonly|static)\s+)*"
            + @"(?:Button|MenuItem|ToggleButton|CheckBox)\??\s+(_\w+)");
        var assignment = new Regex(@"([\w\.]+)\.(IsVisible|IsEnabled)\s*=\s*([^;]+);");

        string appRoot = Path.Combine(RepoRoot(), "src", "TrestleBoard.App");

        foreach (string path in Directory
                     .EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            string source = File.ReadAllText(path);
            var pressable = field.Matches(source).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            if (pressable.Count == 0)
            {
                continue;
            }

            foreach (Match m in assignment.Matches(source))
            {
                string leaf = m.Groups[1].Value.Split('.').Last();
                if (!pressable.Contains(leaf))
                {
                    continue;
                }

                // A field initialiser or a reset to a constant states no precondition at all.
                string expression = m.Groups[3].Value.Trim();
                if (expression is "true" or "false")
                {
                    continue;
                }

                int line = source.Take(m.Index).Count(c => c == '\n') + 1;
                yield return new GatedControl(Path.GetFileName(path), line, leaf, m.Groups[2].Value, expression);
            }
        }
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "TrestleBoard.App", relativePath));

    private static string RepoRoot()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "PLAN.md")))
        {
            at = at.Parent;
        }

        Assert.True(at is not null, "could not find the repository root above the test binary");
        return at!.FullName;
    }
}
