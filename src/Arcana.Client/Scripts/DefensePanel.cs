using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Estadísticas y compras de la partida; nunca utiliza AccountClient ni escribe en Firestore.</summary>
public partial class DefensePanel : Control
{
    public bool ShopOpen => _shop?.Visible == true;
    public event Action? OpenChanged;
    private Action<ClientMessage> _submit = null!;
    private PanelContainer _summary = null!, _window = null!;
    private Control _shop = null!;
    private Label _metrics = null!, _stats = null!, _wallet = null!, _detail = null!, _result = null!, _rules = null!;
    private Button _repair = null!, _supply = null!, _equip = null!, _upgrade = null!, _sell = null!, _restart = null!;
    private readonly List<Button> _auras = [];
    private ItemList _inventory = null!;
    private DefensePlayerSnapshot? _player;
    private DefenseSnapshot? _state;
    private WizardSnapshot? _wizard;
    private string _itemId = "", _inventoryStamp = "", _totalsStamp = "";
    private GridContainer _totals = null!;
    private bool _canRestart;
    private readonly List<string> _itemIds = [];
    public bool PointerOverPanel => _summary.GetGlobalRect().HasPoint(GetGlobalMousePosition()) || ShopOpen;

    public void Build(Action<ClientMessage> submit, Action leave)
    {
        _submit = submit;
        Name = "DefensePanel"; Theme = Ui.Theme(); Ui.Full(this); MouseFilter = MouseFilterEnum.Ignore;
        _summary = new PanelContainer { Name = "DefenseSummary" }; AddChild(_summary);
        _summary.AddThemeStyleboxOverride("panel", Ui.Panel(new("0b192b"), 7, new("56748f")));
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 5); _summary.AddChild(stack);
        _stats = Ui.Label("", Ui.IsMobile ? 13 : 15); _stats.AutowrapMode = TextServer.AutowrapMode.WordSmart; _stats.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 13 : 15); stack.AddChild(_stats);
        _metrics = Ui.Label("", 12, Ui.Muted); _metrics.AutowrapMode = TextServer.AutowrapMode.WordSmart; _metrics.AddThemeFontSizeOverride("font_size", 12); stack.AddChild(_metrics);
        var open = Ui.Button("Equipo / tienda", OpenShop); open.Name = "DefenseShopButton";
        open.CustomMinimumSize = new(0, Ui.IsMobile ? 46 : 40); stack.AddChild(open);
        _shop = new Control { Name = "DefenseShop", Visible = false }; AddChild(_shop); Ui.Full(_shop);
        var shade = new ColorRect { Color = new(0.01f, 0.025f, 0.045f, 0.94f), MouseFilter = MouseFilterEnum.Stop };
        _shop.AddChild(shade); Ui.Full(shade);
        var center = new CenterContainer(); _shop.AddChild(center); Ui.Full(center);
        _window = new PanelContainer { Name = "DefenseShopWindow" }; center.AddChild(_window);
        _window.AddThemeStyleboxOverride("panel", Ui.Panel(new("102039"), 9, Ui.Gold.Darkened(0.45f)));
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 8); _window.AddChild(body);
        var header = new HBoxContainer(); body.AddChild(header);
        var title = Ui.Label("DEFENSA · EQUIPO DE PARTIDA", Ui.IsMobile ? 19 : 23, Ui.Gold);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill; title.AutowrapMode = TextServer.AutowrapMode.WordSmart; header.AddChild(title);
        header.AddChild(Ui.Button("Cerrar", CloseShop));
        _wallet = Ui.Label("", 16); _wallet.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_wallet);
        var scroll = new ContentScrollContainer { Name = "DefenseShopScroll", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; body.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddThemeConstantOverride("separation", 9); scroll.AddChild(content);
        _result = Ui.Label("", 15, Ui.Gold); _result.AutowrapMode = TextServer.AutowrapMode.WordSmart; content.AddChild(_result);
        _repair = Ui.Button("", () => Send("defense_repair")); _repair.Name = "DefenseRepairButton"; content.AddChild(_repair);
        for (int i = 0; i < 3; i++)
        {
            int aura = i;
            var button = Ui.Button("", () => _submit(new() { Type = "defense_aura", Slot = aura }));
            button.Name = "DefenseAura" + i; button.CustomMinimumSize = new(0, Ui.IsMobile ? 52 : 44);
            content.AddChild(button); _auras.Add(button);
        }
        _supply = Ui.Button("Comprar pieza aleatoria · 100 oro", () => Send("defense_supply")); content.AddChild(_supply);
        content.AddChild(Ui.Label("INVENTARIO TEMPORAL · 24 piezas como máximo", 16, Ui.Gold));
        _inventory = new ItemList { Name = "DefenseInventory", CustomMinimumSize = new(0, 140), SelectMode = ItemList.SelectModeEnum.Single,
            SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddChild(_inventory);
        _inventory.ItemSelected += index => { _itemId = _itemIds[(int)index]; UpdateDetails(); };
        var detailPanel = new PanelContainer(); detailPanel.AddThemeStyleboxOverride("panel", Ui.Panel(new("091525"), 4, new("54718b"))); content.AddChild(detailPanel);
        _detail = Ui.Label("", 15); _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart; detailPanel.AddChild(_detail);
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 8); content.AddChild(actions);
        _equip = Ui.Button("Equipar", () => Send("defense_equip")); actions.AddChild(_equip);
        _upgrade = Ui.Button("Mejorar", () => Send("defense_upgrade")); actions.AddChild(_upgrade);
        _sell = Ui.Button("Vender", () => Send("defense_sell")); actions.AddChild(_sell);
        foreach (var button in new[] { _equip, _upgrade, _sell }) button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.AddChild(Ui.Label("SUMA DE BONIFICACIONES ACTIVAS", 16, Ui.Gold));
        _totals = Ui.Table(2, "DefenseTotals"); content.AddChild(_totals);
        _rules = Ui.Label("", 13, Ui.Muted); _rules.AutowrapMode = TextServer.AutowrapMode.WordSmart; content.AddChild(_rules);
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 8); body.AddChild(footer);
        _restart = Ui.Button("Otra partida", () => { Send("defense_restart"); CloseShop(); }); footer.AddChild(_restart);
        footer.AddChild(Ui.Button("Volver al menú", leave));
        foreach (var button in new[] { open, _repair, _supply, _equip, _upgrade, _sell }.Concat(_auras))
        {
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 18 : 16);
        }
        Resize(); GetViewport().SizeChanged += Resize;
    }

    public void SetNetworkStats(string text) => _metrics.Text = text;
    internal Rect2 SummaryRectForVerification => _summary.GetGlobalRect();
    public bool BlocksTouch(Vector2 point) => _summary.GetGlobalRect().HasPoint(point) || ShopOpen;

    public void OpenShop() { _shop.Visible = true; OpenChanged?.Invoke(); UpdateDetails(); }
    public void CloseShop() { _shop.Visible = false; OpenChanged?.Invoke(); }
    private void Send(string type) => _submit(new() { Type = type, ItemId = _itemId });

    private void Resize()
    {
        var size = GetViewportRect().Size;
        bool compact = Ui.IsMobile || size.Y < 700;
        float width = compact ? 205 : 250;
        float mobileScale = Math.Min(1, size.Y / 720f);
        _summary.Size = new(Ui.IsMobile ? 296 * mobileScale : width, 0);
        _summary.Position = Ui.IsMobile ? new(22 * mobileScale, 200 * mobileScale) : new(size.X - width - 16, compact ? 100 : 210);
        _window.CustomMinimumSize = new(Math.Min(790, size.X - 28), Math.Min(700, size.Y - 28));
        _window.Size = _window.CustomMinimumSize;
    }

    public void Update(WorldSnapshot snapshot, int id, bool online, bool rewardsAvailable)
    {
        if (snapshot.Defense is not { } defense) return;
        _state = defense; _player = defense.Defenders.FirstOrDefault(p => p.PlayerId == id);
        _wizard = snapshot.Players.FirstOrDefault(p => p.Id == id);
        if (_player == null || _wizard == null) return;
        _canRestart = !online || snapshot.LobbyHostId == id;
        var p = _player;
        int earned = Math.Min(4, (int)(p.ActiveSeconds / DefenseRules.RewardInterval));
        _stats.Text = Ui.IsMobile
            ? $"Nv. {p.Level} · Oro {p.Gold} · Bajas {p.Kills}\n" +
                $"Daño +{(p.DamageMultiplier - 1) * 100:0}% · Def {(1 - p.IncomingMultiplier) * 100:0}% · Vel +{(p.SpeedMultiplier - 1) * 100:0}%\n" +
                $"Regen {p.Regeneration:0.#}/s · EXP {p.Experience}/{p.NextLevelExperience}"
            : $"Nivel {p.Level} · {p.Experience}/{p.NextLevelExperience} EXP\nOro {p.Gold} · Bajas {p.Kills}\n" +
                $"Daño extra {(p.DamageMultiplier - 1) * 100:+0;-0;0} %\nReducción {(1 - p.IncomingMultiplier) * 100:0} %\n" +
                $"Velocidad extra {(p.SpeedMultiplier - 1) * 100:+0;-0;0} %\nRegeneración {p.Regeneration:0.#}/s";
        _wallet.Text = $"Oro: {p.Gold} · Nivel {p.Level} · Bajas {p.Kills} · Muertes {p.Deaths}";
        _repair.Text = $"Reparar bastión +{Math.Min(defense.CastleMaxHealth * 0.2f, defense.CastleMaxHealth - defense.CastleHealth):0} vida · {defense.RepairCost} oro";
        bool buying = _wizard.Alive && !snapshot.MatchEnded;
        _repair.Disabled = !buying || p.Gold < defense.RepairCost || defense.CastleHealth >= defense.CastleMaxHealth || V2.Distance(_wizard.Position, defense.CastlePosition) > 12;
        _repair.TooltipText = "Requiere estar a menos de 12 metros del bastión. El precio aumenta para todo el equipo.";
        for (int i = 0; i < 3; i++)
        {
            int level = p.Auras[i];
            string effect = i < 2 ? $"{24 * (level + 1)} daño/s base" : $"{10 * (level + 1)} vida/s base";
            _auras[i].Text = DefenseRules.AuraName((DefenseAura)i) + (level >= DefenseRules.MaxAuraLevel ? $" · nivel {level} · MÁXIMO"
                : $" {level} → {level + 1} · {effect} · {DefenseRules.AuraCost(level)} oro");
            _auras[i].Disabled = !buying || level >= DefenseRules.MaxAuraLevel || p.Gold < DefenseRules.AuraCost(level);
        }
        _supply.Disabled = !buying || p.Gold < 100 || p.Items.Length >= DefenseRules.InventoryLimit;
        _result.Text = snapshot.MatchEnded ? $"Bastión destruido. Tiempo: {DefenseRules.Clock(defense.Elapsed)} · Oleada {defense.Wave}."
            : $"Bastión: {defense.CastleHealth:0}/{defense.CastleMaxHealth:0} · Oleada {defense.Wave} · {DefenseRules.Clock(defense.Elapsed)}";
        _rules.Text = "Nivel, oro, piezas, mejoras y reliquias se conservan al morir y desaparecen al iniciar otra partida. La tienda no pausa el combate.\n" +
            (!online ? "Partida local: sin premios permanentes."
                : !rewardsAvailable ? "Servidor sin recompensas permanentes."
                : $"Hitos: {earned}/4 · tiempo activo: {DefenseRules.Clock(p.ActiveSeconds)}. " +
                    (earned < 4 ? $"Siguiente objeto a los {(earned + 1) * 5} minutos activos." : "Límite de cuatro objetos alcanzado.")) +
            (snapshot.RewardStatus.Length > 0 ? "\n" + snapshot.RewardStatus : "");
        _restart.Visible = snapshot.MatchEnded; _restart.Disabled = !_canRestart;
        _restart.TooltipText = _canRestart ? "Reinicia toda la progresión temporal." : "Solo el anfitrión puede iniciar otra partida.";
        string stamp = string.Join('|', p.Items.Select(i => $"{i.Item.Id}:{i.Upgrades}:{i.Equipped}"));
        if (stamp != _inventoryStamp)
        {
            _inventoryStamp = stamp; _inventory.Clear(); _itemIds.Clear();
            foreach (var i in p.Items)
            {
                _inventory.AddItem((i.Equipped ? "[E] " : "") + EquipmentCatalogue.SlotName(i.Item.Slot) + " · " +
                    EquipmentCatalogue.TierName(i.Item.Tier) + " · " + EquipmentCatalogue.FamilyName(i.Item.Family) + $" +{i.Upgrades}");
                _itemIds.Add(i.Item.Id);
            }
            if (!_itemIds.Contains(_itemId)) _itemId = _itemIds.FirstOrDefault() ?? "";
            int selected = _itemIds.IndexOf(_itemId); if (selected >= 0) _inventory.Select(selected);
        }
        UpdateDetails();
        UpdateTotals();
        _summary.Size = new(_summary.Size.X, 0);
    }

    private void UpdateDetails()
    {
        var selected = _player?.Items.FirstOrDefault(i => i.Item.Id == _itemId);
        bool buying = _wizard?.Alive == true && _state?.CastleHealth > 0;
        _equip.Disabled = !buying || selected == null || selected.Equipped;
        _upgrade.Disabled = !buying || selected == null || selected.Upgrades >= 10 ||
            selected.Item.Bonuses.All(b => b.Current >= b.Maximum) || _player!.Gold < DefenseRules.UpgradeCost(selected.Upgrades);
        _sell.Disabled = !buying || selected == null;
        _upgrade.Text = selected == null ? "Mejorar" : $"Mejorar · {DefenseRules.UpgradeCost(selected.Upgrades)} oro";
        _sell.Text = selected == null ? "Vender" : $"Vender · +{selected.Item.SaleGold} oro";
        var equipped = selected == null ? null : _player!.Items.FirstOrDefault(i => i.Equipped && i.Item.Slot == selected.Item.Slot);
        _detail.Text = selected == null ? "Recoge una pieza del suelo o compra suministros. Las piezas recogidas son exclusivas de esta partida."
            : EquipmentCatalogue.SlotName(selected.Item.Slot) + " · " + EquipmentCatalogue.TierName(selected.Item.Tier) + (selected.Equipped ? " · EQUIPADA" : "") + "\n" +
                string.Join("\n", selected.Item.Bonuses.Select(b => $"{EquipmentCatalogue.StatName(b.Stat)}: {b.Current * 100:+0.#;-0.#;0} %  [{b.Minimum * 100:0.#} a {b.Maximum * 100:0.#} %]" +
                    (!selected.Equipped ? $"  Δ {(b.Current - (equipped?.Item.Bonuses.FirstOrDefault(v => v.Stat == b.Stat)?.Current ?? 0)) * 100:+0.#;-0.#;0} puntos" : ""))) +
                "\nCada mejora suma 1 punto porcentual por atributo, hasta su máximo.";
    }


    private void UpdateTotals()
    {
        if (_player == null || _wizard == null) return;
        var p = _player;
        var rows = new List<(string Name, string Value)>();
        foreach (var stat in Enum.GetValues<EquipmentStat>())
        {
            float value = stat switch
            {
                EquipmentStat.Power => p.DamageMultiplier - 1,
                EquipmentStat.Defense => 1 - p.IncomingMultiplier,
                EquipmentStat.MovementSpeed => p.SpeedMultiplier - 1,
                EquipmentStat.HealingPower => p.Modifiers.Healing * (1 + (_wizard.Buffs ?? []).Where(b => b.Kind == BuffKind.Healing).Sum(b => b.Stacks) * 0.1f) - 1,
                _ => p.Modifiers.Bonus(stat)
            };
            if (Math.Abs(value) > 0.0001f) rows.Add((EquipmentCatalogue.StatName(stat), $"{value * 100:+0.#;-0.#;0} %"));
        }
        if (_wizard.MaxHealth > Wizard.BaseMaxHealth) rows.Add(("Vida máxima extra", $"+{_wizard.MaxHealth - Wizard.BaseMaxHealth:0}"));
        if (p.Regeneration > 0) rows.Add(("Regeneración propia", $"{p.Regeneration:0.#} vida/s"));
        for (int i = 0; i < 3; i++) if (p.Auras[i] > 0)
            rows.Add((DefenseRules.AuraName((DefenseAura)i), $"Nivel {p.Auras[i]} · {DefenseRules.AuraRadius(p.Auras[i]):0.#} m"));
        if (rows.Count == 0) rows.Add(("Sin bonificaciones activas", "0"));
        string stamp = string.Join('|', rows.Select(r => r.Name + r.Value));
        if (stamp == _totalsStamp) return;
        _totalsStamp = stamp;
        foreach (var child in _totals.GetChildren()) { _totals.RemoveChild(child); child.QueueFree(); }
        Ui.TableCell(_totals, "Atributo", header: true, size: 15); Ui.TableCell(_totals, "Total efectivo", header: true, size: 15);
        foreach (var row in rows)
        { Ui.TableCell(_totals, row.Name, size: 15); Ui.TableCell(_totals, row.Value, number: true, size: 15); }
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Resize;
}
