using xLostProHome.Ecosystem.Web.DialogueMachine.Models;

namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

/// <summary>
/// STATE DECONSTRUCTION COMMAND
/// Safely removes a state from the graph while preserving its data for restoration.
/// Note: Pointers to this ID from other nodes are naturally restored when the 
/// ID is re-registered in the ListOfCharacterStates.
/// </summary>
public class DeleteStateCommand : ICommand
{
    private readonly DialogueRoot _root;
    private readonly CharacterState _state;
    private readonly int _originalIndex;

    public string Description => $"Deleted State '{_state.State}'";

    public DeleteStateCommand(DialogueRoot root, CharacterState state)
    {
        _root = root;
        _state = state;
        _originalIndex = root.ListOfCharacterStates.IndexOf(state);
    }

    public void Execute() => _root.ListOfCharacterStates.Remove(_state);

    public void Undo() 
    {
        if (_originalIndex >= 0 && _originalIndex <= _root.ListOfCharacterStates.Count)
            _root.ListOfCharacterStates.Insert(_originalIndex, _state);
        else
            _root.ListOfCharacterStates.Add(_state);
    }
}