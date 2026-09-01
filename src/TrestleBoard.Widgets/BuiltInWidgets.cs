using TrestleBoard.Widgets.Builtins.BirthdayList;
using TrestleBoard.Widgets.Builtins.CommitteeList;
using TrestleBoard.Widgets.Builtins.CoverBanner;
using TrestleBoard.Widgets.Builtins.DistrictCalendar;
using TrestleBoard.Widgets.Builtins.EventCard;
using TrestleBoard.Widgets.Builtins.MonthCalendar;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Builtins.SimpleList;

namespace TrestleBoard.Widgets;

/// <summary>
/// The widget set (PLAN.md §5; the seventh arrived at M60). This list is the single registration point — widgets never
/// register themselves — which is what lets six of them be built in parallel without a shared file
/// changing under anyone (docs/M7-spec.md §12.1). Order is menu and gallery order.
/// </summary>
public static class BuiltInWidgets
{
    public static IReadOnlyList<IWidgetDefinition> All { get; } =
    [
        new OfficersTableDefinition(),
        new BirthdayListDefinition(),
        new CommitteeListDefinition(),
        new DistrictCalendarDefinition(),

        // M84: the eighth. Beside the district calendar because they are the two calendars, and
        // after it because the district one has been there since M7 — this is the month-at-a-glance
        // view the district table cannot give.
        new MonthCalendarDefinition(),
        new EventCardDefinition(),
        new CoverBannerDefinition(),

        // M60: the seventh, and the only one that does not know what it is for. Last in the list
        // because it is the one to reach for when none of the six above fits.
        new SimpleListDefinition(),
    ];
}
