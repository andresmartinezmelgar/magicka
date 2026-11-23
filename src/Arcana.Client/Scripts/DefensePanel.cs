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
    private ElementTextLabel _metrics = null!, _stats = null!, _wallet = null!, _result = null!, _rules = null!;
    private Button _restart = null!;
    private ElementTextLabel _bonuses = null!;
    private bool _ended;
    private readonly List<Button> _auras = [], _passives = [];
    private readonly List<ElementTextLabel> _auraHints = [], _passiveHints = [];
    private readonly List<VBoxContainer> _sections = [];
    private ElementTextLabel _shopStatus = null!, _equipmentSummary = null!, _noBotInventories = null!;
    private VBoxContainer _botInventories = null!;
    private string _botInventoryRoster = "";
    private ContentScrollContainer _scroll = null!;
    private VBoxContainer _auraSection = null!, _passiveSection = null!;
    private Button _auraTab = null!, _passiveTab = null!;
    private DefensePlayerSnapshot? _player;
    private DefenseSnapshot? _state;
    private WizardSnapshot? _wizard;
    private string _totalsStamp = "";
    private GridContainer _totals = null!;
    private GridContainer _progressTotals = null!;
    private string _progressTotalsStamp = "";
    private GridContainer _equipmentTotals = null!;
    private ElementTextLabel _equipmentTotalsEmpty = null!;
    private string _equipmentTotalsStamp = "";
    private bool _canRestart;
    private GridContainer _navigation = null!;
    private Button _sectionBack = null!;
    private ElementTextLabel _shopTitle = null!;
    private int _sectionIndex = -1;
    private int _powerBotId;
    private const int PowersSection = 2;
    private const int BotEquipmentSection = 7;
    private static readonly string[] SectionNames = ["Partida", "Equipo", "Poderes", "Estadísticas", "Guía", "Reliquias", "Configuración predeterminada", "Bots aliados"];
    private WorldSnapshot? _lastSnapshot;
    private int _lastPlayerId;
    private bool _lastOnline, _lastRewardsAvailable, _lastShopOpen;
    private (int Level, int Experience, int Next, int Gold, int Kills, int Lives, int Momentum)? _summaryState;
    private BuffSnapshot[]? _lastBonuses;
    public bool PointerOverPanel => _summary.GetGlobalRect().HasPoint(GetGlobalMousePosition()) || EquippedHud.Contains(GetGlobalMousePosition()) || ShopOpen;

    public void Build(Main main, Action<ClientMessage> submit)
    {
        _main = main; _submit = submit;
        Name = "DefensePanel"; Theme = Ui.Theme(); Ui.Full(this); MouseFilter = MouseFilterEnum.Ignore;
        _summary = new PanelContainer { Name = "DefenseSummary" }; AddChild(_summary);
        var summaryStyle = Ui.Panel(new("0b192b"), 7, new("56748f"));
        summaryStyle.ContentMarginLeft = summaryStyle.ContentMarginRight = 10;
        summaryStyle.ContentMarginTop = summaryStyle.ContentMarginBottom = 10;
        _summary.AddThemeStyleboxOverride("panel", summaryStyle);
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 5); _summary.AddChild(stack);
        _stats = Ui.Label("", Ui.IsMobile ? 13 : 15); _stats.AutowrapMode = TextServer.AutowrapMode.WordSmart; _stats.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 13 : 15); stack.AddChild(_stats);
        _metrics = Ui.Label("", 12, Ui.Muted); _metrics.AutowrapMode = TextServer.AutowrapMode.WordSmart; _metrics.AddThemeFontSizeOverride("font_size", 12); stack.AddChild(_metrics);
        BuildEquippedStrip();
                var open = Ui.Button("Equipo / tienda", OpenShop); open.Name = "DefenseShopButton";
        open.CustomMinimumSize = new(0, Ui.IsMobile ? 46 : 40); stack.AddChild(open);
        _shop = new Control { Name = "DefenseShop", Visible = false }; AddChild(_shop); Ui.Full(_shop);
        var shade = new ColorRect { Color = Ui.ModalDim, MouseFilter = MouseFilterEnum.Stop };
        _shop.AddChild(shade); Ui.Full(shade);
        var center = new CenterContainer(); _shop.AddChild(center); Ui.Full(center);
        _window = new PanelContainer { Name = "DefenseShopWindow" }; center.AddChild(_window);
        _window.AddThemeStyleboxOverride("panel", Ui.ModalPanel());
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 8); _window.AddChild(body);
        var header = new HBoxContainer(); body.AddChild(header);
        _sectionBack = Ui.ExitButton("Atrás", HandleBack, "DefenseShopBack"); header.AddChild(_sectionBack);
        _shopTitle = Ui.Label("SUPERVIVENCIA", Ui.IsMobile ? 19 : 23, Ui.Gold);
        _shopTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _shopTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart; header.AddChild(_shopTitle);
        header.AddChild(Ui.ExitButton("Continuar", CloseShop, "DefenseShopClose"));
        _wallet = Ui.Label("", 16); _wallet.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_wallet);
        _shopStatus = Ui.Label("", 14, Ui.Health); _shopStatus.Name = "DefenseShopStatus";
        _shopStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_shopStatus);
        _scroll = new ContentScrollContainer { Name = "DefenseShopScroll", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; body.AddChild(_scroll); Ui.VisibleVerticalScroll(_scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _scroll.AddChild(content);
        _navigation = new GridContainer { Name = "DefenseMenu", Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _navigation.AddThemeConstantOverride("h_separation", 10);
        _navigation.AddThemeConstantOverride("v_separation", 10); content.AddChild(_navigation);
        for (int i = 0; i < SectionNames.Length; i++)
        {
            int section = i;
            var tab = Ui.ExitButton(SectionNames[i], () => ShowSection(section), "DefenseTab" + i);
            tab.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            tab.CustomMinimumSize = new(0, 54);
            tab.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 18 : 17);
            tab.SizeFlagsHorizontal = SizeFlags.ExpandFill; _navigation.AddChild(tab);
        }
        for (int i = 0; i < SectionNames.Length; i++)
        {
            var section = new VBoxContainer { Name = "DefenseSection" + i, SizeFlagsHorizontal = SizeFlags.ExpandFill, Visible = false };
            section.AddThemeConstantOverride("separation", 10); content.AddChild(section); _sections.Add(section);
        }
        _bonuses = Wrapped(_sections[5], "Sin mejoras recogidas", 18, Ui.Gold);
        _bonuses.Name = "DefenseCollectedRelics";
        Wrapped(_sections[5], "Las reliquias recogidas se conservan durante esta partida. Sus efectos están separados del equipo.", 16, Ui.Muted);
        var survival = _sections[0];
        _result = Wrapped(survival, "", 18, Ui.Gold);
        Wrapped(survival, "Tienes dos vidas: tras la primera muerte reapareces; la segunda te elimina. En cooperativo la partida sigue mientras quede un humano con vidas.", 16);
        Wrapped(survival, "Recorre el mapa por sus pasillos y usa los portales del mismo color para cruzar el mapa. Los enemigos te buscan desde cualquier zona, pero nunca aparecen dentro de un radio de 16 m a tu alrededor.", 16);
        Wrapped(survival, "Aparecen colosos a intervalos variables. Algunos minions aceleran durante unos segundos: vigila sus anillos azules.", 16, Ui.Muted);
        Wrapped(survival, "Cómo mejorar", 18, Ui.Gold);
        Wrapped(survival, "EQUIPO: recoge, equipa y mejora piezas.\nPODERES: compra auras y habilidades automáticas.\nESTADÍSTICAS: consulta los bonos y los premios por supervivencia.", 16);
        var equipment = _sections[1];
        _equipmentSummary = Wrapped(equipment, "", 16, Ui.Gold);
        _equipmentSummary.Name = "DefenseEquipmentSummary";
        equipment.AddChild(Ui.Button("Abrir inventario de partida", OpenEquipmentInventory));
        Wrapped(equipment, "Elige botas, gorro, pechera, arma y los dos anillos. Compara las piezas por calidad y consulta sus atributos y bonificaciones. La selección se aplica al volver a la tienda.", 16);
        _botInventories = _sections[BotEquipmentSection];
        _noBotInventories = Wrapped(_botInventories, "No hay bots aliados vivos con inventario disponible.", 16, Ui.Muted);
        var powers = _sections[PowersSection];
        var powerNavigation = new HBoxContainer(); powers.AddChild(powerNavigation);
        _passiveTab = Ui.Button("Habilidades automáticas", () => ShowPowers(true));
        _auraTab = Ui.Button("Auras", () => ShowPowers(false));
        foreach (var tab in new[] { _passiveTab, _auraTab })
        { tab.SizeFlagsHorizontal = SizeFlags.ExpandFill; tab.CustomMinimumSize = new(0, 48); tab.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 17 : 16); powerNavigation.AddChild(tab); }
        _passiveSection = new VBoxContainer(); _passiveSection.AddThemeConstantOverride("separation", 10); powers.AddChild(_passiveSection);
        _auraSection = new VBoxContainer(); _auraSection.AddThemeConstantOverride("separation", 10); powers.AddChild(_auraSection);
        Wrapped(_auraSection, "AURAS · siempre activas alrededor del mago", 18, Ui.Gold);
        for (int i = 0; i < DefenseRules.AuraCount; i++)
        {
            int aura = i; var card = Card(_auraSection);
            _auraHints.Add(Wrapped(card, "", 16));
            var button = Ui.Button("", () => BuyPower(automatic: false, slot: aura));
            button.Name = "DefenseAura" + i; card.AddChild(button); _auras.Add(button);
        }
        Wrapped(_passiveSection, "HABILIDADES · se activan solas en combate", 18, Ui.Gold);
        for (int i = 0; i < 4; i++)
        {
            int passive = i; var card = Card(_passiveSection);
            _passiveHints.Add(Wrapped(card, "", 16));
            var button = Ui.Button("", () => BuyPower(automatic: true, slot: passive));
            button.Name = "DefensePassive" + i; card.AddChild(button); _passives.Add(button);
        }
        var statistics = _sections[3];
        Wrapped(statistics, "BONIFICACIONES DEL EQUIPO · POR PIEZA", 18, Ui.Gold);
        _equipmentTotals = Ui.Table(2, "DefenseEquipmentTotals"); statistics.AddChild(_equipmentTotals);
        _equipmentTotalsEmpty = Wrapped(statistics, "No hay bonificaciones activas en el equipo actual.", 15, Ui.Muted);
        Wrapped(statistics, "MEJORAS DE PARTIDA · NIVEL, RELIQUIAS Y PODERES", 18, Ui.Gold);
        _progressTotals = Ui.Table(2, "DefenseProgressStatsTable"); statistics.AddChild(_progressTotals);
        Wrapped(statistics, "TOTAL EFECTIVO EN PARTIDA · equipo, nivel y mejoras", 18, Ui.Gold);
        _totals = Ui.Table(2, "DefenseTotals"); statistics.AddChild(_totals);
        _rules = Wrapped(statistics, "", 15, Ui.Muted);
        BuildGuide(_sections[4]);
        BuildEquipmentSetup(_sections[6]);
        ShowMenu(); ShowPowers(true);
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 8); body.AddChild(footer);
        _restart = Ui.Button("Otra partida", () => { Send("defense_restart"); CloseShop(); }); footer.AddChild(_restart);
        foreach (var button in new[] { open }.Concat(_auras).Concat(_passives))
        {
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 18 : 16);
        }
        Resize(); GetViewport().SizeChanged += Resize;
    }

    private static ElementTextLabel Wrapped(Control parent, string text, int size, Color? color = null)
    {
        var label = Ui.Label(text, size, color); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill; parent.AddChild(label); return label;
    }
    private static VBoxContainer Card(Control parent)
    {
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", Ui.Panel(new("0c192c"), 6, new("385571"))); parent.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 7); panel.AddChild(box); return box;
    }
    internal void ShowSection(int index, int botId = 0)
    {
        if (index < 0 || index >= _sections.Count) return;
        var bot = botId == 0 ? null : FindBotInventory(botId);
        if (botId != 0 && (index != PowersSection || bot == null)) return;
        _powerBotId = botId;
        _sectionIndex = index; _navigation.Visible = false; _sectionBack.Visible = true;
        _shopTitle.Text = bot is { } target ? $"PODERES · {target.Wizard.Name}" :
            "SUPERVIVENCIA · " + SectionNames[index].ToUpperInvariant();
        for (int i = 0; i < _sections.Count; i++) _sections[i].Visible = i == index;
        _scroll.ScrollVertical = 0;
        if (index == 1) OpenEquipmentInventory();
        if (index == 3 && ShopOpen) UpdateTotals();
        if (index == 4) UpdateGuide();
        if (index == BotEquipmentSection) UpdateBotInventories();
        if (botId != 0) ShowPowers(false);
        UpdateShopWalletAndPowers();
    }
    private void ShowMenu()
    {
        _powerBotId = 0;
        _sectionIndex = -1; _navigation.Visible = true;
        _shopTitle.Text = "SUPERVIVENCIA · MENÚ";
        foreach (var section in _sections) section.Visible = false;
        _scroll.ScrollVertical = 0;
        UpdateShopWalletAndPowers();
    }
    internal void ShowPowers(bool automatic)
    {
        _passiveSection.Visible = automatic; _auraSection.Visible = !automatic;
        _passiveTab.AddThemeColorOverride("font_color", automatic ? Ui.Gold : Ui.Text);
        _auraTab.AddThemeColorOverride("font_color", automatic ? Ui.Text : Ui.Gold);
        _scroll.ScrollVertical = 0;
    }
    private static string PriceLabel(int cost, int gold, bool max) => max ? "Nivel máximo" :
        gold < cost ? $"Faltan {cost - gold} oro · coste {cost}" : $"Comprar / mejorar · {cost} oro";

    internal Rect2 SummaryRectForVerification => _summary.GetGlobalRect();
    public bool BlocksTouch(Vector2 point) => _summary.GetGlobalRect().HasPoint(point) || EquippedHud.Contains(point) || ShopOpen;

}
