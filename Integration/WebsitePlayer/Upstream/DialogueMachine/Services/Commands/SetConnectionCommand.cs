using xLostProHome.Ecosystem.Web.DialogueMachine.Models;

namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

/// <summary>
/// TOPOLOGICAL CONNECTION COMMAND v1.0.0
/// Manages the creation and severance of Bezier connections between nodes.
/// Supports both State Fallbacks and Choice Navigation targets.
/// Standards: British English (Initialisation, Normalisation).
/// </summary>
public class SetConnectionCommand : ICommand
{
    private readonly object _source; // CharacterState or Choice
    private readonly string? _oldTargetId;
    private readonly string? _newTargetId;

    public string Description => "Modified Node Connection";

    public SetConnectionCommand(object source, string? oldId, string? newId)
    {
        _source = source;
        _oldTargetId = oldId;
        _newTargetId = newId;
    }

    /// <summary>
    /// Applies the new target ID to the source object.
    /// </summary>
    public void Execute() => ApplyId(_newTargetId);

    /// <summary>
    /// Restores the original target ID, physically recreating the Bezier path.
    /// </summary>
    public void Undo() => ApplyId(_oldTargetId);

    private void ApplyId(string? id)
    {
        if (_source is CharacterState state) 
        {
            state.DefaultNextId = id;
        }
        else if (_source is Choice choice) 
        {
            choice.NextStateId = id;
        }
    }
}