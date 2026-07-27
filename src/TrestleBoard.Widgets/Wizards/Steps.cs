using System.Globalization;

namespace TrestleBoard.Widgets.Wizards;

/// <summary>
/// Explicit accessor pair binding one field to the data POCO (docs/M7-spec.md §6.4). Lambdas rather
/// than reflection: a renamed property fails the build instead of the user's evening, it is
/// trim/AOT-safe, and one field can bind to two properties (a month/day pair) which no
/// property-name binder can express.
/// </summary>
public sealed record WizardFieldBinding<T>(string Key, Func<T, string> Get, Action<T, string> Set);

/// <summary>Shared plumbing: field lookup, validation, and the summary lines.</summary>
public abstract class WizardStepBase : IWizardStep
{
    protected WizardStepBase(string title, string? helpText, IReadOnlyList<WizardField> fields)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        HelpText = helpText;
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public abstract WizardStepKind Kind { get; }

    public string Title { get; }

    public string? HelpText { get; }

    public IReadOnlyList<WizardField> Fields { get; }

    public virtual bool IsActive(object data) => true;

    public abstract string GetValue(object data, string fieldKey, int rowIndex);

    public abstract void SetValue(object data, string fieldKey, int rowIndex, string value);

    public virtual int GetRowCount(object data) => 1;

    public virtual string GetRowLabel(object data, int rowIndex) => "";

    public virtual int AddRow(object data) => -1;

    public virtual bool RemoveRow(object data, int rowIndex) => false;

    public virtual bool MoveRow(object data, int fromIndex, int toIndex) => false;

    public abstract IReadOnlyList<WizardFieldError> Validate(object data, int rowIndex);

    public abstract IReadOnlyList<string> GetSummaryLines(object data);

