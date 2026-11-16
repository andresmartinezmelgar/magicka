using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Seis piezas independientes, compartidas por la partida y el editor local.</summary>
internal partial class SurvivalEquipmentHud : Control
{
    internal static readonly Vector2 CellSize = new(64, 58);
    private readonly record struct ItemState(string Id, ItemTier? Tier, EquipmentFamily? Family, ItemAffix? Affix, int Level, bool Affordable);
    private sealed record Cell(Button Button, TextureRect Piece, TextureRect Effect, ElementIcon Element,
        EquipmentItemIndicators Indicators);
    private readonly Dictionary<EquipmentSlot, Cell> _cells = [];
    private readonly Dictionary<EquipmentSlot, ItemState> _states = [];
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    internal IReadOnlyDictionary<string, Button> Buttons => _buttons;
    private TouchSettings? _lastSettings;
    private Vector2 _lastScreen;
    private ScreenRect[] _lastOccupied = [];
    internal static string Key(EquipmentSlot slot) => "SurvivalEquipped_" + slot;
    internal Rect2[] ItemRectangles => _buttons.Values.Select(b => b.GetGlobalRect()).ToArray();
    internal bool Contains(Vector2 point) => IsVisibleInTree() && _buttons.Values.Any(b => b.IsVisibleInTree() && b.GetGlobalRect().HasPoint(point));

    // En pantallas estrechas la cabecera baja debajo de las utilidades, conservando
    // tres piezas a cada lado. El temporizador y las piezas comparten esta geometría.
    internal static Rect2 TimerRectangle(Vector2 screen)
    {
        float s = Math.Min(1, screen.Y / 720f), gap = ScreenPlacement.MinimumGap;
        float left = 332 * s + gap, right = screen.X - 382 * s - gap, top = Math.Max(8, 12 * s);
        if (right - left < 412 * s + 8 * gap) { right = screen.X - gap; top = 92 * s + gap; }
        float available = Math.Max(1, right - left);
        float width = Math.Min(240 * s, Math.Max(1, (available - 8 * gap) * .44f));
        return new(new((left + right - width) / 2, top), new(width, 84 * s));
    }

