using System.Collections.Generic;
using System.Linq; // Ensure Linq is present for .Select
using xLostProHome.Core.Services;
using xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services;

/// <summary>
/// CHRONOS HISTORY MANAGER v1.1.0
/// Orchestrates the Undo/Redo stacks for the Dialogue Machine.
/// Provides a "Journalled" view of the session's modifications.
/// </summary>
public class HistoryManager
{
    private readonly Stack<ICommand> _undoStack = new();
    private readonly Stack<ICommand> _redoStack = new();
    private readonly ToastService _toasts;

    public event Action? OnHistoryChanged;

    public HistoryManager(ToastService toasts)
    {
        _toasts = toasts;
    }

    /// <summary>
    /// Records a new command, executes it, and clears the Redo stack 
    /// to maintain chronological consistency.
    /// </summary>
    public void ExecuteCommand(ICommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear(); // New actions invalidate the redo timeline
        
        NotifyStateChanged();
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);

        _toasts.ShowToast($"Undo: {command.Description}", ToastLevel.Info);
        NotifyStateChanged();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);

        _toasts.ShowToast($"Redo: {command.Description}", ToastLevel.Info);
        NotifyStateChanged();
    }

    /// <summary>
    /// Returns a list of descriptions for the Editorial sidebar.
    /// </summary>
    public IEnumerable<string> GetUndoHistory() => _undoStack.Select(c => c.Description);

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnHistoryChanged?.Invoke();
}