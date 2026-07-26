using System.Globalization;
using System.Text.Json;
using TrestleBoard.Layout.Widgets;

namespace TrestleBoard.Widgets.Wizards;

/// <summary>
/// Drives a wizard with no window attached (docs/M7-spec.md §6.5). The window is a renderer over
/// this object and holds no wizard state, so every navigation, validation and commit rule is
/// unit-testable headlessly — and the grid re-editor is a second view over the same session.
/// </summary>
public sealed class WizardSession
{
    private readonly List<Screen> _screens = [];
    private readonly HashSet<int> _showAllRows = [];
    private readonly object _data;
    private List<WizardFieldError> _errors = [];
    private int _screenIndex;

    private WizardSession(IWidgetDefinition definition, object data)
    {
        Definition = definition;
        _data = data;
        RebuildScreens();
    }

    /// <summary>
    /// Existing data (decoded and migrated) pre-fills the wizard — this IS the re-edit path (§7.1).
    /// Unreadable or absent data starts from the widget's empty shape.
    /// </summary>
    public static WizardSession Create(
        IWidgetDefinition definition,
        JsonElement? existingData,
        int dataVersion,
        WidgetSeed seed)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(seed);
        object data = definition.TryReadData(existingData, dataVersion, out object typed)
            ? typed
            : definition.CreateEmptyData(seed);
        return new WizardSession(definition, data);
    }

    public IWidgetDefinition Definition { get; }

    public string Title => Definition.Wizard.Title;

    public string IntroText => Definition.Wizard.IntroText;

    // ---- navigation ----------------------------------------------------------------------

    public int ScreenCount => _screens.Count;

    public int ScreenIndex => _screenIndex;

    public IWizardStep CurrentStep => Definition.Wizard.Steps[_screens[_screenIndex].StepIndex];

    /// <summary>-1 unless this screen shows exactly one row of a paginated list.</summary>
    public int CurrentRowIndex => _screens[_screenIndex].RowIndex;

    public string ScreenTitle
    {
        get
        {
            Screen screen = _screens[_screenIndex];
            IWizardStep step = Definition.Wizard.Steps[screen.StepIndex];
            if (screen.RowIndex < 0)
            {
                return step.Title;
            }

            string label = step.GetRowLabel(_data, screen.RowIndex);
            return label.Length == 0 ? step.Title : label;
        }
    }

    public bool IsFirstScreen => _screenIndex == 0;

    public bool IsReviewScreen => CurrentStep.Kind == WizardStepKind.Review;

    public string ProgressText =>
        string.Create(CultureInfo.InvariantCulture, $"Step {_screenIndex + 1} of {_screens.Count}");

    /// <summary>Validates only the CURRENT screen — a user is never blocked by a question ahead.</summary>
    public bool TryGoNext()
    {
        _errors = [.. ValidateScreen(_screenIndex)];
        if (_errors.Count > 0)
        {
            return false;
        }

        RebuildScreens();
        if (_screenIndex + 1 < _screens.Count)
        {
            _screenIndex++;
        }

        return true;
    }

    public bool TryGoTo(int screenIndex)
    {
        if (screenIndex < 0 || screenIndex >= _screens.Count)
        {
            return false;
        }

        _screenIndex = screenIndex;
        _errors = [];
        return true;
    }

    public void GoBack()
    {
        if (_screenIndex > 0)
        {
            _screenIndex--;
        }

        _errors = [];
    }

    // ---- values --------------------------------------------------------------------------

    public string GetValue(string fieldKey, int rowIndex = -1) =>
        CurrentStep.GetValue(_data, fieldKey, ResolveRow(rowIndex));

    public void SetValue(string fieldKey, string value, int rowIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(value);
        CurrentStep.SetValue(_data, fieldKey, ResolveRow(rowIndex), value);
        IsDirty = true;
        RebuildScreens();
    }

    /// <summary>Errors for the current screen, populated by the last <see cref="TryGoNext"/>.</summary>
    public IReadOnlyList<WizardFieldError> Errors => _errors;

    /// <summary>Drives the "throw away what you typed?" confirmation on Cancel.</summary>
    public bool IsDirty { get; private set; }

    // ---- rows ----------------------------------------------------------------------------

    public int RowCount => CurrentStep.GetRowCount(_data);

    public string GetRowLabel(int rowIndex) => CurrentStep.GetRowLabel(_data, rowIndex);

    public int AddRow()
    {
        int index = CurrentStep.AddRow(_data);
        if (index >= 0)
        {
            IsDirty = true;
            RebuildScreens();
        }

        return index;
    }

    public bool RemoveRow(int rowIndex)
    {
        if (!CurrentStep.RemoveRow(_data, rowIndex))
        {
            return false;
        }

        IsDirty = true;
        RebuildScreens();
        return true;
    }

    public bool MoveRow(int from, int to)
    {
        if (!CurrentStep.MoveRow(_data, from, to))
        {
            return false;
        }

        IsDirty = true;
        return true;
    }

    /// <summary>Collapses a one-row-per-screen list onto a single screen, for this session only (§6.7).</summary>
    public void ShowAllRows()
    {
        _showAllRows.Add(_screens[_screenIndex].StepIndex);
        int step = _screens[_screenIndex].StepIndex;
        RebuildScreens();
        _screenIndex = _screens.FindIndex(s => s.StepIndex == step);
        if (_screenIndex < 0)
        {
            _screenIndex = 0;
        }
    }

    public bool IsShowingAllRows(int stepIndex) => _showAllRows.Contains(stepIndex);

    // ---- finish --------------------------------------------------------------------------

    /// <summary>
    /// The review screen's read-back. Each line carries the screen that produced it, so "Change
    /// this" lands on the answer the user is looking at rather than sending them back to the start
    /// (docs/M7-spec.md §6.6) — the whole point of a review step for someone who has just answered
    /// fourteen questions.
    /// </summary>
    public IReadOnlyList<WizardReviewLine> ReviewLines
    {
        get
        {
            var lines = new List<WizardReviewLine>();
            for (int stepIndex = 0; stepIndex < Definition.Wizard.Steps.Count; stepIndex++)
            {
                IWizardStep step = Definition.Wizard.Steps[stepIndex];
                if (step.Kind == WizardStepKind.Review || !step.IsActive(_data))
                {
                    continue;
                }

                IReadOnlyList<string> summary = step.GetSummaryLines(_data);

                // A list step's summary is one line per row, so a paginated one maps line i to the
                // screen showing row i. Everything else has a single screen.
                bool paginated = step is IWizardListStep { Pagination: WizardListPagination.OneRowPerScreen }
                    && !_showAllRows.Contains(stepIndex)
                    && step.GetRowCount(_data) == summary.Count;

                for (int i = 0; i < summary.Count; i++)
                {
                    lines.Add(new WizardReviewLine(
                        summary[i],
                        FindScreenIndex(stepIndex, paginated ? i : -1)));
                }
            }

            return lines;
        }
    }

    /// <summary>Screen showing a given step (and row, when it is paginated); 0 if it has none.</summary>
    public int FindScreenIndex(int stepIndex, int rowIndex)
    {
        for (int i = 0; i < _screens.Count; i++)
        {
            if (_screens[i].StepIndex != stepIndex)
            {
                continue;
            }

            if (rowIndex < 0 || _screens[i].RowIndex == rowIndex)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Validates EVERY active step; the caller navigates to the first failure's screen.</summary>
    public bool TryCommit(out JsonElement data, out int dataVersion, out IReadOnlyList<WizardFieldError> errors)
    {
        var found = new List<WizardFieldError>();
        for (int i = 0; i < Definition.Wizard.Steps.Count; i++)
        {
            IWizardStep step = Definition.Wizard.Steps[i];
            if (step.Kind != WizardStepKind.Review && step.IsActive(_data))
            {
                found.AddRange(step.Validate(_data, -1));
            }
        }

        if (found.Count > 0)
        {
            data = default;
            dataVersion = 0;
            errors = found;
            _errors = found;
            return false;
        }

        data = Definition.WriteData(_data);
        dataVersion = Definition.CurrentDataVersion;
        errors = [];
        return true;
    }

    /// <summary>Screen index holding a given error, so the caller can navigate to it.</summary>
    public int FindScreen(WizardFieldError error)
    {
        for (int i = 0; i < _screens.Count; i++)
        {
            IWizardStep step = Definition.Wizard.Steps[_screens[i].StepIndex];
            if (!step.Fields.Any(f => string.Equals(f.Key, error.FieldKey, StringComparison.Ordinal)))
            {
                continue;
            }

            if (_screens[i].RowIndex < 0 || _screens[i].RowIndex == error.RowIndex)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>The Edit-menu sentence for the undo step this wizard produces (§7.3).</summary>
    public string UndoLabel =>
        string.Create(CultureInfo.InvariantCulture, $"Edit {Definition.DisplayName.ToLowerInvariant()}");

    // ---- step-scoped access (the grid editor's surface) --------------------------------------
    // The grid is a second VIEW over this same session, not a second session type: that is what
    // makes "both editors emit the same POCO through one command" true by construction (§7.2).

    /// <summary>Steps the current data makes relevant, in order, excluding the review step.</summary>
    public IReadOnlyList<IWizardStep> ActiveSteps
    {
        get
        {
            var steps = new List<IWizardStep>();
            foreach (IWizardStep step in Definition.Wizard.Steps)
            {
                if (step.Kind != WizardStepKind.Review && step.IsActive(_data))
                {
                    steps.Add(step);
                }
            }

            return steps;
        }
    }

    public string GetValue(IWizardStep step, string fieldKey, int rowIndex)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.GetValue(_data, fieldKey, rowIndex);
    }

    public void SetValue(IWizardStep step, string fieldKey, int rowIndex, string value)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(value);
        step.SetValue(_data, fieldKey, rowIndex, value);
        IsDirty = true;
        RebuildScreens();
    }

    public int GetRowCount(IWizardStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.GetRowCount(_data);
    }

    public string GetRowLabel(IWizardStep step, int rowIndex)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.GetRowLabel(_data, rowIndex);
    }

    public int AddRow(IWizardStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        int index = step.AddRow(_data);
        if (index >= 0)
        {
            IsDirty = true;
            RebuildScreens();
        }

        return index;
    }

    public bool RemoveRow(IWizardStep step, int rowIndex)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (!step.RemoveRow(_data, rowIndex))
        {
            return false;
        }

        IsDirty = true;
        RebuildScreens();
        return true;
    }

    public bool MoveRow(IWizardStep step, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (!step.MoveRow(_data, from, to))
        {
            return false;
        }

        IsDirty = true;
        return true;
    }

    /// <summary>True when at least one step is a list — the condition for offering "Edit list…".</summary>
    public bool HasListSteps => Definition.Wizard.Steps.Any(s => s is IWizardListStep);

    /// <summary>The live POCO — for the grid view, which edits the same object.</summary>
    internal object Data => _data;

    private int ResolveRow(int rowIndex) =>
        rowIndex >= 0 ? rowIndex : _screens[_screenIndex].RowIndex;

    private IReadOnlyList<WizardFieldError> ValidateScreen(int screenIndex)
    {
        Screen screen = _screens[screenIndex];
        IWizardStep step = Definition.Wizard.Steps[screen.StepIndex];
        return step.Kind == WizardStepKind.Review ? [] : step.Validate(_data, screen.RowIndex);
    }

    private void RebuildScreens()
    {
        // Remember the screen the user is ACTUALLY on — step and row — not just its numeric index:
        // adding or removing a row before the current one leaves the index pointing at a different
        // row of the same step, which is invisible to a step-only comparison.
        Screen? previous = _screens.Count > 0 ? _screens[Math.Min(_screenIndex, _screens.Count - 1)] : null;
        _screens.Clear();
        IReadOnlyList<IWizardStep> steps = Definition.Wizard.Steps;
        for (int i = 0; i < steps.Count; i++)
        {
            IWizardStep step = steps[i];
            if (!step.IsActive(_data))
            {
                continue;
            }

            bool paginated = step is IWizardListStep { Pagination: WizardListPagination.OneRowPerScreen }
                && !_showAllRows.Contains(i);
            int rowCount = paginated ? step.GetRowCount(_data) : 0;
            if (paginated && rowCount > 0)
            {
                for (int row = 0; row < rowCount; row++)
                {
                    _screens.Add(new Screen(i, row));
                }
            }
            else
            {
                _screens.Add(new Screen(i, -1));
            }
        }

        if (_screens.Count == 0)
        {
            _screenIndex = 0;
            return;
        }

        if (previous is not { } was)
        {
            _screenIndex = Math.Min(_screenIndex, _screens.Count - 1);
            return;
        }

        // Same screen still there: stay on it, wherever it moved to.
        int exact = _screens.IndexOf(was);
        if (exact >= 0)
        {
            _screenIndex = exact;
            return;
        }

        // Its row went away (or a step did): land on the nearest survivor of that step, else on the
        // next step that still exists.
        int sameStep = _screens.FindLastIndex(s => s.StepIndex == was.StepIndex);
        if (sameStep >= 0)
        {
            _screenIndex = was.RowIndex < 0
                ? sameStep
                : Math.Min(sameStep, _screens.FindIndex(s => s.StepIndex == was.StepIndex) + was.RowIndex);
            return;
        }

        int fallback = _screens.FindIndex(s => s.StepIndex >= was.StepIndex);
        _screenIndex = fallback >= 0 ? fallback : _screens.Count - 1;
    }

    private readonly record struct Screen(int StepIndex, int RowIndex);
}
