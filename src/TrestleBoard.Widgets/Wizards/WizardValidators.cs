using System.Globalization;

namespace TrestleBoard.Widgets.Wizards;

/// <summary>
/// Plain-language validation (docs/M7-spec.md §6.2). Every message is a complete sentence, names the
/// thing in the user's own words, and says what to do. The words "invalid", "error", "required
/// field" and "malformed" appear nowhere a user can see them.
/// </summary>
public static class WizardValidators
{
    public static string Required(string label) =>
        string.Create(CultureInfo.InvariantCulture, $"Please fill in {label.ToLowerInvariant()}.");

    public static string? Phone(string value)
    {
        int digits = 0;
        foreach (char c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                digits++;
            }
            else if (c is not ('-' or '.' or ' ' or '(' or ')' or '+'))
            {
                return PhoneMessage;
            }
        }

        return digits is >= 7 and <= 15 ? null : PhoneMessage;
    }

    public static string? MonthDay(string value)
    {
        if (!TryParseMonthDay(value, out _, out _))
        {
            return "Please type the month and day like 1/1 or 12/25.";
        }

        return null;
    }

    public static string? DayOfMonthRule(string value) =>
        Core.Text.MeetingRule.TryParse(value, out _)
            ? null
            : "Please type something like 1st Tuesday.";

    public static string? Time(string value)
    {
        string[] parts = value.Split(':');
        if (parts.Length != 2)
        {
            return TimeMessage;
        }

        string minutes = parts[1].Trim();
        int space = minutes.IndexOf(' ', StringComparison.Ordinal);
        if (space >= 0)
        {
            // "6:30 pm" is fine; only the numbers are checked.
            minutes = minutes[..space];
        }

        if (!int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int hour)
            || !int.TryParse(minutes, NumberStyles.None, CultureInfo.InvariantCulture, out int minute)
            || hour is < 0 or > 23
            || minute is < 0 or > 59
            || minutes.Length != 2)
        {
            return TimeMessage;
        }

        return null;
    }

    /// <summary>
    /// A choice must be one of the offered values. Without this a widget would silently reinterpret
    /// anything else — which is exactly what an unguarded free-text field on a Choice would allow.
    /// </summary>
    public static string? Choice(WizardField field, string value)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Choices is not { Count: > 0 } choices)
        {
            return null;
        }

        foreach (WizardChoice choice in choices)
        {
            if (string.Equals(choice.Value, value, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Please pick one of: {string.Join(", ", choices.Select(c => c.Label))}.");
    }

    public static string TooLong(string label, int max) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{label} is too long. Please keep it under {max} letters.");

    /// <summary>Shared by the MonthDay validator and the BirthdayList binding.</summary>
    public static bool TryParseMonthDay(string? value, out int month, out int day)
    {
        month = 0;
        day = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Split(['/', '-'], StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out month)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out day)
            || month is < 1 or > 12
            || day is < 1 or > 31)
        {
            month = 0;
            day = 0;
            return false;
        }

        return true;
    }

    /// <summary>Canonical "M/D", no leading zeros, invariant culture.</summary>
    public static string FormatMonthDay(int month, int day) =>
        month <= 0 || day <= 0
            ? ""
            : string.Create(CultureInfo.InvariantCulture, $"{month}/{day}");

    private const string PhoneMessage = "Phone numbers look like 555-0100. Please check this one.";

    private const string TimeMessage = "Please type a time like 6:30.";

    /// <summary>Runs the built-in rule for a kind, then the field's own validator (§6.2).</summary>
    internal static string? Validate(WizardField field, string rawValue)
    {
        string value = rawValue.Trim();
        if (value.Length == 0)
        {
            return field.IsOptional ? null : Required(field.Label);
        }

        if (value.Length > field.MaxLength)
        {
            return TooLong(field.Label, field.MaxLength);
        }

        string? builtIn = field.Kind switch
        {
            WizardFieldKind.Phone => Phone(value),
            WizardFieldKind.MonthDay => MonthDay(value),
            WizardFieldKind.DayOfMonthRule => DayOfMonthRule(value),
            WizardFieldKind.Time => Time(value),
            WizardFieldKind.Choice => Choice(field, value),
            _ => null,
        };

        return builtIn ?? field.Validator?.Invoke(value);
    }
}
