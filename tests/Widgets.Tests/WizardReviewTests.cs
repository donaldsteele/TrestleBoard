using TrestleBoard.Widgets.Builtins.DistrictCalendar;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// The review screen and the guards around choice fields (docs/M7-spec.md §6.2/§6.6). Both were
/// M7 review findings: a review line that always sent the user back to screen 0, and a choice field
/// with no validation rule behind it.
/// </summary>
public sealed class WizardReviewTests
{
    [Fact]
    public void EachReviewLineNavigatesToTheAnswerItShows()
    {
        var definition = new OfficersTableDefinition();
        WizardSession session = WizardSession.Create(definition, null, 1, WidgetTestData.Seed);

        session.TryGoNext();
        for (int i = 0; i < 12; i++)
        {
            session.SetValue("name", WidgetTestData.Names[i]);
            session.TryGoNext();
        }

        IReadOnlyList<WizardReviewLine> lines = session.ReviewLines;

        // One heading line plus twelve office lines.
        Assert.Equal(13, lines.Count);
        Assert.Equal(0, lines[0].ScreenIndex);

        // "Change this" beside the seventh officer must land on the SEVENTH officer's screen, not
        // back at the beginning — the whole point of a review step.
        for (int row = 0; row < 12; row++)
        {
            WizardReviewLine line = lines[row + 1];
            Assert.True(session.TryGoTo(line.ScreenIndex));
            Assert.Equal(OfficersTableData.StandardPositions[row], session.ScreenTitle);
            Assert.Equal(WidgetTestData.Names[row], session.GetValue("name"));
        }
    }

    [Fact]
    public void ShowingAllRowsCollapsesEveryReviewLineOntoTheOneScreen()
    {
        var definition = new OfficersTableDefinition();
        WizardSession session = WizardSession.Create(definition, null, 1, WidgetTestData.Seed);
        session.TryGoNext();
        session.ShowAllRows();

        IReadOnlyList<WizardReviewLine> lines = session.ReviewLines;
        int listScreen = lines[1].ScreenIndex;

        Assert.All(lines.Skip(1), l => Assert.Equal(listScreen, l.ScreenIndex));
        Assert.True(session.TryGoTo(listScreen));
        Assert.Equal(12, session.RowCount);
    }

    [Fact]
    public void AChoiceOutsideTheOfferedValuesIsRefused()
    {
        var definition = new DistrictCalendarDefinition();
        WizardSession session = WizardSession.Create(definition, null, 1, WidgetTestData.Seed);

        session.SetValue("mode", "both");
        Assert.Equal("both", session.GetValue("mode"));

        // A value that is not on the list LEAVES the answer alone. Both editors render a picker, so
        // this should be unreachable — but quietly resetting the user's choice to a default would be
        // the worst way to handle it if one ever arrived.
        session.SetValue("mode", "whatever the user typed");
        Assert.Equal("both", session.GetValue("mode"));
        Assert.True(session.TryGoNext());
    }

    [Fact]
    public void ChoiceValidationAcceptsEveryOfferedValue()
    {
        WizardField mode = new DistrictCalendarDefinition().Wizard.Steps[0].Fields
            .First(f => f.Kind == WizardFieldKind.Choice);

        Assert.NotNull(mode.Choices);
        Assert.All(mode.Choices!, c => Assert.Null(WizardValidators.Choice(mode, c.Value)));
        Assert.NotNull(WizardValidators.Choice(mode, "not-a-mode"));
    }
}
