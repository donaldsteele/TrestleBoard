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

    /// <summary>
    /// A person's name (M13). Offered from the lodge address book, but <b>free typing is always
    /// allowed</b> — a brother who is not in the book must still be typeable, or the wizard has a
    /// dead end in it. The candidate names are handed to the window by the shell; nothing here
    /// reaches for a roster (PLAN.md §5).
    /// </summary>
    Person,
}

public sealed record WizardChoice(string Value, string Label);

/// <summary>Returns null when the value is fine, otherwise the sentence shown to the user.</summary>
public delegate string? WizardFieldValidator(string value);

/// <summary>
/// One question. The label is the question in plain language; help text is one calm sentence under
/// the box; the example becomes the watermark AND part of the error message — never a replacement
/// for the label (PLAN.md §6).
///
/// <c>AllowsPeoplePicker</c> (M13) puts an "Add someone from the address book…" button beside a
/// several-line field, which is how committees stop being retyped without their members list
/// changing shape at all: it stays a list of strings, so there is no migration and no layouter
/// change to get wrong.
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
    WizardFieldValidator? Validator = null,
    bool AllowsPeoplePicker = false);

public readonly record struct WizardFieldError(string FieldKey, int RowIndex, string Message);

/// <summary>One line of the review screen, and the screen "Change this" should go back to.</summary>
public readonly record struct WizardReviewLine(string Text, int ScreenIndex);
