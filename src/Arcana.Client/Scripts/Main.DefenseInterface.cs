using Godot;
using Arcana.Core;

namespace Arcana.Client;

public partial class Main
{
    private bool? _lastDefenseInterface;
    private ElementTextLabel? _defenseInterfaceHint;
    internal bool DefensiveAssistanceActive => DefenseInterfaceOpen && _snapshot?.Defense?.HumanCount > 1;
    private bool DefenseInterfaceOpen => _snapshot?.Defense != null && (_paused || _help.Visible ||
        InspectingEquipment || ControlsOpen || ViewingDamageSummary && _snapshot.Players.Any(p => p.Id == _localId && p.Alive) ||
        _hud?.ScoreOpen == true || Input.IsPhysicalKeyPressed(Key.Tab) ||
        _relicWindow?.Visible == true);

    private void SynchronizeDefenseInterface()
    {
        if (!_playing || _snapshot?.Defense == null)
        {
            _lastDefenseInterface = null;
            if (_defenseInterfaceHint != null) _defenseInterfaceHint.Text = "En red, la partida continúa mientras estás en este menú.";
            return;
        }
        bool open = DefenseInterfaceOpen;
        if (_defenseInterfaceHint != null)
            _defenseInterfaceHint.Text = _snapshot.Defense.HumanCount > 1
                ? "La partida continúa. Supervivencia máxima: tu mago prioriza el escudo y alejarse de los enemigos. Puede recibir daño."
                : "Partida en pausa: eres el único humano. Los bots y el combate se reanudan al cerrar estas pantallas.";
        if (_lastDefenseInterface == open) return;
        _lastDefenseInterface = open;
        _moveTo = false; _ignoreLeftUntilRelease = true; _touch?.Reset();
        Submit(new() { Type = "release" });
        Submit(new() { Type = "defense_shop", Slot = open ? 1 : 0 });
    }
}
