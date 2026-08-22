using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using TrestleBoard.App.Actions;
using TrestleBoard.App.Theme;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// **One primary per panel group** (PLAN.md §11 M76 (f), docs/M76-spec.md §3, §9).
///
/// <para>M16 defined the primary treatment — accent fill, taller minimum, gold left bar — as "the
/// offer you probably came for", and then left every action to assert it for itself. Fifteen did.
/// <c>docs/images/action-panel-photo.png</c> is the result: three navy-and-gold slabs stacked in
/// one panel. Three simultaneous offers are not three answers to that question, they are the
/// absence of an answer, and the gold bar is the most expensive ornament in the palette — the only
/// place the lodge's own gold is legal — spent on all of them at once.</para>
///
/// <para><b>Why this is its own file rather than an addition to <see cref="OfferIntegrityTests"/>
/// or <see cref="ActionSurfaceTests"/>.</b> Gate 26 is about <i>agreement</i> between what a
/// control offers and what its handler requires, and it says at length that it is never about
/// availability; nothing here is about availability either, but nothing here is about agreement
/// with a handler. <see cref="ActionSurfaceTests"/> is M11's acceptance criteria, which are about
/// greying and about silence. This is a third question: how many things one panel is allowed to
/// shout at once.</para>
///
/// <para><b>Three strengths of check, labelled rather than blended</b>, in the manner gate 26
/// established:</para>
/// <list type="number">
///   <item><b>Proved for every panel that could ever exist, by exhaustion.</b>
///   <see cref="ActionCatalog.PrimaryOffers"/> is a pure function of a set of offers, and only the
///   ranked actions of a group can affect its answer — so enumerating <i>every subset of every
///   group's ranked actions, in every combination of available and blocked</i> covers every panel
///   any context could produce and a great many it could not. No <see cref="ActionContext"/> can
///   reach outside that space, because <c>ForSelection</c> only ever drops an offer from a group;
///   it never invents one and never reorders the groups' contents.</item>
///   <item><b>Proved impossible to tie.</b> The catalog refuses to initialise if two actions in one
///   group claim the same rank, so a winner can never come down to enumeration order.</item>
///   <item><b>Proved on the real window.</b> The panel is driven headlessly through every kind of
///   selection and the buttons it actually drew are counted — because a rule the renderer does not
///   consult is not a rule.</item>
/// </list>
/// </summary>
public sealed class PrimaryEmphasisTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    // ---- 1. every panel that could ever exist -------------------------------------------------

    /// <summary>
    /// <b>The gate, over every offer set the catalog can produce.</b> Not four situations and not
    /// the six selection kinds: every subset of every group's ranked actions, each one both
    /// available and blocked, which is a strict superset of the panels any context could ask for.
    /// The group's unranked actions ride along in every case, because they are what a real panel is
    /// mostly made of and a resolver that mistook one for a candidate has to fail here.
    ///
    /// <para>Order-independence is asserted in the same breath, by handing the same offers over
    /// backwards. "Whichever one happened to be enumerated first" is precisely the non-rule this
    /// milestone replaced, and a test that only ever passes them in declaration order could not
    /// tell the difference.</para>
    /// </summary>
    [Fact]
    public void NoSetOfOffersTheCatalogCanProduceEverHasTwoPrimariesInAGroup()
    {
        int setsChecked = 0;
        int setsWithAWinner = 0;

        foreach (ActionGroup group in Enum.GetValues<ActionGroup>())
        {
            EditorAction[] ranked = ActionCatalog.All
                .Where(a => a.Group == group && a.PrimaryRank > 0)
                .ToArray();
            EditorAction[] unranked = ActionCatalog.All
                .Where(a => a.Group == group && a.PrimaryRank == 0)
                .ToArray();

            // Every subset of the ranked actions (the presence axis) crossed with every
            // available/blocked assignment over them (the availability axis).
            for (int present = 0; present < 1 << ranked.Length; present++)
            {
                for (int blocked = 0; blocked < 1 << ranked.Length; blocked++)
                {
                    var offers = new List<ActionOffer>();
                    for (int i = 0; i < ranked.Length; i++)
                    {
                        if ((present & (1 << i)) == 0)
                        {
                            continue;
                        }

                        offers.Add(new ActionOffer(
                            ranked[i],
                            (blocked & (1 << i)) == 0
                                ? ActionAvailability.Available
                                : ActionAvailability.Blocked("Not just now, and this says why.")));
                    }

                    offers.AddRange(unranked.Select(a => new ActionOffer(a, ActionAvailability.Available)));

                    IReadOnlySet<string> winners = ActionCatalog.PrimaryOffers(offers);
                    setsChecked++;

                    Assert.True(
                        winners.Count <= 1,
                        $"the {group} group would draw {winners.Count} primaries at once: "
                        + string.Join(", ", winners));

                    if (winners.Count == 1)
                    {
                        setsWithAWinner++;

                        // The winner is the lowest rank present — not the first one enumerated,
                        // and the same one whichever end the set is read from.
                        string expected = offers
                            .Where(o => o.Action.PrimaryRank > 0)
                            .OrderBy(o => o.Action.PrimaryRank)
                            .First().Action.Id;
                        Assert.Equal(expected, winners.Single());

                        offers.Reverse();
                        Assert.Equal(expected, Assert.Single(ActionCatalog.PrimaryOffers(offers)));
                    }
                    else
                    {
                        // No winner at all is allowed only when nothing in the set claimed a rank.
                        Assert.DoesNotContain(offers, o => o.Action.PrimaryRank > 0);
                    }
                }
            }
        }

        // Guards against the sweep quietly checking nothing, which is how an audit stays green
        // while auditing air.
        Assert.True(setsChecked > 100, $"only {setsChecked} offer sets were tried");
        Assert.True(setsWithAWinner > 20, $"only {setsWithAWinner} sets produced a primary at all");
    }

    /// <summary>
    /// <b>Ties are impossible, not broken.</b> Two actions in one group may not claim the same
    /// non-zero rank — <see cref="ActionCatalog"/>'s static constructor throws if they do, in the
    /// spirit of <c>ActionAvailability</c> refusing an empty reason at construction. It is asserted
    /// here as well as enforced there, so the rule a reader is relying on is written down in the
    /// place they will go looking for it.
    /// </summary>
    [Fact]
    public void NoTwoActionsInOneGroupClaimTheSameRank()
    {
        foreach (IGrouping<ActionGroup, EditorAction> group in ActionCatalog.All
                     .Where(a => a.PrimaryRank > 0)
                     .GroupBy(a => a.Group))
        {
            List<string> clashes = group
                .GroupBy(a => a.PrimaryRank)
                .Where(byRank => byRank.Count() > 1)
                .Select(byRank => $"rank {byRank.Key}: " + string.Join(" and ", byRank.Select(a => a.Id)))
                .ToList();

            Assert.True(
                clashes.Count == 0,
                $"two actions in the {group.Key} group claim one rank, so which of them gets the "
                + "gold would come down to declaration order: " + string.Join("; ", clashes));
        }

        Assert.All(ActionCatalog.All, a => Assert.True(
            a.PrimaryRank >= 0,
            $"{a.Id} declares a negative rank; zero already means no claim"));

        // The change is not merely representational: some group must really have had to choose,
        // or the rank is a boolean wearing a hat and the sweep above proves nothing.
        Assert.Contains(
            ActionCatalog.All.Where(a => a.PrimaryRank > 0).GroupBy(a => a.Group),
            g => g.Count() > 1);
    }

    // ---- 2. the real window -------------------------------------------------------------------

    /// <summary>
    /// <b>The renderer consults the rule.</b> Every kind of selection in turn, and after each one
    /// the panel's own buttons counted by the treatment they carry. A rule the panel never asked
    /// about would sail through both tests above.
    ///
    /// <para>The same walk asserts the other half of M76 (f), which is the half that could do real
    /// damage: <b>an action that loses its group is still drawn with the ordinary action
    /// treatment</b>. Losing the gold is not becoming unstyled and it is not looking disabled —
    /// nothing in this panel is ever greyed (M11). <see cref="ActionSurfaceTests"/> proves that for
    /// every window in the app; it is repeated here on the buttons this change actually demotes.</para>
    /// </summary>
    [Fact]
    public async Task ThePanelNeverDrawsTwoPrimariesInOneGroup()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenIssueSample();

            object? primaryTheme =
                window.TryFindResource(Tokens.PrimaryButtonTheme, out object? found) ? found : null;
            Assert.NotNull(primaryTheme);

            var tooMany = new List<string>();
            var bare = new List<string>();
            int primariesSeen = 0;
            int situations = 0;

            foreach ((string Situation, Action Arrange) state in Situations(window))
            {
                state.Arrange();
                window.RefreshActions();
                Dispatcher.UIThread.RunJobs();
                situations++;

                var byGroup = new Dictionary<ActionGroup, List<string>>();

                foreach (Button button in window.PanelForTest.ButtonsForTest)
                {
                    if (button.Tag is not string actionId
                        || !ActionCatalog.TryGet(actionId, out EditorAction? action))
                    {
                        continue;
                    }

                    if (ReferenceEquals(button.Theme, primaryTheme))
                    {
                        primariesSeen++;
                        if (!byGroup.TryGetValue(action.Group, out List<string>? held))
                        {
                            byGroup[action.Group] = held = [];
                        }

                        held.Add(ActionPanel.LabelOf(button));
                    }
                    else if (!button.Classes.Contains("action"))
                    {
                        // A demoted offer must land on Tokens.Action(), never on nothing at all.
                        bare.Add($"{state.Situation}: {ActionPanel.LabelOf(button)}");
                    }
                }

                tooMany.AddRange(byGroup
                    .Where(g => g.Value.Count > 1)
                    .Select(g => $"{state.Situation}: {ActionCatalog.DescribeGroup(g.Key)} drew "
                        + $"{g.Value.Count} primaries — {string.Join(", ", g.Value)}"));
            }

            Assert.True(situations > 5, $"only {situations} situations were tried");
            Assert.True(primariesSeen > 0, "no primary button was drawn at all, so nothing was checked");
            Assert.True(
                tooMany.Count == 0,
                "more than one offer claimed to be the one the user came for: "
                + string.Join(" | ", tooMany));
            Assert.True(
                bare.Count == 0,
                "these panel buttons carry neither treatment, so losing the accent made them look "
                + "dead: " + string.Join(", ", bare));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Every kind of selection the panel has a group for, plus the two states that have no
    /// selection at all: nothing chosen (the Insert group, where two actions used to claim the gold
    /// together) and typing (the Text group, where three did).
    /// </summary>
    private static IEnumerable<(string Situation, Action Arrange)> Situations(MainWindow window)
    {
        yield return ("nothing selected", () => window.FramesForTest!.ClearSelection());

        foreach (Core.Model.Block block in window.SessionForTest!.Document.Pages[0].Blocks.ToList())
        {
            Core.Model.Block chosen = block;
            yield return ($"{chosen.GetType().Name} selected", () => window.FramesForTest!.Select(chosen.Id));
        }

        foreach (string typeId in new[]
                 {
                     "officersTable", "birthdayList", "committeeList",
                     "districtCalendar", "eventCard", "coverBanner",
                 })
        {
            string kind = typeId;
            yield return ($"{kind} selected", () =>
            {
                string id = window.WidgetsForTest!.InsertWidget(0, kind);
                window.FramesForTest!.Select(id);
            });
        }

        yield return ("typing in a text frame", () =>
        {
            string blockId = window.FramesForTest!.AddTextFrame(0);
            window.EditorForTest!.TryBeginAt(
                0,
                window.SourceForTest!.GetEffectiveRect(blockId).X + 2f,
                window.SourceForTest.GetEffectiveRect(blockId).Y + 2f);
        });
    }
}
