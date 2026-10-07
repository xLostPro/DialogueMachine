using xLostProHome.Ecosystem.Web.DialogueMachine.Models;

namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

/// <summary>
/// SPATIAL PERSISTENCE COMMAND
/// Handles the translation of nodes on the SVG canvas.
/// </summary>
public class MoveNodeCommand : ICommand
{
    private readonly CharacterState? _state;
    private readonly Choice? _choice;
    private readonly double _oldX, _oldY, _newX, _newY;

    public string Description => _state != null ? $"Moved State '{_state.State}'" : "Moved Choice Node";

    public MoveNodeCommand(CharacterState state, double oldX, double oldY, double newX, double newY)
    {
        _state = state; _oldX = oldX; _oldY = oldY; _newX = newX; _newY = newY;
    }

    public MoveNodeCommand(Choice choice, double oldX, double oldY, double newX, double newY)
    {
        _choice = choice; _oldX = oldX; _oldY = oldY; _newX = newX; _newY = newY;
    }

    public void Execute() 
    { 
        if (_state != null) { _state.VisualX = _newX; _state.VisualY = _newY; }
        else if (_choice != null) { _choice.VisualX = _newX; _choice.VisualY = _newY; }
    }

    public void Undo() 
    { 
        if (_state != null) { _state.VisualX = _oldX; _state.VisualY = _oldY; }
        else if (_choice != null) { _choice.VisualX = _oldX; _choice.VisualY = _oldY; }
    }
}