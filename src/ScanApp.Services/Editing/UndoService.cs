namespace ScanApp.Services.Editing;

/// <summary>One reversible operation (edit change, page add/remove/reorder…).</summary>
public sealed record UndoableAction(string Label, Action Undo, Action Redo, string? CoalesceKey = null);

/// <summary>
/// Application-wide undo/redo history. Actions carry their own undo/redo closures so any kind of
/// operation (per-page edit change, page deletion, reorder) can participate. Consecutive actions
/// with the same non-null <see cref="UndoableAction.CoalesceKey"/> within a short window merge into
/// one step, so a slider drag is a single undo instead of fifty.
/// </summary>
public sealed class UndoService
{
    private const int MaxDepth = 100;
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(900);

    private readonly List<UndoableAction> _undo = new();
    private readonly Stack<UndoableAction> _redo = new();
    private DateTime _lastPushAt;
    private bool _applying;

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(UndoableAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_applying)
        {
            return; // changes performed BY undo/redo must not re-record
        }

        var now = DateTime.UtcNow;
        if (action.CoalesceKey is not null
            && _undo.Count > 0
            && _undo[^1].CoalesceKey == action.CoalesceKey
            && now - _lastPushAt < CoalesceWindow)
        {
            // Merge: keep the earliest Undo (restores the true "before"), adopt the newest Redo.
            var prev = _undo[^1];
            _undo[^1] = prev with { Redo = action.Redo, Label = action.Label };
        }
        else
        {
            _undo.Add(action);
            if (_undo.Count > MaxDepth)
            {
                _undo.RemoveAt(0);
            }
        }

        _lastPushAt = now;
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }
        var action = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        RunGuarded(action.Undo);
        _redo.Push(action);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }
        var action = _redo.Pop();
        RunGuarded(action.Redo);
        _undo.Add(action);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RunGuarded(Action action)
    {
        _applying = true;
        try
        {
            action();
        }
        finally
        {
            _applying = false;
        }
    }
}
