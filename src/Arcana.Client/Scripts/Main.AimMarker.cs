using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    internal const float ProjectileMarkerDistance = 7;
    private AimMarker _aimMarker = null!;
    private SpellRecipe? _markerSpell;

    private void UpdateAimMarker(WorldSnapshot world)
    {
        _aimMarker.Hide();
        var player = world.Players.FirstOrDefault(p => p.Id == _localId);
        if (!_playing || _paused || _help.Visible || InspectingEquipment || _waitingPanel?.Visible == true ||
            _defensePanel?.ShopOpen == true || world.WaitingForPlayers || world.MatchEnded || world.Defense?.Paused == true ||
            player is not { Alive: true, Frozen: <= 0 }) return;

        if (_touch is { PendingMagick: 1 or 2 } touch)
        {
            var (target, valid) = MagickPreview(world, player, touch.PendingMagick);
            ShowAimMarker(target, touch.PendingMagick == 2 ? SpellBalance.MeteorRadius : ArenaMap.WizardRadius,
                valid ? Ui.Gold : Ui.Red);
            return;
        }

        var elements = _touch != null ? _touch.ProjectileAimElements
            : player.Casting && player.CastingElements?.Contains(Element.Earth) == true ? player.CastingElements : player.Selected;
        if (elements == null || elements.Length == 0) return;
        if (_markerSpell == null || !elements.SequenceEqual(_markerSpell.Elements)) _markerSpell = new(elements);
        if (_markerSpell.Shape is not (SpellShape.Rock or SpellShape.Shards)) return;

        V2 direction = _aim - player.Position;
        if (direction.Length < 0.05f) direction = player.Facing;
        // La referencia indica dirección a una distancia fija en ambas plataformas.
        V2 point = player.Position + direction.Normalized * ProjectileMarkerDistance;
        ShowAimMarker(point, ArenaMap.WizardRadius, _markerSpell.Shape == SpellShape.Shards ? Ui.ElementColor(Element.Cold) : Ui.Gold,
            player.Position + direction.Normalized * 0.85f);
    }

    private void ShowAimMarker(V2 target, float radius, Color color, V2? from = null)
    {
        var camera = _renderer.Camera;
        Vector2 Project(V2 point) => camera.UnprojectPosition(ArenaRenderer.World(point, 0.05f));
        var at = Project(target);
        _aimMarker.ShowTarget(at, Project(target + new V2(radius, 0)) - at,
            Project(target + new V2(0, radius)) - at, color, from is { } origin ? Project(origin) : null);
    }
}
