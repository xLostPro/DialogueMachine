using xLostProHome.Ecosystem.Web.DialogueMachine.Models;

namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

/// <summary>
/// STATE INITIALISATION COMMAND
/// Handles the atomic creation of a new narrative state.
/// </summary>
public class AddStateCommand : ICommand
{
    private readonly DialogueRoot _root;
    private readonly CharacterState _state;

    public string Description => $"Added State '{_state.State}'";

    public AddStateCommand(DialogueRoot root, CharacterState state)
    {
        _root = root;
        _state = state;
    }

    public void Execute() => _root.ListOfCharacterStates.Add(_state);
    public void Undo() => _root.ListOfCharacterStates.Remove(_state);
}