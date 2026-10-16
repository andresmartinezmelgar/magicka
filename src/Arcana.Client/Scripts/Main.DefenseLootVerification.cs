using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private WorldSnapshot? _defenseLootReview;
    private readonly List<Label3D> _defenseLootLabels = [];
    private Vector3 _defenseCoinRotation;

    private void StartDefenseLootReview(WorldSnapshot state)
    {
        var drops = new List<DefenseDropSnapshot>();
        var names = new List<string>();
        void Add(DefenseDropKind kind, string name, EquipmentSlot? slot = null, ItemTier? tier = null, BuffKind buff = default)
        {
            int index = drops.Count;
            drops.Add(new(90000 + index, kind == DefenseDropKind.Gold ? 0 : _localId,
                new(-20 + (index % 6 - 2.5f) * 2.5f, (index / 6 - 0.5f) * 5.2f), kind, 10, buff, slot, tier));
            names.Add(name);
        }
        Add(DefenseDropKind.Gold, "Oro");
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            Add(DefenseDropKind.Equipment, EquipmentCatalogue.SlotName(slot), slot, (ItemTier)(int)slot);
        foreach (var buff in Enum.GetValues<BuffKind>()) Add(DefenseDropKind.Relic, RelicBuffs.Name(buff), buff: buff);
        for (int i = 0; i < drops.Count; i++)
        {
            var label = new Label3D { Text = names[i], Position = ArenaRenderer.World(drops[i].Position, 1.9f),
                FontSize = 26, PixelSize = 0.011f, Modulate = Ui.Text, OutlineSize = 7,
                OutlineModulate = new("070c15"), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
            _renderer.AddChild(label); _defenseLootLabels.Add(label);
        }
        // También se comprueba que una recogida privada ajena nunca llegue a dibujarse.
        drops.Add(new(99999, _localId + 1000, new(-20, 0), DefenseDropKind.Equipment, 0, default, EquipmentSlot.Chest, ItemTier.Mythic));
        _defenseLootReview = state with
        {
            Players = [state.Players.Single(p => p.Id == _localId) with { Position = new(-20, 7) }],
            Defense = state.Defense! with { Minions = [], Defenders = [], Drops = drops.ToArray() }
        };
        _ui.Visible = false;
        _renderer.PreviewFocus = new(-20, 0, 0);
        _renderer.SetPreviewZoom(17);
        _defenseSmokeFrame = 0;
    }

    private bool CheckDefenseLootReview()
    {
        try
        {
            _renderer.Render(_defenseLootReview!, _localId, 1 / 60.0, true);
            if (++_defenseSmokeFrame == 60)
            {
                if (_renderer.DefenseLootVisuals.Count != 12 || _renderer.DefenseLootVisuals.ContainsKey(99999))
                    throw new InvalidOperationException("El botín visible no respeta las doce variantes o su propietario.");
                _defenseCoinRotation = _renderer.DefenseLootVisuals[90000].BodyRotation;
                CaptureDefense("06-botin");
            }
            if (_defenseSmokeFrame < 90) return true;
            var coin = _renderer.DefenseLootVisuals[90000];
            if (!coin.SpinsVertically || coin.BodyRotation.X != 0 || coin.BodyRotation.Z != 0 ||
                Math.Abs(coin.BodyRotation.Y - _defenseCoinRotation.Y) < 0.5f)
                throw new InvalidOperationException("La moneda no gira sobre su eje vertical.");
            CaptureDefense("07-moneda-giro");
            var state = _defenseLootReview!;
            _renderer.Render(state with { Defense = state.Defense! with { Drops = [] } }, _localId, 0, true);
            if (_renderer.DefenseLootVisuals.Count != 0) throw new InvalidOperationException("Queda botín después de recogerlo.");
            foreach (var label in _defenseLootLabels) label.QueueFree();
            _defenseLootLabels.Clear(); _renderer.PreviewFocus = null; _renderer.Clear();
            if (_engineLog.ErrorCount > 0) throw new InvalidOperationException("Godot registró errores al dibujar el botín.");
            GD.Print("DEFENSE_LOOT_SMOKE_OK: moneda vertical animada, cuatro piezas, siete mejoras, privacidad y retirada de recogidas.");
            GD.Print("DEFENSE_SMOKE_OK: mapa, tanques, pausa con bots, inventario compartido, selección al salir, compra/mejora/venta, aislamiento de cuenta, controles y botín.");
            GetTree().Quit();
        }
        catch (Exception error)
        { CaptureDefense("00-error"); GD.PrintErr("DEFENSE_SMOKE_FAILED: " + error); GetTree().Quit(1); }
        return true;
    }
}
