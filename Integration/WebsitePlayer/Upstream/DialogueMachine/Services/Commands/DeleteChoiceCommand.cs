using xLostProHome.Ecosystem.Web.DialogueMachine.Models;

namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

public class DeleteChoiceCommand : ICommand
{
    private readonly CharacterState _parent;
    private readonly Choice _choice;
    private readonly int _originalIndex;

    public string Description => $"Deleted Choice: '{_choice.ButtonText}'";

    public DeleteChoiceCommand(CharacterState parent, Choice choice)
    {
        _parent = parent;
        _choice = choice;
        // We store the index so "Undo" puts it back exactly where it was
        _originalIndex = parent.Choices.IndexOf(choice);
    }

    public void Execute() => _parent.Choices.Remove(_choice);

    public void Undo()
    {
        if (_originalIndex >= 0 && _originalIndex <= _parent.Choices.Count)
            _parent.Choices.Insert(_originalIndex, _choice);
        else
            _parent.Choices.Add(_choice);
    }
}