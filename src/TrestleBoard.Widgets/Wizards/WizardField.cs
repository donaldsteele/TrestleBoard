namespace TrestleBoard.Widgets.Wizards;

/// <summary>What kind of answer a question expects (docs/M7-spec.md §6.1).</summary>
public enum WizardFieldKind
{
    /// <summary>One line of free text.</summary>
    Text,

    /// <summary>Several lines; Enter inserts a newline instead of moving on.</summary>
    MultiLineText,

    /// <summary>A phone number, e.g. "555-0100". Stored exactly as typed.</summary>
    Phone,

    /// <summary>Month and day only, e.g. "1/1" — never a year, never an age.</summary>
    MonthDay,

    /// <summary>A recurrence rule, e.g. "1st Tuesday" (Core.Text.MeetingRule).</summary>
    DayOfMonthRule,

    /// <summary>A clock time, e.g. "6:30".</summary>
    Time,

    /// <summary>One of a fixed list.</summary>
    Choice,
}

public sealed record WizardChoice(string Value, string Label);

/// <summary>Returns null when the value is fine, otherwise the sentence shown to the user.</summary>
public delegate string? WizardFieldValidator(string value);

/// <summary>
/// One question. The label is the question in plain language; help text is one calm sentence under
/// the box; the example becomes the watermark AND part of the error message — never a replacement
/// for the label (PLAN.md §6).
/// </summary>
public sealed record WizardField(
    string Key,
    string Label,
    WizardFieldKind Kind = WizardFieldKind.Text,
    bool IsOptional = false,
    string? HelpText = null,
    string? ExampleText = null,
    IReadOnlyList<WizardChoice>? Choices = null,
    int MaxLength = 200,
    WizardFieldValidator? Validator = null);

public readonly record struct WizardFieldError(string FieldKey, int RowIndex, string Message);
