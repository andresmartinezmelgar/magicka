using Godot;
using Arcana.Core;

namespace Arcana.Client;

public partial class Launcher
{
    private void UpdateOptionAPage(int page)
    {
        if (_optionAHeading != null) _optionAHeading.Text = page switch
        {
            1 => "PARTIDAS MULTIJUGADOR",
            2 => "ENTRENAMIENTO CONTRA IA",
            _ => "ELIGE TU AVENTURA"
        };
        UpdateTutorialLink();
        if (_optionAMagic != null) _optionAMagic.Visible = page == 2;
        if (_optionAPasswordLabel != null) _optionAPasswordLabel.Visible = page == 1;
        _optionAChrome?.Select(page switch { 1 => 1, 2 => 2, _ => 0 });
    }

    public void NavigateOptionA(int target)
    {
        if (_main == null) return;
        switch (target)
        {
            case 0: CloseRooms(); OpenPage(0); break;
            case 1: OpenPvp(); break;
            case 2: CloseRooms(); OpenPage(2); break;
            case 3: _main.OpenAccountProfile(); break;
            case 4: _main.OpenEquipment("automatic"); break;
            case 5: _main.ToggleHelp(); break;
            case 6: GetTree().Quit(); break;
            case 7: _main.OpenItemShop(); break;
        }
    }
}
