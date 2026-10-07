namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

/// <summary>
/// ZENITH-APEX COMMAND INTERFACE v1.0.0
/// Defines the essential contract for reversible narrative operations.
/// Every implementation must be capable of restoring the graph to its 
/// exact previous state.
/// </summary>
public interface ICommand
{
    /// <summary>
    /// A human-readable description of the action for the Editorial UI.
    /// Example: "Moved State 'Alpha' to (200, 450)"
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Executes the forward action and applies changes to the DialogueRoot.
    /// </summary>
    void Execute();

    /// <summary>
    /// Reverses the action, returning the DialogueRoot to its prior state.
    /// </summary>
    void Undo();
}