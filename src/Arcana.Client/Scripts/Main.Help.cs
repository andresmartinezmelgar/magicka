using Godot;

namespace Arcana.Client;

public partial class Main
{
    private HelpPanel? _menuHelp;

    private void ToggleMenuHelp()
    {
        if (GodotObject.IsInstanceValid(_menuHelp))
        {
            CloseMenuHelp();
            return;
        }
        _menuHelp = new HelpPanel();
        _ui.AddChild(_menuHelp);
        _menuHelp.Build(this, inGame: false, CloseMenuHelp);
    }

    private void CloseMenuHelp()
    {
        if (GodotObject.IsInstanceValid(_menuHelp))
            _menuHelp!.QueueFree();
        _menuHelp = null;
    }

    private HelpPanel BuildHelp()
    {
        var help = new HelpPanel { Name = "InGameHelp" };
        _ui.AddChild(help);
        help.Build(this, inGame: true, CloseInGameHelp);
        return help;
    }
}
