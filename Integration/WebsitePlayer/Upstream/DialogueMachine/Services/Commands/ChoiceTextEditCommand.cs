using xLostProHome.Ecosystem.Web.DialogueMachine.Models;

namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

/// <summary>
/// CHOICE EDITORIAL COMMAND v1.0.0
/// Manages the reversal of text modifications within a player Choice node.
/// Ensures that player-facing navigation labels are tracked by the History Engine.
/// Standards: British English (Initialisation, Categorisation).
/// </summary>
public class ChoiceTextEditCommand : ICommand
{
    private readonly Choice _choice;
    private readonly string _oldText;
    private readonly string _newText;

    public string Description => $"Edited Choice: '{_newText.Substring(0, Math.Min(15, _newText.Length))}...'";

    public ChoiceTextEditCommand(Choice choice, string oldText, string newText)
    {
        _choice = choice;
        _oldText = oldText;
        _newText = newText;
    }

    public void Execute() => _choice.ButtonText = _newText;
    public void Undo() => _choice.ButtonText = _oldText;
}