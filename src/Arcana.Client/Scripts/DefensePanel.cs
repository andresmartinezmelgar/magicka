using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Estadísticas y compras de la partida; nunca utiliza AccountClient ni escribe en Firestore.</summary>
public partial class DefensePanel : Control
{
    public bool ShopOpen => _shop?.Visible == true;
    public event Action? OpenChanged;
    private Action<ClientMessage> _submit = null!;
    private Main _main = null!;
    private EquipmentPanel? _equipmentInventory;
    internal EquipmentPanel? EquipmentInventory => _equipmentInventory;
    private PanelContainer _summary = null!, _window = null!;
    private Control _shop = null!;
    private Label _metrics = null!, _stats = null!, _wallet = null!, _result = null!, _rules = null!;
    private Button _restart = null!;
    private Label _bonuses = null!;
    private bool _ended;
    private readonly List<Button> _auras = [], _passives = [], _tabs = [];
    private readonly List<Label> _auraHints = [], _passiveHints = [];
    private readonly List<VBoxContainer> _sections = [];
    private Label _shopStatus = null!, _equipmentSummary = null!;
    private ContentScrollContainer _scroll = null!;
    private VBoxContainer _auraSection = null!, _passiveSection = null!;
    private Button _auraTab = null!, _passiveTab = null!;
    private DefensePlayerSnapshot? _player;
    private DefenseSnapshot? _state;
    private WizardSnapshot? _wizard;
    private string _totalsStamp = "";
    private GridContainer _totals = null!;
    private GridContainer _equipmentTotals = null!;
    private Label _equipmentTotalsEmpty = null!;
    private string _equipmentTotalsStamp = "";
    private bool _canRestart;
    private GridContainer _navigation = null!;
    private WorldSnapshot? _lastSnapshot;
    private int _lastPlayerId;
    private bool _lastOnline, _lastRewardsAvailable, _lastShopOpen;
    private (int Level, int Experience, int Next, int Gold, int Kills, int Lives)? _summaryState;
    private BuffSnapshot[]? _lastBonuses;
    public bool PointerOverPanel => _summary.GetGlobalRect().HasPoint(GetGlobalMousePosition()) || ShopOpen;

