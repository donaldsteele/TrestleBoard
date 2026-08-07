using TrestleBoard.Roster.Tables;

namespace TrestleBoard.Roster.Import;

/// <summary>The six screens of the import flow (PLAN.md §11 M12).</summary>
public enum ImportStep
{
    /// <summary>"Where is the list?" — one big Browse button.</summary>
    ChooseFile,

    /// <summary>"Which sheet?" — skipped unless the workbook has more than one.</summary>
    ChooseSheet,

    /// <summary>"Which row has the column titles?" — first eight rows, one big radio each.</summary>
    ChooseHeaderRow,

    /// <summary>"Match up the columns." — one question per lodge field.</summary>
    MapColumns,

    /// <summary>"Have a look before we add these."</summary>
    Review,

    /// <summary>"Done."</summary>
    Done,
}

/// <summary>
/// The import flow with no window attached (PLAN.md §11 M12).
///
/// It borrows <c>WizardWindow</c>'s visual language — 20pt, one question per screen, big Back and
/// Next, a review before anything happens — but deliberately <b>not</b> its
/// <c>WizardDefinition</c>/<c>WizardSession</c> machinery: the mapping step is dynamic (columns are
/// discovered at runtime) and the preview is a table, so bending the widget wizard to fit would make
/// both worse. What it does copy is the architectural trick that makes the widget wizard testable —
/// <b>all state lives here and the window is a mere renderer</b> — so this entire flow, including
/// every sentence the user reads, is exercised without Avalonia.
///
/// Nothing is written until <see cref="Commit"/>. Screen one says so out loud.
/// </summary>
public sealed class RosterImportSession
{
    private readonly RosterBook _current;
    private readonly Dictionary<int, DuplicateAnswer> _answers = [];
    private Dictionary<RosterField, int> _mapping = [];
    private TableWorkbook? _workbook;
    private int _sheetIndex;
    private int _headerRow = -1;

    public RosterImportSession(RosterBook current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _current = current;
    }

    public ImportStep Step { get; private set; } = ImportStep.ChooseFile;

    /// <summary>The heading of the screen the user is on, in their words.</summary>
    public string Title => Step switch
    {
        ImportStep.ChooseFile => "Where is the list?",
        ImportStep.ChooseSheet => "Which sheet?",
        ImportStep.ChooseHeaderRow => "Which row has the column titles?",
        ImportStep.MapColumns => "Match up the columns.",
        ImportStep.Review => "Have a look before we add these.",
        _ => "Done.",
    };

    /// <summary>The sentence under the heading. On screen one it is the promise the whole flow rests on.</summary>
    public string Explanation => Step switch
    {
        ImportStep.ChooseFile =>
            "Find the spreadsheet or list your lodge already keeps. Nothing changes until you say so "
            + "at the end.",
        ImportStep.ChooseSheet =>
            "This workbook has more than one sheet. Which one has the members in it?",
        ImportStep.ChooseHeaderRow =>
            "Some lists start with a row of column titles, and some do not. Here are the first few "
            + "rows of yours.",
        ImportStep.MapColumns =>
            "We guessed these. Change any that are wrong.",
        ImportStep.Review =>
            "Nothing has changed yet. Have a look, then press Add these people.",
        _ => "Your address book has been updated.",
    };

    /// <summary>The file the user chose, or null.</summary>
    public string? SourcePath => _workbook?.SourcePath;

    public IReadOnlyList<string> SheetNames =>
        _workbook is null ? [] : _workbook.Sheets.Select(s => s.Name).ToList();

    public TableSheet? Sheet => _workbook is null ? null : _workbook.Sheets[_sheetIndex];

    /// <summary>Which row holds the column titles; -1 means "there are no column titles".</summary>
    public int HeaderRow => _headerRow;

    /// <summary>The first rows, as the "which row has the titles?" screen shows them.</summary>
    public IReadOnlyList<IReadOnlyList<string>> HeaderCandidates =>
        Sheet is not { } sheet
            ? []
            : Enumerable.Range(0, Math.Min(ColumnGuesser.HeaderCandidateRows, sheet.RowCount))
                .Select(r => (IReadOnlyList<string>)Enumerable.Range(0, sheet.ColumnCount)
                    .Select(c => sheet.Cell(r, c))
                    .ToList())
                .ToList();

    public IReadOnlyList<TableColumn> Columns =>
        Sheet is { } sheet ? ColumnGuesser.Columns(sheet, _headerRow) : [];

    public IReadOnlyDictionary<RosterField, int> Mapping => _mapping;

    /// <summary>Off means "leave them alone instead" on the review screen.</summary>
    public bool UpdateExisting { get; set; } = true;

    /// <summary>How many people the book will hold afterwards — the number the last screen says.</summary>
    public int ResultCount => Plan().Result.Count;

