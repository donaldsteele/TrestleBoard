using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Commands;

public enum ChangeOrigin
{
    Execute,
    Undo,
    Redo,
}

public sealed class DocumentChangedEventArgs(IDocumentCommand command, ChangeOrigin origin) : EventArgs
{
    public IDocumentCommand Command { get; } = command;

    public ChangeScope Scope { get; } = command.Scope;

    public ChangeOrigin Origin { get; } = origin;
}

/// <summary>
/// Executes commands against the document and owns the undo/redo stacks (PLAN.md §4).
/// Keystroke coalescing: a command executed within <see cref="CoalesceWindow"/> of the previous
/// one is offered to the undo-stack top via TryMerge, so Undo removes word-bursts. Undo depth
/// is effectively unlimited. Executing a new command clears the redo stack.
/// </summary>
public sealed class DocumentSession(Document document, TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(1);

    private readonly Stack<IDocumentCommand> _undo = new();
    private readonly Stack<IDocumentCommand> _redo = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private long? _lastExecuteTimestamp;

    public Document Document { get; } = document ?? throw new ArgumentNullException(nameof(document));

    public event EventHandler<DocumentChangedEventArgs>? Changed;

    public bool CanUndo => _undo.Count > 0;

    /// <summary>
    /// How many commands are on the undo stack right now.
    ///
    /// <para>M27: this is what lets a live-preview dialog offer a real Cancel. The photo adjust
    /// window applies each slider to the document as it moves, which is the whole point of it —
    /// the user watches the page change — but it left them no way back except Ctrl+Z pressed an
    /// unknown number of times. A window that notes this number when it opens can put the document
    /// back exactly as it found it, using the same undo the rest of the app uses rather than a
    /// second, parallel notion of "before".</para>
    /// </summary>
    public int UndoDepth => _undo.Count;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Plain-language label for the Edit menu, e.g. "Undo Move block".</summary>
    public string? UndoDescription => _undo.Count > 0 ? _undo.Peek().Description : null;

    public string? RedoDescription => _redo.Count > 0 ? _redo.Peek().Description : null;

    public void Execute(IDocumentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Apply(Document);
        _redo.Clear();

        long now = _time.GetTimestamp();
        bool merged = _undo.Count > 0
            && _lastExecuteTimestamp is { } last
            && _time.GetElapsedTime(last, now) < CoalesceWindow
            && _undo.Peek().TryMerge(command);
        if (!merged)
        {
            _undo.Push(command);
        }

        _lastExecuteTimestamp = now;
        Changed?.Invoke(this, new DocumentChangedEventArgs(command, ChangeOrigin.Execute));
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        IDocumentCommand command = _undo.Pop();
        command.Revert(Document);
        _redo.Push(command);
        _lastExecuteTimestamp = null;
        Changed?.Invoke(this, new DocumentChangedEventArgs(command, ChangeOrigin.Undo));
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        IDocumentCommand command = _redo.Pop();
        command.Apply(Document);
        _undo.Push(command);
        _lastExecuteTimestamp = null;
        Changed?.Invoke(this, new DocumentChangedEventArgs(command, ChangeOrigin.Redo));
    }
}