    public void Build(Main main, Action<ClientMessage> submit)
    {
        _main = main; _submit = submit;
        Name = "DefensePanel"; Theme = Ui.Theme(); Ui.Full(this); MouseFilter = MouseFilterEnum.Ignore;
        _summary = new PanelContainer { Name = "DefenseSummary" }; AddChild(_summary);
        _summary.AddThemeStyleboxOverride("panel", Ui.Panel(new("0b192b"), 7, new("56748f")));
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 5); _summary.AddChild(stack);
        _stats = Ui.Label("", Ui.IsMobile ? 13 : 15); _stats.AutowrapMode = TextServer.AutowrapMode.WordSmart; _stats.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 13 : 15); stack.AddChild(_stats);
        _metrics = Ui.Label("", 12, Ui.Muted); _metrics.AutowrapMode = TextServer.AutowrapMode.WordSmart; _metrics.AddThemeFontSizeOverride("font_size", 12); stack.AddChild(_metrics);
        var bonusScroll = new ContentScrollContainer { Name = "DefenseBonusScroll", CustomMinimumSize = new(0, 112),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        stack.AddChild(bonusScroll); Ui.VisibleVerticalScroll(bonusScroll);
        _bonuses = Ui.Label("Sin bonificaciones", 13, Ui.Gold); _bonuses.Name = "DefenseVerticalBonuses";
        _bonuses.AutowrapMode = TextServer.AutowrapMode.WordSmart; _bonuses.SizeFlagsHorizontal = SizeFlags.ExpandFill; bonusScroll.AddChild(_bonuses);
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
        var title = Ui.Label("SUPERVIVENCIA · EQUIPO DE PARTIDA", Ui.IsMobile ? 19 : 23, Ui.Gold);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill; title.AutowrapMode = TextServer.AutowrapMode.WordSmart; header.AddChild(title);
        header.AddChild(Ui.Button("Cerrar", CloseShop));
        _wallet = Ui.Label("", 16); _wallet.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_wallet);
        _shopStatus = Ui.Label("", 14, Ui.Health); _shopStatus.Name = "DefenseShopStatus";
        _shopStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_shopStatus);
        _navigation = new GridContainer { Columns = 5 }; _navigation.AddThemeConstantOverride("h_separation", 4);
        _navigation.AddThemeConstantOverride("v_separation", 4); body.AddChild(_navigation);
        string[] names = ["Partida", "Equipo", "Poderes", "Estadísticas", "Guía"];
        for (int i = 0; i < names.Length; i++)
        {
            int section = i;
            var tab = Ui.Button(names[i], () => ShowSection(section)); tab.Name = "DefenseTab" + i;
            tab.SizeFlagsHorizontal = SizeFlags.ExpandFill; tab.CustomMinimumSize = new(0, Ui.IsMobile ? 52 : 44);
            tab.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 17 : 16); _navigation.AddChild(tab); _tabs.Add(tab);
        }
        _scroll = new ContentScrollContainer { Name = "DefenseShopScroll", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; body.AddChild(_scroll); Ui.VisibleVerticalScroll(_scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _scroll.AddChild(content);
        for (int i = 0; i < names.Length; i++)
        {
            var section = new VBoxContainer { Name = "DefenseSection" + i, SizeFlagsHorizontal = SizeFlags.ExpandFill, Visible = i == 0 };
            section.AddThemeConstantOverride("separation", 10); content.AddChild(section); _sections.Add(section);
        }
        var survival = _sections[0];
        _result = Wrapped(survival, "", 18, Ui.Gold);
        Wrapped(survival, "Tienes dos vidas: tras la primera muerte reapareces; la segunda te elimina. En cooperativo la partida sigue mientras quede un humano con vidas.", 16);
        Wrapped(survival, "Recorre Anillos por sus pasillos y usa los portales del mismo color para cruzar el mapa. Los enemigos te buscan desde cualquier zona, pero nunca aparecen dentro de un radio de 16 m a tu alrededor.", 16);
        Wrapped(survival, "Aparecen colosos a intervalos variables. Algunos minions aceleran durante unos segundos: vigila sus anillos azules.", 16, Ui.Muted);
        Wrapped(survival, "Cómo mejorar", 18, Ui.Gold);
        Wrapped(survival, "EQUIPO: recoge, equipa y mejora piezas.\nPODERES: compra auras y habilidades automáticas.\nESTADÍSTICAS: consulta los bonos y los premios por supervivencia.", 16);
        var equipment = _sections[1];
        _equipmentSummary = Wrapped(equipment, "", 16, Ui.Gold);
        equipment.AddChild(Ui.Button("Abrir inventario de partida", OpenEquipmentInventory));
        Wrapped(equipment, "Elige botas, gorro, pechera o arma. Compara las piezas por calidad y consulta sus atributos y bonificaciones. La selección se aplica al volver a la tienda.", 16);
        var powers = _sections[2];
        var powerNavigation = new HBoxContainer(); powers.AddChild(powerNavigation);
        _passiveTab = Ui.Button("Habilidades automáticas", () => ShowPowers(true));
        _auraTab = Ui.Button("Auras", () => ShowPowers(false));
        foreach (var tab in new[] { _passiveTab, _auraTab })
        { tab.SizeFlagsHorizontal = SizeFlags.ExpandFill; tab.CustomMinimumSize = new(0, 48); tab.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 17 : 16); powerNavigation.AddChild(tab); }
        _passiveSection = new VBoxContainer(); _passiveSection.AddThemeConstantOverride("separation", 10); powers.AddChild(_passiveSection);
        _auraSection = new VBoxContainer(); _auraSection.AddThemeConstantOverride("separation", 10); powers.AddChild(_auraSection);
        Wrapped(_auraSection, "AURAS · siempre activas a tu alrededor", 18, Ui.Gold);
        for (int i = 0; i < 3; i++)
        {
            int aura = i; var card = Card(_auraSection);
            _auraHints.Add(Wrapped(card, "", 16));
            var button = Ui.Button("", () => _submit(new() { Type = "defense_aura", Slot = aura }));
            button.Name = "DefenseAura" + i; card.AddChild(button); _auras.Add(button);
        }
        Wrapped(_passiveSection, "HABILIDADES · se activan solas en combate", 18, Ui.Gold);
        for (int i = 0; i < 4; i++)
        {
            int passive = i; var card = Card(_passiveSection);
            _passiveHints.Add(Wrapped(card, "", 16));
            var button = Ui.Button("", () => _submit(new() { Type = "defense_passive", Slot = passive }));
            button.Name = "DefensePassive" + i; card.AddChild(button); _passives.Add(button);
        }
        var statistics = _sections[3];
        Wrapped(statistics, "BONIFICACIONES DEL EQUIPO · POR PIEZA", 18, Ui.Gold);
        _equipmentTotals = Ui.Table(2, "DefenseEquipmentTotals"); statistics.AddChild(_equipmentTotals);
        _equipmentTotalsEmpty = Wrapped(statistics, "No hay bonificaciones activas en el equipo actual.", 15, Ui.Muted);
        Wrapped(statistics, "TOTAL EFECTIVO EN PARTIDA · equipo, nivel y mejoras", 18, Ui.Gold);
        _totals = Ui.Table(2, "DefenseTotals"); statistics.AddChild(_totals);
        _rules = Wrapped(statistics, "", 15, Ui.Muted);
        BuildGuide(_sections[4]);
        ShowSection(0); ShowPowers(true);
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 8); body.AddChild(footer);
        _restart = Ui.Button("Otra partida", () => { Send("defense_restart"); CloseShop(); }); footer.AddChild(_restart);
        foreach (var button in new[] { open }.Concat(_auras).Concat(_passives))
        {
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 18 : 16);
        }
        Resize(); GetViewport().SizeChanged += Resize;
    }

    private static Label Wrapped(Control parent, string text, int size, Color? color = null)
    {
        var label = Ui.Label(text, size, color); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill; parent.AddChild(label); return label;
    }
    private static VBoxContainer Card(Control parent)
    {
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", Ui.Panel(new("0c192c"), 6, new("385571"))); parent.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 7); panel.AddChild(box); return box;
    }
    internal void ShowSection(int index)
    {
        for (int i = 0; i < _sections.Count; i++)
        {
            _sections[i].Visible = i == index;
            _tabs[i].AddThemeColorOverride("font_color", i == index ? Ui.Gold : new Color("edf3ff"));
        }
        _scroll.ScrollVertical = 0;
        if (index == 1) OpenEquipmentInventory();
        if (index == 3 && ShopOpen) UpdateTotals();
        if (index == 4) UpdateGuide();
    }
    internal void ShowPowers(bool automatic)
    {
        _passiveSection.Visible = automatic; _auraSection.Visible = !automatic;
        _passiveTab.AddThemeColorOverride("font_color", automatic ? Ui.Gold : Ui.Text);
        _auraTab.AddThemeColorOverride("font_color", automatic ? Ui.Text : Ui.Gold);
        _scroll.ScrollVertical = 0;
    }
    private string PriceLabel(int cost, bool max = false) => max ? "Nivel máximo" :
        _player != null && _player.Gold < cost ? $"Faltan {cost - _player.Gold} oro · coste {cost}" : $"Comprar / mejorar · {cost} oro";

    public void SetNetworkStats(string text) => _metrics.Text = text;
    internal Rect2 SummaryRectForVerification => _summary.GetGlobalRect();
    public bool BlocksTouch(Vector2 point) => _summary.GetGlobalRect().HasPoint(point) || ShopOpen;

    public void OpenShop() { _shop.Visible = true; OpenChanged?.Invoke(); }
    public void CloseShop()
    {
        if (_equipmentInventory != null) { _equipmentInventory.Close(FinishClosingShop); return; }
        FinishClosingShop();
    }
    private void FinishClosingShop() { _shop.Visible = false; OpenChanged?.Invoke(); }
    internal void HandleBack()
    {
        if (_equipmentInventory != null) _equipmentInventory.HandleBack();
        else CloseShop();
    }
    private void OpenEquipmentInventory()
    {
        if (!ShopOpen || _equipmentInventory != null || _player == null || _wizard == null) return;
        var inventory = new EquipmentPanel(); _equipmentInventory = inventory; _shop.AddChild(inventory);
        inventory.BuildDefense(_main, _player, _wizard, _ended, _submit);
        inventory.Closed += () => { if (_equipmentInventory == inventory) _equipmentInventory = null; };
    }
    private void Send(string type) => _submit(new() { Type = type });

    private void Resize()
    {
        var size = GetViewportRect().Size;
        float scale = Math.Min(1, size.Y / 720f);
        _summary.Scale = Vector2.One * scale;
        _summary.Size = new(296, 0);
        _summary.Position = new(22 * scale, 200 * scale);
        _window.CustomMinimumSize = new(Math.Min(790, size.X - 28), Math.Min(700, size.Y - 28));
        _window.Size = _window.CustomMinimumSize;
        _navigation.Columns = size.X < 660 ? 3 : 5;
    }

    public void Update(WorldSnapshot snapshot, int id, bool online, bool rewardsAvailable)
    {
        bool shopOpen = ShopOpen;
        if (ReferenceEquals(snapshot, _lastSnapshot) && id == _lastPlayerId && online == _lastOnline &&
            rewardsAvailable == _lastRewardsAvailable && shopOpen == _lastShopOpen) return;
        _lastSnapshot = snapshot; _lastPlayerId = id; _lastOnline = online;
        _lastRewardsAvailable = rewardsAvailable; _lastShopOpen = shopOpen;
        if (snapshot.Defense is not { } defense) return;
        _state = defense; _ended = snapshot.MatchEnded; _player = defense.Defenders.FirstOrDefault(p => p.PlayerId == id);
        _wizard = snapshot.Players.FirstOrDefault(p => p.Id == id);
        if (_player == null || _wizard == null) return;
        _canRestart = !online || snapshot.LobbyHostId == id;
        var p = _player;
        var summary = (p.Level, p.Experience, p.NextLevelExperience, p.Gold, p.Kills, p.LivesRemaining);
        if (_summaryState != summary)
        {
            _summaryState = summary;
            _stats.Text = $"Nivel {p.Level} · {p.Experience}/{p.NextLevelExperience} EXP\nOro {p.Gold} · Bajas {p.Kills} · Vidas {p.LivesRemaining}/2";
        }
        UpdateCollectedBonuses();
        _summary.Size = new(_summary.Size.X, 0);
        if (!shopOpen) return; // Las tarjetas ocultas se preparan al abrir su vista.
        int earned = Math.Min(4, (int)(p.ActiveSeconds / DefenseRules.RewardInterval));
        _wallet.Text = $"Oro: {p.Gold} · Nivel {p.Level} · Bajas {p.Kills} · Vidas {p.LivesRemaining}/2";
        _shopStatus.Text = snapshot.MatchEnded ? "PARTIDA TERMINADA" : defense.Paused
            ? "PARTIDA EN PAUSA · el combate y el tiempo se reanudan al cerrar la tienda."
            : defense.HumanCount > 1 ? "COMBATE EN CURSO · hay otros humanos en la partida."
            : "Al abrir la tienda se pausa la partida, incluidos los bots aliados.";
        _equipmentSummary.Text = $"{p.Items.Count(i => i.Equipped)}/4 ranuras equipadas · {p.Items.Length}/{DefenseRules.InventoryLimit} piezas temporales";
        bool buying = _wizard.Alive && !snapshot.MatchEnded;
        for (int i = 0; i < 3; i++)
        {
            int level = p.Auras[i];
            string effect = i < 2 ? $"{24 * Math.Max(1, level)} daño/s base" : $"{10 * Math.Max(1, level)} vida/s para ti y aliados";
            _auraHints[i].Text = DefenseRules.AuraName((DefenseAura)i) + $" · nivel {level}/{DefenseRules.MaxAuraLevel}\n" + effect +
                (level > 0 ? $" · radio {DefenseRules.AuraRadius(level):0.#} m" : " al comprar") +
                (level < DefenseRules.MaxAuraLevel ? $"\nSiguiente nivel: {(i < 2 ? 24 : 10) * (level + 1)}/s · radio {DefenseRules.AuraRadius(level + 1):0.#} m" : "");
            _auras[i].Text = PriceLabel(DefenseRules.AuraCost(level), level >= DefenseRules.MaxAuraLevel);
            _auras[i].Disabled = !buying || level >= DefenseRules.MaxAuraLevel || p.Gold < DefenseRules.AuraCost(level);
        }
        for (int i = 0; i < 4; i++)
        {
            int level = p.Passives[i]; var passive = (DefensePassive)i;
            string status = level == 0 ? "Sin comprar" : i == 1 && p.BerserkerRemaining > 0 ? $"ACTIVO: {p.BerserkerRemaining:0.#} s" :
                p.PassiveCooldowns[i] > 0 ? $"Recarga: {p.PassiveCooldowns[i]:0.#} s" : "Listo para el combate";
            _passiveHints[i].Text = DefenseRules.PassiveName(passive) + $" · nivel {level}/{DefenseRules.MaxPassiveLevel} · {status}\n" +
                DefenseRules.PassiveDescription(passive, Math.Min(DefenseRules.MaxPassiveLevel, level + 1)) + (level > 0 && level < DefenseRules.MaxPassiveLevel ? " (siguiente nivel)" : "");
            _passives[i].Text = PriceLabel(DefenseRules.PassiveCost(level), level >= DefenseRules.MaxPassiveLevel);
            _passives[i].Disabled = !buying || level >= DefenseRules.MaxPassiveLevel || p.Gold < DefenseRules.PassiveCost(level);
        }
        bool bossAlive = defense.Minions.Any(m => m.Kind == MinionKind.Boss);
        _result.Text = (snapshot.MatchEnded ? "Partida terminada" : $"Vidas {p.LivesRemaining}/2") +
            $" · {DefenseRules.Clock(defense.Elapsed)} · Dificultad {defense.Difficulty}\nColosos derrotados: {defense.BossesKilled} · " +
            (bossAlive ? "¡Coloso en el mapa!" : $"Próximo coloso en {Math.Ceiling(defense.NextBossIn)} s");
        _rules.Text = "Nivel, oro, piezas, mejoras y reliquias se conservan al morir y desaparecen al iniciar otra partida. Si eres el único humano, la tienda pausa el combate y el tiempo de los premios.\n" +
            (!online ? "Partida local: sin premios permanentes."
                : !rewardsAvailable ? "Los premios requieren una cuenta verificada. Los hitos registrados se guardan como pendientes hasta poder entregarlos."
                : $"Hitos: {earned}/4 · tiempo activo: {DefenseRules.Clock(p.ActiveSeconds)}. " +
                    (earned < 4 ? $"Siguiente objeto a los {(earned + 1) * 5} minutos activos." : "Límite de cuatro objetos alcanzado.")) +
            (snapshot.RewardStatus.Length > 0 ? "\n" + snapshot.RewardStatus : "");
        _restart.Visible = snapshot.MatchEnded; _restart.Disabled = !_canRestart;
        _restart.TooltipText = _canRestart ? "Reinicia toda la progresión temporal." : "Solo el anfitrión puede iniciar otra partida.";
        _equipmentInventory?.UpdateDefenseInventory(p, _wizard, snapshot.MatchEnded);
        if (_sections[3].Visible) UpdateTotals();
        if (_sections[4].Visible) UpdateGuide();
    }

    private void UpdateCollectedBonuses()
    {
        var buffs = _wizard!.Buffs ?? [];
        if (_lastBonuses != null && _lastBonuses.AsSpan().SequenceEqual(buffs)) return;
        _lastBonuses = buffs;
        _bonuses.Text = buffs.Length > 0 ? "MEJORAS RECOGIDAS\n" +
            string.Join("\n", buffs.Select(b => RelicBuffs.Name(b.Kind) + " ×" + b.Stacks)) : "Sin mejoras recogidas";
    }

    private void UpdateTotals()
    {
        if (_player == null || _wizard == null) return;
        var p = _player;
        var equipped = p.Items.Where(i => i.Equipped).ToArray();
        string equipmentStamp = string.Join('|', equipped.Select(i => i.Item.Id + ":" + i.Upgrades));
        if (equipmentStamp != _equipmentTotalsStamp || _equipmentTotals.GetChildCount() == 0)
        {
            _equipmentTotalsStamp = equipmentStamp;
            var loadout = new EquipmentLoadout();
            foreach (var item in equipped) loadout = loadout.With(item.Item.Slot, item.Item.Id);
            var summary = PublicEquipment.Prepare(new("", "", 0, 0, 0, loadout, equipped.Select(i => i.Item).ToArray()));
            _equipmentTotalsEmpty.Visible = !EquipmentPanel.RenderTotals(_equipmentTotals, summary.View.Stats, summary.View.Items);
        }
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
        int energyStacks = (_wizard.Buffs ?? []).Where(b => b.Kind == BuffKind.Energy).Sum(b => b.Stacks);
        if (energyStacks > 0) rows.Add(("Energía por reliquias", $"+{energyStacks * p.Modifiers.EnergyRegeneration:0.#} energía/s"));
        for (int i = 0; i < 3; i++) if (p.Auras[i] > 0)
            rows.Add((DefenseRules.AuraName((DefenseAura)i), $"Nivel {p.Auras[i]} · {DefenseRules.AuraRadius(p.Auras[i]):0.#} m"));
        for (int i = 0; i < 4; i++) if (p.Passives[i] > 0)
            rows.Add((DefenseRules.PassiveName((DefensePassive)i), $"Nivel {p.Passives[i]}"));
        if (rows.Count == 0) rows.Add(("Sin bonificaciones activas", "0"));
        string stamp = string.Join('|', rows.Select(r => r.Name + r.Value));
        if (stamp == _totalsStamp) return;
        _totalsStamp = stamp;
        foreach (var child in _totals.GetChildren()) { _totals.RemoveChild(child); child.QueueFree(); }
        Ui.TableCell(_totals, "Total efectivo", header: true, size: 15); Ui.TableCell(_totals, "Atributo", header: true, size: 15);
        foreach (var row in rows)
        { Ui.TableCell(_totals, row.Value, number: true, size: 15); Ui.TableCell(_totals, row.Name, size: 15); }
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Resize;
}