    protected WizardField Field(string fieldKey) =>
        Fields.FirstOrDefault(f => string.Equals(f.Key, fieldKey, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"No field '{fieldKey}' on step '{Title}'.");
}

/// <summary>
/// A fixed set of single-value questions. At most three, and only when they are one thought —
/// otherwise one question per screen (PLAN.md §6).
/// </summary>
public sealed class FieldsStep<TData> : WizardStepBase
    where TData : class
{
    private readonly IReadOnlyList<(WizardField Field, WizardFieldBinding<TData> Binding)> _bound;
    private readonly Func<TData, bool>? _isActive;

    public FieldsStep(
        string title,
        string? helpText,
        IReadOnlyList<(WizardField Field, WizardFieldBinding<TData> Binding)> fields,
        Func<TData, bool>? isActive = null)
        : base(title, helpText, (fields ?? throw new ArgumentNullException(nameof(fields))).Select(f => f.Field).ToArray())
    {
        if (fields.Count is 0 or > 3)
        {
            throw new ArgumentException("A Fields step holds one to three questions.", nameof(fields));
        }

        _bound = fields;
        _isActive = isActive;
    }

    public override WizardStepKind Kind => WizardStepKind.Fields;

    public override bool IsActive(object data) => _isActive is null || _isActive((TData)data);

    public override string GetValue(object data, string fieldKey, int rowIndex) =>
        Binding(fieldKey).Get((TData)data) ?? "";

    public override void SetValue(object data, string fieldKey, int rowIndex, string value) =>
        Binding(fieldKey).Set((TData)data, value);

    public override IReadOnlyList<WizardFieldError> Validate(object data, int rowIndex)
    {
        var errors = new List<WizardFieldError>();
        foreach ((WizardField field, WizardFieldBinding<TData> binding) in _bound)
        {
            if (WizardValidators.Validate(field, binding.Get((TData)data) ?? "") is { } message)
            {
                errors.Add(new WizardFieldError(field.Key, -1, message));
            }
        }

        return errors;
    }

    public override IReadOnlyList<string> GetSummaryLines(object data)
    {
        var lines = new List<string>();
        foreach ((WizardField field, WizardFieldBinding<TData> binding) in _bound)
        {
            string value = binding.Get((TData)data) ?? "";
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{field.Label}: {(value.Length == 0 ? "—" : value)}"));
        }

        return lines;
    }

    private WizardFieldBinding<TData> Binding(string fieldKey) =>
        _bound.FirstOrDefault(b => string.Equals(b.Field.Key, fieldKey, StringComparison.Ordinal)).Binding
            ?? throw new KeyNotFoundException($"No field '{fieldKey}' on step '{Title}'.");
}

/// <summary>
/// A list of records — variable length, or fixed when <c>fixedRows</c> names the rows (the twelve
/// officer positions are "a list whose rows are pre-named", which is why this is one step kind and
/// not two).
/// </summary>
public sealed class RecordListStep<TData, TRow> : WizardStepBase, IWizardListStep
    where TData : class
    where TRow : class
{
    private readonly Func<TData, IList<TRow>> _getRows;
    private readonly Func<TRow> _createRow;
    private readonly IReadOnlyList<(WizardField Field, WizardFieldBinding<TRow> Binding)> _bound;
    private readonly Func<TRow, string>? _rowLabel;
    private readonly Func<TData, bool>? _isActive;
    private readonly Action<TData, TRow>? _onRemoveRow;

    public RecordListStep(
        string title,
        string? helpText,
        Func<TData, IList<TRow>> getRows,
        Func<TRow> createRow,
        IReadOnlyList<(WizardField Field, WizardFieldBinding<TRow> Binding)> fields,
        IReadOnlyList<string>? fixedRows = null,
        WizardListPagination pagination = WizardListPagination.AllRows,
        bool allowReorder = false,
        Func<TRow, string>? rowLabel = null,
        string addButtonText = "Add another",
        string emptyText = "Nothing here yet. Press “Add another” to start.",
        Func<TData, bool>? isActive = null,
        Action<TData, TRow>? onRemoveRow = null)
        : base(title, helpText, (fields ?? throw new ArgumentNullException(nameof(fields))).Select(f => f.Field).ToArray())
    {
        _getRows = getRows ?? throw new ArgumentNullException(nameof(getRows));
        _createRow = createRow ?? throw new ArgumentNullException(nameof(createRow));
        _bound = fields;
        _rowLabel = rowLabel;
        _isActive = isActive;
        _onRemoveRow = onRemoveRow;
        FixedRows = fixedRows;
        Pagination = pagination;
        AllowReorder = allowReorder && fixedRows is null;
        AddButtonText = addButtonText;
        EmptyText = emptyText;
    }

    public override WizardStepKind Kind => WizardStepKind.RecordList;

    /// <summary>Row headings when the list has a fixed shape; null for a free list.</summary>
    public IReadOnlyList<string>? FixedRows { get; }

    public WizardListPagination Pagination { get; }

    public bool AllowReorder { get; }

    public string AddButtonText { get; }

    public string EmptyText { get; }

    public override bool IsActive(object data) => _isActive is null || _isActive((TData)data);

    public override int GetRowCount(object data) => _getRows((TData)data).Count;

    public override string GetRowLabel(object data, int rowIndex)
    {
        IList<TRow> rows = _getRows((TData)data);
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            return "";
        }

        if (FixedRows is { } fixedRows && rowIndex < fixedRows.Count)
        {
            return fixedRows[rowIndex];
        }

        return _rowLabel?.Invoke(rows[rowIndex])
            ?? string.Create(CultureInfo.InvariantCulture, $"{Title} {rowIndex + 1}");
    }

    public override string GetValue(object data, string fieldKey, int rowIndex)
    {
        IList<TRow> rows = _getRows((TData)data);
        return rowIndex < 0 || rowIndex >= rows.Count ? "" : Binding(fieldKey).Get(rows[rowIndex]) ?? "";
    }

    public override void SetValue(object data, string fieldKey, int rowIndex, string value)
    {
        IList<TRow> rows = _getRows((TData)data);
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            return;
        }

