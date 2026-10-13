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

    /// <summary>Conserva las demostraciones del libro existente sin tocar la ayuda durante la partida.</summary>
    public void OpenOriginalSpellBookFromMenu()
    {
        if (_playing) return;
        CloseOptionAHelp();
        _help.Visible = true;
        _helpPages.CurrentTab = 4;
    }
}
