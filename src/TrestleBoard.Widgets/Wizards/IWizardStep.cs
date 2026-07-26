namespace TrestleBoard.Widgets.Wizards;

public enum WizardStepKind
{
    Fields,
    RecordList,
    Text,
    Review,
}

public enum WizardListPagination
{
    /// <summary>Every row on one scrolling screen.</summary>
    AllRows,

    /// <summary>One row per screen — one question at a time (PLAN.md §6).</summary>
    OneRowPerScreen,
}

/// <summary>
/// The list-specific surface, non-generic so the session and the window can read pagination and
/// row chrome without knowing the row type (docs/M7-spec.md §6.3).
/// </summary>
public interface IWizardListStep
{
    /// <summary>Row headings when the list has a fixed shape; null for a free list.</summary>
    IReadOnlyList<string>? FixedRows { get; }

    WizardListPagination Pagination { get; }

    bool AllowReorder { get; }

    string AddButtonText { get; }

    string EmptyText { get; }
}

/// <summary>
/// A wizard screen, driven entirely through this untyped surface (docs/M7-spec.md §6.3). Each
/// concrete step performs exactly one cast back to its data type, inside itself.
/// </summary>
public interface IWizardStep
{
    WizardStepKind Kind { get; }

    /// <summary>The question or screen heading.</summary>
    string Title { get; }

    string? HelpText { get; }

    IReadOnlyList<WizardField> Fields { get; }

    /// <summary>False hides the step entirely — e.g. the events list when the mode is table-only.</summary>
    bool IsActive(object data);

    string GetValue(object data, string fieldKey, int rowIndex);

    void SetValue(object data, string fieldKey, int rowIndex, string value);

    int GetRowCount(object data);

    /// <summary>Row heading; "" for non-list steps.</summary>
    string GetRowLabel(object data, int rowIndex);

    /// <summary>Index of the new row, or -1 when the step forbids adding.</summary>
    int AddRow(object data);

    bool RemoveRow(object data, int rowIndex);

    bool MoveRow(object data, int fromIndex, int toIndex);

    /// <summary>Validates one row (or the whole step when rowIndex is -1).</summary>
    IReadOnlyList<WizardFieldError> Validate(object data, int rowIndex);

    /// <summary>Read-back lines for the review screen.</summary>
    IReadOnlyList<string> GetSummaryLines(object data);
}
