using xLostProHome.Ecosystem.Web.DialogueMachine.Models;

namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

/// <summary>
/// CHOICE CREATION COMMAND
/// Handles the addition of a player navigation node to a specific character state.
/// </summary>
public class AddChoiceCommand : ICommand
{
    private readonly CharacterState _parent;
    private readonly Choice _choice;

    public string Description => "Added Player Choice";

    public AddChoiceCommand(CharacterState parent, Choice choice)
    {
        _parent = parent;
        _choice = choice;
    }

    public void Execute() => _parent.Choices.Add(_choice);
    public void Undo() => _parent.Choices.Remove(_choice);
}