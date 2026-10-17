using Godot;

namespace Arcana.Client;

public partial class Main
{
    private OptionAHelpPanel? _optionAHelp;

    private void ToggleOptionAHelp()
    {
        if (GodotObject.IsInstanceValid(_optionAHelp))
        {
            CloseOptionAHelp();
            return;
        }
        _optionAHelp = new OptionAHelpPanel();
        _ui.AddChild(_optionAHelp);
        _optionAHelp.Build(this);
    }

    private void CloseOptionAHelp()
    {
        if (GodotObject.IsInstanceValid(_optionAHelp))
            _optionAHelp!.QueueFree();
        _optionAHelp = null;
    }

}
