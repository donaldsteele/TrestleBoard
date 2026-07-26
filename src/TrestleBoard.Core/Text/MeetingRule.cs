using System.Globalization;

namespace TrestleBoard.Core.Text;

/// <summary>Which occurrence of a weekday inside a month ("1st", "last").</summary>
public enum WeekOrdinal
{
    First = 1,
    Second = 2,
    Third = 3,
    Fourth = 4,
    Last = 5,
}

/// <summary>
/// "1st Tuesday" — the stated-communication recurrence rule (docs/M7-spec.md §0.3). Lives in Core
/// rather than Widgets because M9's start-from-last-month recomputes dates from it and must not
/// reference the widget layer. Invariant culture throughout; never throws.
/// </summary>
public readonly record struct MeetingRule(WeekOrdinal Ordinal, DayOfWeek Day)
{
    private static readonly (string Text, WeekOrdinal Value)[] Ordinals =
    [
        ("1st", WeekOrdinal.First),
        ("first", WeekOrdinal.First),
        ("2nd", WeekOrdinal.Second),
        ("second", WeekOrdinal.Second),
        ("3rd", WeekOrdinal.Third),
        ("third", WeekOrdinal.Third),
        ("4th", WeekOrdinal.Fourth),
        ("fourth", WeekOrdinal.Fourth),
        ("last", WeekOrdinal.Last),
    ];

    private static readonly (string Text, DayOfWeek Value)[] Days =
    [
        ("sunday", DayOfWeek.Sunday),
        ("monday", DayOfWeek.Monday),
        ("tuesday", DayOfWeek.Tuesday),
        ("wednesday", DayOfWeek.Wednesday),
        ("thursday", DayOfWeek.Thursday),
        ("friday", DayOfWeek.Friday),
        ("saturday", DayOfWeek.Saturday),
    ];

    /// <summary>Parses "1st Tuesday", "First Tuesday", "last friday". False for anything else.</summary>
    public static bool TryParse(string? text, out MeetingRule rule)
    {
        rule = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        WeekOrdinal? ordinal = null;
        foreach ((string candidate, WeekOrdinal value) in Ordinals)
        {
            if (string.Equals(parts[0], candidate, StringComparison.OrdinalIgnoreCase))
            {
                ordinal = value;
                break;
            }
        }

        if (ordinal is null)
        {
            return false;
        }

        foreach ((string candidate, DayOfWeek value) in Days)
        {
            if (string.Equals(parts[1], candidate, StringComparison.OrdinalIgnoreCase))
            {
                rule = new MeetingRule(ordinal.Value, value);
                return true;
            }
        }

        return false;
    }

    /// <summary>Canonical form, e.g. "1st Tuesday". Invariant — never localised.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{OrdinalText(Ordinal)} {DayText(Day)}");

    /// <summary>The concrete date this rule lands on in a given month (M9's date bumping).</summary>
    public DateOnly ResolveDate(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);

        if (Ordinal == WeekOrdinal.Last)
        {
            int lastDay = DateTime.DaysInMonth(year, month);
            var last = new DateOnly(year, month, lastDay);
            int back = ((int)last.DayOfWeek - (int)Day + 7) % 7;
            return last.AddDays(-back);
        }

        var first = new DateOnly(year, month, 1);
        int forward = ((int)Day - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(forward + (7 * ((int)Ordinal - 1)));
    }

    private static string OrdinalText(WeekOrdinal ordinal) => ordinal switch
    {
        WeekOrdinal.First => "1st",
        WeekOrdinal.Second => "2nd",
        WeekOrdinal.Third => "3rd",
        WeekOrdinal.Fourth => "4th",
        _ => "Last",
    };

    private static string DayText(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => "Sunday",
        DayOfWeek.Monday => "Monday",
        DayOfWeek.Tuesday => "Tuesday",
        DayOfWeek.Wednesday => "Wednesday",
        DayOfWeek.Thursday => "Thursday",
        DayOfWeek.Friday => "Friday",
        _ => "Saturday",
    };
}