    /// <summary>
    /// Reads the file the user chose and moves to the next screen that has a question worth asking.
    /// Throws <see cref="TableReadException"/>, whose message is already addressed to the user.
    /// </summary>
    public void ChooseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _workbook = TableFileReader.Read(path);
        _sheetIndex = 0;
        _answers.Clear();

        // One sheet is the common case, and a screen with one answer on it is a screen that wastes
        // the user's time (PLAN.md §11 M12: "skipped otherwise").
        Step = _workbook.Sheets.Count > 1 ? ImportStep.ChooseSheet : ImportStep.ChooseHeaderRow;
        if (Step == ImportStep.ChooseHeaderRow)
        {
            GuessHeaderAndMapping();
        }
    }

    public void ChooseSheet(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, SheetNames.Count);

        _sheetIndex = index;
        GuessHeaderAndMapping();
        Step = ImportStep.ChooseHeaderRow;
    }

    /// <summary>-1 for "there are no column titles"; re-guesses the mapping, since it depends on this.</summary>
    public void ChooseHeaderRow(int row)
    {
        // Clamped at BOTH ends. A row past the end of the sheet used to be accepted, and produced
        // an empty mapping and an empty plan with no error anywhere — the import simply appeared to
        // find nobody (review §14.2). Out of range now means "no column titles", which is the
        // answer that at least tries to read the file.
        _headerRow = row < 0 || (Sheet is { } s && row >= s.RowCount) ? -1 : row;
        if (Sheet is { } sheet)
        {
            _mapping = ColumnGuesser.GuessMapping(sheet, _headerRow);
        }

        Step = ImportStep.MapColumns;
    }

    /// <summary>Points a lodge field at a column, or at nothing ("Not in this file").</summary>
    public void MapColumn(RosterField field, int? column)
    {
        if (column is { } index && index >= 0)
        {
            _mapping[field] = index;
        }
        else
        {
            _mapping.Remove(field);
        }
    }

    /// <summary>Only the name is required; that is the whole of what makes a row usable.</summary>
    public bool CanReview => _mapping.ContainsKey(RosterField.Name);

    /// <summary>The sentence shown when Next cannot yet be pressed — never a silent dead end (PLAN.md §6).</summary>
    public string? WhyNotReady => CanReview
        ? null
        : "Choose which column has the names in it. Without names there is nobody to add.";

    public void GoToReview()
    {
        if (!CanReview)
        {
            throw new InvalidOperationException(WhyNotReady);
        }

        Step = ImportStep.Review;
    }

    /// <summary>"Are these the same person?" — answered one row at a time, never in bulk.</summary>
    public void Answer(int rowNumber, DuplicateAnswer answer) => _answers[rowNumber] = answer;

    /// <summary>What the import would do. Recomputed rather than cached: every screen can change it.</summary>
    public MergePlan Plan()
    {
        if (Sheet is not { } sheet || !_mapping.ContainsKey(RosterField.Name))
        {
            return new MergePlan(_current, [], []);
        }

        return RosterMerge.Plan(
            _current,
            sheet,
            _headerRow,
            _mapping,
            new MergeOptions { UpdateExisting = UpdateExisting, Answers = _answers });
    }

    /// <summary>
    /// The address book as it will be. The caller writes it — this type never touches the disk,
    /// which is what lets the whole flow be tested without one.
    /// </summary>
    public RosterBook Commit()
    {
        MergePlan plan = Plan();
        Step = ImportStep.Done;
        return plan.Result;
    }

    /// <summary>"Your address book now has 118 people." — the last screen, and the only number on it.</summary>
    public static string DoneMessage(int count) => count == 1
        ? "Your address book now has 1 person."
        : $"Your address book now has {count} people.";

    /// <summary>
    /// The rows that could not be used, one line each, ready to be written to a file — nothing is
    /// silently lost (PLAN.md §11 M12, screen 5).
    /// </summary>
    public string UnusableRowsText()
    {
        MergePlan plan = Plan();
        var lines = new List<string>
        {
            "Rows TrestleBoard could not use from " + (SourcePath ?? "your file"),
            string.Empty,
        };

        foreach (PlannedRow row in plan.Unusable)
        {
            lines.Add($"Row {row.RowNumber}: {row.Note}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Back a screen, skipping the ones that were skipped on the way in.</summary>
    public void Back() => Step = Step switch
    {
        ImportStep.ChooseSheet => ImportStep.ChooseFile,
        ImportStep.ChooseHeaderRow => SheetNames.Count > 1 ? ImportStep.ChooseSheet : ImportStep.ChooseFile,
        ImportStep.MapColumns => ImportStep.ChooseHeaderRow,
        ImportStep.Review => ImportStep.MapColumns,
        _ => Step,
    };

    private void GuessHeaderAndMapping()
    {
        if (Sheet is not { } sheet)
        {
            return;
        }

        _headerRow = ColumnGuesser.GuessHeaderRow(sheet);
        _mapping = ColumnGuesser.GuessMapping(sheet, _headerRow);
    }
}