    internal void Build(Action<EquipmentSlot>? inspect = null)
    {
        Name = "SurvivalEquippedItems"; MouseFilter = MouseFilterEnum.Ignore; Ui.Full(this);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var button = new Button { Name = Key(slot), CustomMinimumSize = CellSize, Size = CellSize,
                FocusMode = FocusModeEnum.None, MouseFilter = inspect == null ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop };
            AddChild(button); _buttons.Add(Key(slot), button);
            if (inspect != null) button.Pressed += () => inspect(slot);
            var piece = EquipmentIcons.View(EquipmentIcons.Slot(slot), 0);
            piece.Position = new(6, 6); piece.Size = new(30, 24); button.AddChild(piece);
            var effect = EquipmentIcons.View(EquipmentIcons.Slot(slot), 0);
            effect.Name = "EquipmentExtraAttribute"; effect.Visible = false; effect.Modulate = Ui.Text;
            effect.Position = new(39, 27); effect.Size = new(20, 20); button.AddChild(effect);
            var element = new ElementIcon { Name = "EquipmentElement", Position = new(40, 6), Size = new(18, 18), CustomMinimumSize = Vector2.Zero, Visible = false };
            button.AddChild(element);
            var indicators = new EquipmentItemIndicators { MouseFilter = MouseFilterEnum.Ignore }; button.AddChild(indicators); Ui.Full(indicators);
            _cells.Add(slot, new(button, piece, effect, element, indicators));
        }
        Update(null);
    }

    internal void Update(DefensePlayerSnapshot? player)
    {
        foreach (var (slot, cell) in _cells)
        {
            var entry = player?.Items.FirstOrDefault(i => i.Equipped && i.Item.Slot == slot);
            var item = entry?.Item;
            var active = item == null ? null : ItemAffixes.Active(item.Affix, slot);
            bool canImprove = entry != null && ItemAffixes.CanUpgrade(entry.Item, entry.Upgrades);
            int price = canImprove ? DefenseRules.SurvivalUpgradeCost(entry!.Item.Tier, entry.Upgrades) : 0;
            var state = new ItemState(item?.Id ?? "", item?.Tier, item?.Family, active, entry?.Upgrades ?? 0, canImprove && player!.Gold >= price);
            if (_states.TryGetValue(slot, out var previous) && previous == state) continue;
            _states[slot] = state;
            Color quality = item == null ? Ui.Muted : EquipmentPanel.TierColor(item.Tier);
            cell.Piece.Modulate = item == null ? new Color(Ui.Muted, .35f) : EquipmentIcons.FamilyColor(item.Family);
            cell.Indicators.Update(item?.Tier, entry?.Upgrades ?? 0, state.Affordable);
            cell.Element.Value = item == null ? null : RingEffects.ElementFor(item.Family);
            cell.Element.Visible = cell.Element.Value != null;
            // El elemento y el atributo extra tienen huecos distintos. Un anillo
            // sin afijo no muestra un atributo ficticio por su habilidad básica.
            cell.Effect.Visible = active != null;
            if (active != null) cell.Effect.Texture = EquipmentIcons.Affix(active.Kind);
            foreach (string mode in new[] { "normal", "hover", "pressed" })
            {
                var background = new Color(mode == "normal" ? "101f33" : "213853").Lerp(quality, .10f);
                // La calidad permanece visible también bajo el dedo o el ratón.
                var style = Ui.Panel(background, 5, quality);
                style.ContentMarginLeft = style.ContentMarginRight = style.ContentMarginTop = style.ContentMarginBottom = 4;
                style.BorderWidthLeft = style.BorderWidthRight = style.BorderWidthTop = style.BorderWidthBottom = item == null ? 2 : 4;
                cell.Button.AddThemeStyleboxOverride(mode, style);
            }
            cell.Button.TooltipText = item == null ? EquipmentCatalogue.SlotName(slot) + " · Sin equipar."
                : EquipmentCatalogue.SlotName(slot) + " · " + EquipmentCatalogue.TierName(item.Tier) + " · " + EquipmentCatalogue.FamilyName(item.Family) +
                    $"\nMejoras: {entry!.Upgrades}/10" + (canImprove ? $" · Siguiente: {price} de oro" : " · Sin mejoras disponibles") + "\n" +
                    string.Join("\n", item.Bonuses.Where(b => EquipmentCatalogue.BonusActive(slot, b.Stat))
                        .Select(b => EquipmentCatalogue.StatName(b.Stat) + ": " + EquipmentCatalogue.Percent(b.Current))) + "\n" +
                    EquipmentPanel.ItemEffects(item, entry.Upgrades, player!.Modifiers) + "\nPulsa para ver la pieza y sus mejoras.";
        }
    }

    internal Dictionary<string, Rect2> Desired(Vector2 screen, float scale, TouchSettings settings, bool custom)
    {
        float s = Math.Min(1, screen.Y / 720f), gap = ScreenPlacement.MinimumGap;
        var timer = TimerRectangle(screen);
        float left = 332 * s + gap;
        float cellWidth = Math.Max(1, (timer.Position.X - left - 3 * gap) / 3);
        float headerScale = Math.Min(scale, cellWidth / CellSize.X);
        var cell = CellSize * headerScale;
        var result = new Dictionary<string, Rect2>(StringComparer.Ordinal);
        int index = 0;
        foreach (var key in _buttons.Keys)
        {
            var size = cell * settings.ControlScale(key);
            float x = index < 3 ? timer.Position.X - gap - (3 - index) * cell.X - (2 - index) * gap
                : timer.End.X + gap + (index - 3) * (cell.X + gap);
            var center = new Vector2(x + cell.X / 2, timer.Position.Y + cell.Y / 2);
            if (custom && settings.Positions.TryGetValue(key, out var saved)) center = new(saved.X * screen.X, saved.Y * screen.Y);
            result.Add(key, new(center - size / 2, size)); index++;
        }
        return result;
    }

    internal void Apply(IReadOnlyDictionary<string, Rect2> rectangles, Vector2 globalOrigin)
    {
        foreach (var (key, button) in _buttons)
            if (rectangles.TryGetValue(key, out var rect))
            {
                button.Scale = Vector2.One * (rect.Size.X / CellSize.X); button.Size = CellSize;
                button.GlobalPosition = globalOrigin + rect.Position;
            }
    }

    internal void LayoutStandalone(TouchSettings settings, Vector2 screen, IReadOnlyList<ScreenRect> occupied)
    {
        if (ReferenceEquals(settings, _lastSettings) && screen == _lastScreen && _lastOccupied.SequenceEqual(occupied)) return;
        _lastSettings = settings; _lastScreen = screen; _lastOccupied = occupied.ToArray();
        for (float fit = settings.LayoutScale; fit >= .45f; fit = fit <= .45f ? 0 : Math.Max(.45f, fit - .05f))
        {
            var placed = new Dictionary<string, Rect2>(StringComparer.Ordinal); var taken = occupied.ToList();
            foreach (var (key, wanted) in Desired(screen, Math.Min(1, screen.Y / 720f) * settings.Size * fit, settings, true))
            {
                if (!ScreenPlacement.TryPlace(TouchHudLayout.Rect(wanted), new(0, 0, screen.X, screen.Y), taken, out var safe)) break;
                placed.Add(key, TouchHudLayout.Rect(safe)); taken.Add(safe);
            }
            if (placed.Count != _buttons.Count) continue;
            Apply(placed, Vector2.Zero); return;
        }
    }
}
