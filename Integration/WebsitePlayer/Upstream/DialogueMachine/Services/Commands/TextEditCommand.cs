using xLostProHome.Ecosystem.Web.DialogueMachine.Models;

namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

/// <summary>
/// NARRATIVE EDITORIAL COMMAND v1.0.0
/// Handles the reversal of text modifications within a CharacterState.
/// Designed to capture "sessions" of typing to prevent stack flooding.
/// Standards: British English (Initialisation, Normalisation).
/// </summary>
public class TextEditCommand : ICommand
{
    private readonly CharacterState _state;
    private readonly string _oldText;
    private readonly string _newText;

    /// <summary>
    /// Human-readable description for the Command Centre audit log.
    /// </summary>
    public string Description => $"Edited Narrative in State '{_state.State}'";

    public TextEditCommand(CharacterState state, string oldText, string newText)
    {
        _state = state;
        _oldText = oldText;
        _newText = newText;
    }

    /// <summary>
    /// Applies the new text to the model.
    /// </summary>
    public void Execute()
    {
        _state.Text = _newText;
    }

    /// <summary>
    /// Restores the original text from before the editorial session.
    /// </summary>
    public void Undo()
    {
        _state.Text = _oldText;
    }
}