        Binding(fieldKey).Set(rows[rowIndex], value);
    }

    public override int AddRow(object data)
    {
        if (FixedRows is not null)
        {
            return -1;
        }

        IList<TRow> rows = _getRows((TData)data);
        rows.Add(_createRow());
        return rows.Count - 1;
    }

    public override bool RemoveRow(object data, int rowIndex)
    {
        if (FixedRows is not null)
        {
            return false;
        }

        IList<TRow> rows = _getRows((TData)data);
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            return false;
        }

        // The one thing removal can be asked to remember. The birthday list uses it to record that
        // this brother was taken off deliberately, so the next sync does not put him straight back
        // (PLAN.md §11 M13). Both editors remove rows through here, so both are covered.
        _onRemoveRow?.Invoke((TData)data, rows[rowIndex]);
        rows.RemoveAt(rowIndex);
        return true;
    }

    public override bool MoveRow(object data, int fromIndex, int toIndex)
    {
        if (!AllowReorder)
        {
            return false;
        }

        IList<TRow> rows = _getRows((TData)data);
        if (fromIndex < 0 || fromIndex >= rows.Count || toIndex < 0 || toIndex >= rows.Count || fromIndex == toIndex)
        {
            return false;
        }

        TRow row = rows[fromIndex];
        rows.RemoveAt(fromIndex);
        rows.Insert(toIndex, row);
        return true;
    }

    public override IReadOnlyList<WizardFieldError> Validate(object data, int rowIndex)
    {
        IList<TRow> rows = _getRows((TData)data);
        var errors = new List<WizardFieldError>();
        int first = rowIndex < 0 ? 0 : rowIndex;
        int last = rowIndex < 0 ? rows.Count - 1 : rowIndex;
        for (int i = first; i <= last && i < rows.Count; i++)
        {
            foreach ((WizardField field, WizardFieldBinding<TRow> binding) in _bound)
            {
                if (WizardValidators.Validate(field, binding.Get(rows[i]) ?? "") is { } message)
                {
                    errors.Add(new WizardFieldError(field.Key, i, message));
                }
            }
        }

        return errors;
    }

    public override IReadOnlyList<string> GetSummaryLines(object data)
    {
        IList<TRow> rows = _getRows((TData)data);
        var lines = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            var values = new List<string>();
            foreach ((WizardField _, WizardFieldBinding<TRow> binding) in _bound)
            {
                string value = binding.Get(rows[i]) ?? "";
                if (value.Length > 0)
                {
                    values.Add(value);
                }
            }

            string label = GetRowLabel(data, i);
            string joined = values.Count == 0 ? "—" : string.Join(", ", values);
            lines.Add(label.Length == 0 ? joined : string.Create(CultureInfo.InvariantCulture, $"{label}: {joined}"));
        }

        if (lines.Count == 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{Title}: nothing yet"));
        }

        return lines;
    }

    private WizardFieldBinding<TRow> Binding(string fieldKey) =>
        _bound.FirstOrDefault(b => string.Equals(b.Field.Key, fieldKey, StringComparison.Ordinal)).Binding
            ?? throw new KeyNotFoundException($"No field '{fieldKey}' on step '{Title}'.");
}

/// <summary>One free-text passage on its own screen.</summary>
public sealed class TextStep<TData> : WizardStepBase
    where TData : class
{
    private readonly WizardField _field;
    private readonly WizardFieldBinding<TData> _binding;

    public TextStep(string title, string? helpText, WizardField field, WizardFieldBinding<TData> binding)
        : base(title, helpText, [field ?? throw new ArgumentNullException(nameof(field))])
    {
        if (field.Kind != WizardFieldKind.MultiLineText)
        {
            throw new ArgumentException("A Text step holds exactly one MultiLineText field.", nameof(field));
        }

        _field = field;
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
    }

    public override WizardStepKind Kind => WizardStepKind.Text;

    public override string GetValue(object data, string fieldKey, int rowIndex) => _binding.Get((TData)data) ?? "";

    public override void SetValue(object data, string fieldKey, int rowIndex, string value) =>
        _binding.Set((TData)data, value);

    public override IReadOnlyList<WizardFieldError> Validate(object data, int rowIndex) =>
        WizardValidators.Validate(_field, _binding.Get((TData)data) ?? "") is { } message
            ? [new WizardFieldError(_field.Key, -1, message)]
            : [];

    public override IReadOnlyList<string> GetSummaryLines(object data)
    {
        string value = (_binding.Get((TData)data) ?? "").ReplaceLineEndings(" ");
        if (value.Length > 80)
        {
            value = value[..77] + "…";
        }

        return [string.Create(CultureInfo.InvariantCulture, $"{_field.Label}: {(value.Length == 0 ? "—" : value)}")];
    }
}

/// <summary>The mandatory last screen: read it back, then "Save it".</summary>
public sealed class ReviewStep : WizardStepBase
{
    public ReviewStep(string title = "Check this over", string? helpText =
        "Read it through. Choose “Change this” beside anything that needs fixing.")
        : base(title, helpText, [])
    {
    }

    public override WizardStepKind Kind => WizardStepKind.Review;

    public override string GetValue(object data, string fieldKey, int rowIndex) => "";

    public override void SetValue(object data, string fieldKey, int rowIndex, string value)
    {
    }

    public override IReadOnlyList<WizardFieldError> Validate(object data, int rowIndex) => [];

    public override IReadOnlyList<string> GetSummaryLines(object data) => [];
}
