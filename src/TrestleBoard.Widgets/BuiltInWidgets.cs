using TrestleBoard.Widgets.Builtins.BirthdayList;
using TrestleBoard.Widgets.Builtins.CommitteeList;
using TrestleBoard.Widgets.Builtins.CoverBanner;
using TrestleBoard.Widgets.Builtins.DistrictCalendar;
using TrestleBoard.Widgets.Builtins.EventCard;
using TrestleBoard.Widgets.Builtins.OfficersTable;

namespace TrestleBoard.Widgets;

/// <summary>
/// The v1 widget set (PLAN.md §5). This list is the single registration point — widgets never
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
        new EventCardDefinition(),
        new CoverBannerDefinition(),
    ];
}
