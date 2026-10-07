namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

/// <summary>
/// ATOMIC PROPERTY COMMAND v1.0.0
/// A polymorphic command designed to handle simple property mutations 
/// such as toggles, enums, and ID changes.
/// </summary>
public class UpdatePropertyCommand<T> : ICommand
{
    private readonly Action<T> _setter;
    private readonly T _oldValue;
    private readonly T _newValue;
    public string Description { get; }

    public UpdatePropertyCommand(string description, T oldValue, T newValue, Action<T> setter)
    {
        Description = description;
        _oldValue = oldValue;
        _newValue = newValue;
        _setter = setter;
    }

    public void Execute() => _setter(_newValue);
    public void Undo() => _setter(_oldValue);
}