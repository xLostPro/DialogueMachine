using System.Collections;

namespace xLostProHome.Ecosystem.Web.DialogueMachine.Services.Commands;

/// <summary>
/// LOGIC COLLECTION COMMAND v1.0.0
/// Reversibly manages the addition or removal of logic entities 
/// (Actions/Requirements) within narrative collections.
/// </summary>
public class UpdateLogicListCommand : ICommand
{
    private readonly IList _list;
    private readonly object _item;
    private readonly bool _isAdding;
    private readonly int _index;

    public string Description => _isAdding ? "Added Logic Entry" : "Removed Logic Entry";

    public UpdateLogicListCommand(IList list, object item, bool isAdding)
    {
        _list = list;
        _item = item;
        _isAdding = isAdding;
        _index = isAdding ? list.Count : list.IndexOf(item);
    }

    public void Execute()
    {
        if (_isAdding) _list.Add(_item);
        else _list.Remove(_item);
    }

    public void Undo()
    {
        if (_isAdding) _list.Remove(_item);
        else
        {
            if (_index >= 0 && _index <= _list.Count) _list.Insert(_index, _item);
            else _list.Add(_item);
        }
    }
}