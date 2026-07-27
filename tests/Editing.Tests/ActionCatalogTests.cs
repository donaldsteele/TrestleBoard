using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// The action catalog (PLAN.md §11 M11, §12 gate 8). The owner's complaint was controls greying
/// themselves out with nothing said about why; these tests are the machine-checked version of it.
/// Everything here runs without an Avalonia session, which is the whole reason the catalog lives in
/// this project rather than in the window's code-behind.
/// </summary>
public sealed class ActionCatalogTests
{
    /// <summary>Every situation the app can be in that changes what is possible.</summary>
    private static IEnumerable<ActionContext> EveryInterestingContext()
    {
        yield return ActionContext.Empty;
        yield return Document();
        yield return Typing();
        yield return Typing() with { HasTextSelection = true };
        yield return Photo();
        yield return Widget();
        yield return Widget() with { CanEditWidget = false };
        yield return Widget() with { WidgetHasListEditor = true };
        yield return TextFrame();
        yield return TextFrame() with { SelectionIsLinked = true, CanAutoFlow = true };
        yield return Document() with { Selection = SelectionKind.Shape, SelectedBlockId = "s1" };
        yield return Document() with { PageIndex = 2, PageCount = 3 };
        yield return Document() with { PageCount = 1 };
        yield return Document() with { CanUndo = true, CanRedo = true };
        yield return BirthdayList();
        yield return BirthdayList() with { RosterCount = 0, RosterBirthdaysThisMonth = 0 };
        yield return BirthdayList() with { RosterBirthdaysThisMonth = 0 };
        yield return Document() with { BirthdayListIsStale = true, RosterCount = 40, RosterBirthdaysThisMonth = 7 };
    }

    private static ActionContext Document() => new()
    {
        HasDocument = true,
        PageCount = 3,
        PageIndex = 0,
        CanStartFromLastMonth = true,
    };

    private static ActionContext Typing() => Document() with
    {
        Selection = SelectionKind.Text,
        IsEditingText = true,
        SelectionIsTextFrame = true,
        SelectedBlockId = "t1",
    };

    private static ActionContext TextFrame() => Document() with
    {
        Selection = SelectionKind.TextFrame,
        SelectionIsTextFrame = true,
        SelectedBlockId = "t1",
    };

    private static ActionContext Photo() => Document() with
    {
        Selection = SelectionKind.Photo,
        SelectedBlockId = "p1",
    };

    private static ActionContext Widget() => Document() with
    {
        Selection = SelectionKind.Widget,
        SelectedBlockId = "w1",
        WidgetTypeId = "officersTable",
        WidgetDisplayName = "Lodge officers",
        CanEditWidget = true,
    };

    private static ActionContext BirthdayList() => Widget() with
    {
        WidgetTypeId = "birthdayList",
        WidgetDisplayName = "Birthdays",
        RosterCount = 40,
        RosterBirthdaysThisMonth = 7,
    };

    // ---- bringing in birthdays (M13) -----------------------------------------------------------

    [Fact]
    public void BringingInBirthdaysIsAboutTheBirthdayListAndNothingElse()
    {
        Assert.True(ActionCatalog.Evaluate(ActionId.SyncBirthdays, BirthdayList()).IsAvailable);

        // Another widget is not "blocked" here — it is simply not what this action is about, so the
        // panel leaves it out entirely rather than greying it (PLAN.md §6).
        Assert.Equal(
            ActionAvailabilityKind.NotApplicable,
            ActionCatalog.Evaluate(ActionId.SyncBirthdays, Widget()).Kind);
        Assert.Equal(
            ActionAvailabilityKind.NotApplicable,
            ActionCatalog.Evaluate(ActionId.SyncBirthdays, Photo()).Kind);
    }

    [Fact]
    public void AStaleBirthdayListCanBeUpdatedFromTheWhatsNextCardWithNothingSelected()
    {
        // The card is the no-selection state, so the action has to be reachable there or the one
        // row that tells the user their list is out of date would lead nowhere.
        ActionContext nothingSelected = Document() with
        {
            BirthdayListIsStale = true,
            RosterCount = 40,
            RosterBirthdaysThisMonth = 7,
        };

        Assert.True(ActionCatalog.Evaluate(ActionId.SyncBirthdays, nothingSelected).IsAvailable);
        Assert.Contains(
            WhatsNext.Suggestions(nothingSelected),
            step => step.ActionId == ActionId.SyncBirthdays);
    }

