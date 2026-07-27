namespace TrestleBoard.Roster;

/// <summary>
/// The address book the app is holding, and the one step of undo it offers (PLAN.md §11 M12).
///
/// <b>Roster undo is deliberately not an <c>IDocumentCommand</c>.</b> Four reasons, and this is a
/// rule rather than a preference: <c>IDocumentCommand.Apply</c> takes a <c>Document</c> and a roster
/// is not one; <c>CommandTests</c>' coverage gate would demand <c>Apply;Revert == identity</c>
/// against a document fixture, a type mismatch the build would enforce; roster edits are coarse and
/// have to survive across sessions, which suits a snapshot ring better than an in-memory stack; and
/// decisively, sharing <c>DocumentSession</c> would make <b>Ctrl+Z in the newsletter undo an
/// address-book edit</b> — exactly the class of surprise M11 exists to eliminate.
///
/// So: one level of "Undo the last change" here, and "Restore an earlier version…" over
/// <see cref="RosterStore.Backups"/> for anything older. Snapshot restore is the right undo idiom
/// for this audience anyway — it is the one they already understand from photographs and documents.
/// </summary>
public sealed class RosterService
{
    private readonly RosterStore _store;
    private RosterBook _book;
    private RosterBook? _undo;
    private string? _undoDescription;
    private bool _hasEarlierVersions;

    public RosterService(RosterStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _book = store.Load();
        _hasEarlierVersions = store.Backups().Count > 0;
    }

    /// <summary>Raised after any change, so the People window and the "what's next" card can refresh.</summary>
    public event EventHandler? Changed;

    public RosterBook Book => _book;

    public string Location => _store.Path;

    public bool CanUndo => _undo is not null;

    /// <summary>"Undo adding A. Placeholder" — the menu item says what it will take back.</summary>
    public string? UndoDescription => _undoDescription;

    /// <summary>An id that has never been used in this book and never will be again.</summary>
    public string NextMemberId() => MemberIds.Next(_book);

    /// <summary>Adds or updates one person.</summary>
    public void Save(Member member, string description)
    {
        ArgumentNullException.ThrowIfNull(member);
        Apply(_book.With(member.Normalised()), description);
    }

    /// <summary>
    /// Removes one person. Only ever reached from the People window's plain-language confirm —
    /// an import never deletes anybody.
    /// </summary>
    public void Delete(string memberId, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);
        Apply(_book.Without(memberId), description);
    }

    /// <summary>Replaces the whole book — what an import commits, and what a restore commits.</summary>
    public void Replace(RosterBook book, string description) => Apply(book, description);

    /// <summary>
    /// Puts the book back as it was before the last change, and writes that. The replaced version
    /// goes into the backup ring like any other save, so undoing by mistake is itself recoverable.
    /// </summary>
    public bool Undo()
    {
        if (_undo is not { } previous)
        {
            return false;
        }

        _undo = null;
        _undoDescription = null;
        _book = previous;
        _hasEarlierVersions |= _store.Exists;
        _store.Save(_book);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>The kept copies, newest first, for "Restore an earlier version…".</summary>
    public IReadOnlyList<RosterBackup> Backups() => _store.Backups();

    public void Restore(RosterBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        Apply(RosterStore.ReadBackup(backup.Path), "Restore an earlier version of the address book");
    }

    /// <summary>Re-reads the file. Used after something outside this service wrote it.</summary>
    public void Reload()
    {
        _book = _store.Load();
        _undo = null;
        _undoDescription = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Is there an earlier version to restore? Tracked rather than re-scanned, because the shell
    /// asks this on every refresh and a directory listing per keystroke is not a thing to do.
    /// </summary>
    public bool HasEarlierVersions => _hasEarlierVersions;

    private void Apply(RosterBook book, string description)
    {
        ArgumentNullException.ThrowIfNull(book);

        // A save copies the file it replaces into the ring — so if there is a file now, there is a
        // kept copy afterwards.
        _hasEarlierVersions |= _store.Exists;
        _undo = _book;
        _undoDescription = description;
        _book = book.Normalised();
        _store.Save(_book);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Where a member id comes from. Sequential and highest-plus-one rather than lowest-free: an id
/// must never be reused, or a spreadsheet exported last month would re-import onto the wrong person
/// (PLAN.md §11 M12). The shape matches the document model's "frame-3" convention, which keeps
/// fixtures readable and test failures legible.
/// </summary>
public static class MemberIds
{
    public const string Prefix = "person-";

    public static string Next(RosterBook book)
    {
        ArgumentNullException.ThrowIfNull(book);

        int highest = 0;
        foreach (Member member in book.Members)
        {
            if (member.Id.StartsWith(Prefix, StringComparison.Ordinal)
                && int.TryParse(
                    member.Id.AsSpan(Prefix.Length),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int number)
                && number > highest)
            {
                highest = number;
            }
        }

        return Prefix + (highest + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
