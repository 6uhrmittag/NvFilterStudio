namespace NvFilterStudio.Core.Model;

/// <summary>A point the editor can return to.</summary>
/// <param name="Json">The whole document, serialised.</param>
/// <param name="GameExePath">Which game was selected.</param>
/// <param name="SlotId">Which slot was selected.</param>
/// <param name="Description">What the step that followed it was, for the status line.</param>
public sealed record EditSnapshot(string Json, string? GameExePath, int? SlotId, string Description);

/// <summary>
/// Undo and redo over whole-document snapshots.
/// </summary>
/// <remarks>
/// Snapshots rather than inverse operations. The document serialises to a
/// string cheaply — around 9 KB for a real store — so at this scale the simple
/// approach is affordable and cannot drift out of step with the model the way a
/// hand-written inverse for "remove filter" eventually would.
/// <para>
/// Undo matters more here than in most editors because the destructive actions
/// are one click and easy to mis-aim: ✕ removes a filter outright, and pasting
/// a share code replaces an entire stack that may have taken a while to tune.
/// </para>
/// </remarks>
public sealed class EditHistory
{
    /// <summary>
    /// How many steps are kept. Each is a copy of the document, so this is a
    /// memory bound rather than a usability one.
    /// </summary>
    public const int Capacity = 50;

    private readonly LinkedList<EditSnapshot> _undo = new();
    private readonly Stack<EditSnapshot> _redo = new();

    /// <summary>Whether there is anything to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Whether there is anything to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>What undoing would reverse, for a tooltip or status line.</summary>
    public string? NextUndoDescription => _undo.Last?.Value.Description;

    /// <summary>
    /// Records the state <em>before</em> a change, along with what that change
    /// was about to be.
    /// </summary>
    public void Record(EditSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _undo.AddLast(snapshot);

        while (_undo.Count > Capacity)
        {
            _undo.RemoveFirst();
        }

        // A new edit invalidates any forward history, as everywhere else.
        _redo.Clear();
    }

    /// <summary>
    /// Steps back. <paramref name="current"/> is pushed onto the redo stack so
    /// the move is reversible.
    /// </summary>
    public EditSnapshot? Undo(EditSnapshot current)
    {
        if (_undo.Last is not { } previous)
        {
            return null;
        }

        _undo.RemoveLast();
        _redo.Push(current);
        return previous.Value;
    }

    /// <summary>Steps forward again.</summary>
    public EditSnapshot? Redo(EditSnapshot current)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        EditSnapshot next = _redo.Pop();
        _undo.AddLast(current);
        return next;
    }

    /// <summary>Forgets everything, for when the document is replaced wholesale.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