    [Fact]
    public void AnEmptyAddressBookAndAQuietMonthAreDifferentRefusalsWithDifferentWaysOut()
    {
        ActionAvailability empty = ActionCatalog.Evaluate(
            ActionId.SyncBirthdays, BirthdayList() with { RosterCount = 0, RosterBirthdaysThisMonth = 0 });
        Assert.Equal(ActionAvailabilityKind.Blocked, empty.Kind);
        Assert.Equal(ActionId.ImportPeople, empty.RemedyId);

        ActionAvailability quiet = ActionCatalog.Evaluate(
            ActionId.SyncBirthdays, BirthdayList() with { RosterBirthdaysThisMonth = 0 });
        Assert.Equal(ActionAvailabilityKind.Blocked, quiet.Kind);
        Assert.Equal(ActionId.ShowPeople, quiet.RemedyId);
        Assert.NotEqual(empty.Reason, quiet.Reason);
    }

    [Fact]
    public void ABirthdayListFromANewerTrestleBoardIsRefusedBeforeTheAddressBookIsEvenConsulted()
    {
        ActionAvailability availability = ActionCatalog.Evaluate(
            ActionId.SyncBirthdays, BirthdayList() with { CanEditWidget = false });

        Assert.Equal(ActionAvailabilityKind.Blocked, availability.Kind);
        Assert.Contains("newer TrestleBoard", availability.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryActionHasAnAvailabilityRuleInEverySituation()
    {
        foreach (ActionContext context in EveryInterestingContext())
        {
            foreach (EditorAction action in ActionCatalog.All)
            {
                ActionAvailability availability = ActionCatalog.Evaluate(action.Id, context);
                Assert.NotNull(availability);
            }
        }
    }

    /// <summary>
    /// The complaint itself, as an assertion: nothing may become unavailable without saying why.
    /// This covers NotApplicable too, because the menu bar keeps conventional greying and puts the
    /// same sentence in its HelpText (PLAN.md §6).
    /// </summary>
    [Fact]
    public void NoUnavailableActionIsSilentAboutWhy()
    {
        var silent = new List<string>();
        foreach (ActionContext context in EveryInterestingContext())
        {
            foreach (EditorAction action in ActionCatalog.All)
            {
                ActionAvailability availability = ActionCatalog.Evaluate(action.Id, context);
                if (availability.Kind != ActionAvailabilityKind.Available
                    && string.IsNullOrWhiteSpace(availability.Reason))
                {
                    silent.Add(action.Id);
                }
            }
        }

        Assert.True(silent.Count == 0, "unavailable with no reason given: " + string.Join(", ", silent));
    }

    /// <summary>A reason is a sentence addressed to the user, not a status code.</summary>
    [Fact]
    public void EveryReasonReadsLikePlainLanguage()
    {
        var bad = new List<string>();
        foreach (ActionContext context in EveryInterestingContext())
        {
            foreach (EditorAction action in ActionCatalog.All)
            {
                string reason = ActionCatalog.Evaluate(action.Id, context).Reason;
                if (reason.Length == 0)
                {
                    continue;
                }

                if (!reason.EndsWith('.') || reason.Length < 12 || !char.IsUpper(reason[0]))
                {
                    bad.Add($"{action.Id}: \"{reason}\"");
                }
            }
        }

        Assert.True(bad.Count == 0, "reasons that do not read as sentences: " + string.Join("; ", bad));
    }

    [Fact]
    public void CannotConstructABlockedAvailabilityWithNothingToSay()
    {
        Assert.Throws<ArgumentException>(() => ActionAvailability.Blocked("  "));
        Assert.Throws<ArgumentException>(() => ActionAvailability.NotApplicable(""));
    }

    [Fact]
    public void ActionIdsAndGesturesAreUnique()
    {
        List<string> duplicateIds = ActionCatalog.All
            .GroupBy(a => a.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicateIds.Count == 0, "duplicate action ids: " + string.Join(", ", duplicateIds));

        List<string> duplicateGestures = ActionCatalog.All
            .Where(a => a.DisplayGesture is not null)
            .GroupBy(a => a.DisplayGesture!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} → {string.Join(" / ", g.Select(a => a.Id))}")
            .ToList();
        Assert.True(
            duplicateGestures.Count == 0,
            "two actions advertise the same shortcut: " + string.Join("; ", duplicateGestures));
    }

    /// <summary>
    /// The panel's rule: what does not apply is absent, never greyed. Absence reads as "not about
    /// photos" only because the panel is headed "A photo is selected".
    /// </summary>
    [Fact]
    public void ThePanelNeverOffersSomethingThatDoesNotApply()
    {
        foreach (ActionContext context in EveryInterestingContext())
        {
            foreach (ActionOffer offer in ActionCatalog.ForSelection(context))
            {
                Assert.NotEqual(ActionAvailabilityKind.NotApplicable, offer.Availability.Kind);
            }
        }
    }

    [Fact]
    public void ChoosingAPhotoOffersThePhotoActionsAndNotTheListOnes()
    {
        List<string> ids = ActionCatalog.ForSelection(Photo()).Select(o => o.Action.Id).ToList();

        Assert.Contains(ActionId.FixPhoto, ids);
        Assert.Contains(ActionId.AdjustPhoto, ids);
        Assert.Contains(ActionId.ToggleWrap, ids);
        Assert.Contains(ActionId.DeleteFrame, ids);
        Assert.DoesNotContain(ActionId.EditWidget, ids);
        Assert.DoesNotContain(ActionId.Bold, ids);
    }

    [Fact]
    public void ChoosingAWidgetOffersItsOwnActionsAndNotThePhotoOnes()
    {
        List<string> ids = ActionCatalog.ForSelection(Widget()).Select(o => o.Action.Id).ToList();

        Assert.Contains(ActionId.EditWidget, ids);
        Assert.Contains(ActionId.FitToContents, ids);
        Assert.DoesNotContain(ActionId.FixPhoto, ids);
    }

    [Fact]
    public void TypingOffersTheWordActionsAndTheClipboard()
    {
        List<string> ids = ActionCatalog.ForSelection(Typing()).Select(o => o.Action.Id).ToList();

        Assert.Contains(ActionId.Bold, ids);
        Assert.Contains(ActionId.Italic, ids);
        Assert.Contains(ActionId.Paste, ids);
        Assert.DoesNotContain(ActionId.FixPhoto, ids);
    }

    [Fact]
    public void CutIsOfferedWhileTypingButExplainsThatNothingIsHighlighted()
    {
        ActionAvailability availability = ActionCatalog.Evaluate(ActionId.Cut, Typing());

        Assert.Equal(ActionAvailabilityKind.Blocked, availability.Kind);
        Assert.Contains("highlighted", availability.Reason, StringComparison.Ordinal);
        Assert.Equal(ActionId.SelectAll, availability.RemedyId);

        Assert.True(ActionCatalog.Evaluate(ActionId.Cut, Typing() with { HasTextSelection = true }).IsAvailable);
    }

    [Fact]
    public void AWidgetFromANewerTrestleBoardExplainsItselfRatherThanGoingQuiet()
    {
        ActionAvailability availability =
            ActionCatalog.Evaluate(ActionId.EditWidget, Widget() with { CanEditWidget = false });

        Assert.Equal(ActionAvailabilityKind.Blocked, availability.Kind);
        Assert.Contains("newer TrestleBoard", availability.Reason, StringComparison.Ordinal);
        Assert.Equal(ActionId.CheckForUpdates, availability.RemedyId);
    }

    [Fact]
    public void UnlinkSaysWhyRatherThanJustGoingGrey()
    {
        ActionAvailability availability = ActionCatalog.Evaluate(ActionId.UnlinkFrames, TextFrame());

        Assert.Equal(ActionAvailabilityKind.Blocked, availability.Kind);
        Assert.Equal(ActionId.LinkFrames, availability.RemedyId);

        Assert.True(ActionCatalog
            .Evaluate(ActionId.UnlinkFrames, TextFrame() with { SelectionIsLinked = true })
            .IsAvailable);
    }

    [Fact]
    public void PageMovesExplainThemselvesAtBothEnds()
    {
        ActionContext first = Document() with { PageIndex = 0, PageCount = 3 };
        ActionContext last = Document() with { PageIndex = 2, PageCount = 3 };

        Assert.Equal("This is the first page.", ActionCatalog.Evaluate(ActionId.PreviousPage, first).Reason);
        Assert.Equal("This is the last page.", ActionCatalog.Evaluate(ActionId.NextPage, last).Reason);
        Assert.True(ActionCatalog.Evaluate(ActionId.NextPage, first).IsAvailable);
        Assert.Equal(
            "A newsletter needs at least one page.",
            ActionCatalog.Evaluate(ActionId.RemovePage, Document() with { PageCount = 1 }).Reason);
    }

    [Fact]
    public void WithNoNewsletterEverythingThatNeedsOneSaysSoAndPointsAtTheWayOut()
    {
        ActionAvailability availability = ActionCatalog.Evaluate(ActionId.ExportPdf, ActionContext.Empty);

        Assert.Equal(ActionAvailabilityKind.Blocked, availability.Kind);
        Assert.Equal(ActionId.NewFromTemplate, availability.RemedyId);
        Assert.True(ActionCatalog.Evaluate(ActionId.NewFromTemplate, ActionContext.Empty).IsAvailable);
    }

    [Fact]
    public void ThePanelHeadingSaysWhatIsSelected()
    {
        Assert.Equal("No newsletter is open", ActionCatalog.DescribeSelection(ActionContext.Empty));
        Assert.Equal("Nothing is selected", ActionCatalog.DescribeSelection(Document()));
        Assert.Equal("A photo is selected", ActionCatalog.DescribeSelection(Photo()));
        Assert.Equal("Lodge officers is selected", ActionCatalog.DescribeSelection(Widget()));
        Assert.Equal("You are typing", ActionCatalog.DescribeSelection(Typing()));
    }

    [Fact]
    public void WithNothingSelectedThePanelOffersWaysToPutSomethingOnThePage()
    {
        List<string> ids = ActionCatalog.ForSelection(Document()).Select(o => o.Action.Id).ToList();

        Assert.Contains(ActionId.InsertPhoto, ids);
        Assert.Contains(ActionId.InsertOfficers, ids);
        Assert.Contains(ActionId.AddTextFrame, ids);
    }

    /// <summary>
    /// <b>No panel action's title outgrows the panel</b> (PLAN.md §12 item 13).
    ///
    /// <para><b>This is a character count, and it is deliberately a proxy.</b> The real question is
    /// how wide the shaped text is at 16pt inside a 360px panel, and answering it properly would
    /// couple this project — which references the BCL and nothing else — to the font stack, the
    /// shaper and the App layer's panel metrics. A ceiling on characters is cheap, is checkable
    /// here, and fails in the same direction as the real constraint.</para>
    ///
    /// <para>The ceiling is two comfortable lines. About 37 characters of 16pt text fit across the
    /// panel once the padding, the glyph box and the button's own padding are taken out, so 44 is a
    /// title that wraps to a second line and stops — not one that runs to a third. Wrapping is not
    /// the failure this guards against: <b>the M15 fix that made panel buttons wrap was the right
    /// fix</b>, and M16 only moved the shortcut onto its own line so that wrapping became rare. What
    /// this guards against is a title nobody measured.</para>
    ///
    /// <para>The longest today is "Bring in birthdays from the address book" at 40, which names both
    /// ends of an operation whose whole difficulty is that people do not expect it to touch the
    /// address book. It is long on purpose.</para>
    /// </summary>
    [Fact]
    public void NoPanelTitleOutgrowsThePanel()
    {
        const int ceiling = 44;

        List<string> tooLong = ActionCatalog.All
            .Where(a => a.Title.Length > ceiling)
            .Select(a => $"{a.Id} ({a.Title.Length}): {a.Title}")
            .ToList();

        Assert.True(
            tooLong.Count == 0,
            $"titles longer than {ceiling} characters: " + string.Join("; ", tooLong));

        // Guard against the ceiling being raised until it stops meaning anything: if the longest
        // title in the catalog is nowhere near it, the check has quietly stopped checking.
        int longest = ActionCatalog.All.Max(a => a.Title.Length);
        Assert.True(longest > ceiling - 12, $"the longest title is only {longest} characters — is this ceiling still real?");
    }
}
