using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>
/// Opción A: marco visual único para todas las pantallas de menú.
/// Cabecera y navegación inferior se comparten en móvil y escritorio, también en subpantallas de ajustes.
/// No se instancia durante el combate; sus acciones reutilizan los controladores existentes.
/// </summary>
public partial class ArcanaMenuChrome : Control
{
    private readonly List<Button> _navigation = [];
    private Main _main = null!;
    private Label _player = null!;
    private double _refreshTimer;
    private MarginContainer? _reservedContent;
    private VBoxContainer _shell = null!;
    private Control _contentHost = null!;
    private GridContainer? _navigationGrid;
    private PanelContainer? _footer;
    private Label? _sectionLabel, _logoLabel;
    private bool _dynamicSection;
    private bool _profileScreen;

    internal Control ContentHost => _contentHost;

    public const int HeaderHeight = 68;
    public const int FooterHeight = 72;
    public static readonly Color Backdrop = new("050d1b");
    public static readonly Color Surface = new("0c1a2e");
    public static readonly Color SurfaceRaised = new("122743");
    public static readonly Color Border = new("2a709d");

    public static ArcanaMenuChrome Attach(Control root, MarginContainer content, Main main,
        string section, int selected, Action<int> navigate)
    {
        // El marco ocupa tres filas administradas por VBoxContainer: cabecera,
        // contenido flexible y navegación. No se posicionan con offsets absolutos.
        var chrome = new ArcanaMenuChrome { _reservedContent = content };
        root.AddChild(chrome);
        Ui.Full(chrome);
        chrome.MouseFilter = MouseFilterEnum.Ignore;
        chrome._main = main;
        chrome.Build(section, selected, navigate);
        // El host aísla el tamaño mínimo de los formularios: un hijo extenso
        // no debe aumentar el ancho de la navegación de toda la aplicación.
        content.Reparent(chrome._contentHost);
        Ui.Full(content);
        content.CustomMinimumSize = Vector2.Zero;
        chrome.GetViewport().SizeChanged += chrome.UpdateResponsive;
        chrome.UpdateResponsive();
        return chrome;
    }

    private void Build(string section, int selected, Action<int> navigate)
    {
        _shell = new VBoxContainer { Name = "ResponsiveMenuShell" };
        _shell.AddThemeConstantOverride("separation", 0);
        AddChild(_shell); Ui.Full(_shell);
        var header = new PanelContainer { Name = "OptionAHeader", MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(0, HeaderHeight), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin };
        header.AddThemeStyleboxOverride("panel", BarStyle());
        // Si un texto cambia, la barra nunca debe ocupar la zona de juego.
        header.ClipContents = true;
        _shell.AddChild(header);

        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", Ui.IsMobile ? 10 : 20);
        header.AddChild(heading);
        _logoLabel = Ui.Label("✦  ARCANA ARENA", Ui.IsMobile ? 24 : 27, Ui.Gold);
        heading.AddChild(_logoLabel);
        var line = new VSeparator();
        heading.AddChild(line);
        _dynamicSection = string.Equals(section, "Inicio", StringComparison.OrdinalIgnoreCase);
        _profileScreen = string.Equals(section, "Mi perfil", StringComparison.OrdinalIgnoreCase);
        _sectionLabel = Ui.Label(section.ToUpperInvariant(), Ui.IsMobile ? 17 : 20, Ui.Text);
        heading.AddChild(_sectionLabel);
        heading.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _player = Ui.Label("", Ui.IsMobile ? 13 : 16, Ui.Gold);
        _player.Name = "OptionAAccountSummary";
        _player.HorizontalAlignment = HorizontalAlignment.Right;
        _player.VerticalAlignment = VerticalAlignment.Center;
        // El texto del perfil no puede alterar el alto de la cabecera.
        // El bug anterior permitía un ancho casi nulo y una letra por fila,
        // agrandando la cabecera hasta tapar el launcher.
        _player.AutowrapMode = TextServer.AutowrapMode.Off;
        _player.ClipText = true;
        _player.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        _player.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _player.CustomMinimumSize = new Vector2(Ui.IsMobile ? 210 : 300, 44);
        heading.AddChild(_player);

        _contentHost = new Control { Name = "ResponsiveContentHost",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = Vector2.Zero,
            ClipContents = true };
        _shell.AddChild(_contentHost);
        var footer = new PanelContainer { Name = "OptionAFooter", MouseFilter = MouseFilterEnum.Stop };
        _footer = footer;
        footer.CustomMinimumSize = new Vector2(0, FooterHeight);
        footer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footer.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        footer.AddThemeStyleboxOverride("panel", BarStyle());
        _shell.AddChild(footer);
        var nav = new GridContainer { Columns = 8, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        _navigationGrid = nav;
        nav.AddThemeConstantOverride("h_separation", Ui.IsMobile ? 7 : 12);
        nav.AddThemeConstantOverride("v_separation", 7);
        footer.AddChild(nav);
        string[] pages = ["⌂  Inicio", "⚔  PvP", "✧  Versus IA", "♙  Perfil", "◇  Inventario", "?  Ayuda", "Salir", "Tienda"];
        for (int i = 0; i < pages.Length; i++)
        {
            int id = i;
            var action = Ui.Button(pages[i], () => navigate(id), false);
            action.Name = "OptionANav" + id;
            action.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            action.CustomMinimumSize = new(0, Ui.IsMobile ? 58 : 48);
            action.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 17 : 16);
            nav.AddChild(action);
            _navigation.Add(action);
        }
        nav.MoveChild(_navigation[7], 5);
        Select(selected);
        RefreshAccount();
    }

    private void UpdateResponsive()
    {
        if (_reservedContent == null || _footer == null || _navigationGrid == null) return;
        float viewportWidth = GetViewport().GetVisibleRect().Size.X;
        // La navegación no tiene scrollbar: descontar solo los márgenes reales
        // permite cuatro columnas en 640 px sin añadir una tercera fila que quite espacio al contenido.
        bool compactNav = Ui.IsMobile && viewportWidth <= 820;
        string[] fullNames = ["⌂  Inicio", "⚔  PvP", "✧  Versus IA", "♙  Perfil", "◇  Inventario", "?  Ayuda", "Salir", "Tienda"];
        string[] compactNames = ["Inicio", "PvP", "IA", "Perfil", "Equipo", "Ayuda", "Salir", "Tienda"];
        for (int i = 0; i < _navigation.Count; i++)
        {
            _navigation[i].Text = compactNav ? compactNames[i] : fullNames[i];
            _navigation[i].AddThemeFontSizeOverride("font_size", compactNav ? 16 : Ui.IsMobile ? 17 : 16);
        }
        int columns = ResponsiveLayout.Columns(Math.Max(1, viewportWidth - (Ui.IsMobile ? 20 : 36)),
            compactNav ? 148 : Ui.IsMobile ? 165 : 140, _navigation.Count, Ui.IsMobile ? 7 : 12);
        _navigationGrid.Columns = columns;
        int rows = ResponsiveLayout.Rows(_navigation.Count, columns);
        int height = Math.Max(FooterHeight, rows * (Ui.IsMobile ? 58 : 48) + (rows - 1) * 7 + 14);
        _footer.CustomMinimumSize = new Vector2(0, height);
        // VBoxContainer redistribuye todo el espacio vertical restante al contenido.
        _reservedContent.CustomMinimumSize = Vector2.Zero;
        if (_sectionLabel != null) _sectionLabel.Visible = viewportWidth >= 1120;
        if (_logoLabel != null) _logoLabel.Text = viewportWidth < 680 ? "✦ ARCANA" : "✦  ARCANA ARENA";
        // Los datos de cuenta son visibles también en móvil, alineados a la
        // derecha del encabezado. La sección se oculta antes si falta espacio.
        if (_player != null)
        {
            // Ancho explícito: nunca dejar que HBoxContainer encoja el perfil
            // a unos pocos píxeles y convierta su contenido en texto vertical.
            float width = Math.Clamp(viewportWidth * (Ui.IsMobile ? 0.31f : 0.28f),
                Ui.IsMobile ? 150 : 190, Ui.IsMobile ? 260 : 320);
            _player.CustomMinimumSize = new Vector2(width, 44);
            _player.Visible = !_profileScreen;
        }
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= UpdateResponsive;

    private static StyleBoxFlat BarStyle()
    {
        var style = Ui.Panel(new Color("091729"), 0, new Color("265a7e"));
        style.ContentMarginTop = style.ContentMarginBottom = 6;
        style.ContentMarginLeft = style.ContentMarginRight = Ui.IsMobile ? 10 : 18;
        return style;
    }

    public void Select(int page)
    {
        if (_dynamicSection && _sectionLabel != null)
            _sectionLabel.Text = page switch { 0 => "INICIO", 1 => "PVP", 2 => "VERSUS IA", 3 => "PERFIL", 4 => "INVENTARIO", 5 => "AYUDA", 7 => "TIENDA", _ => "MENÚ" };
        for (int i = 0; i < _navigation.Count; i++)
        {
            var btn = _navigation[i];
            var style = Ui.Panel(i == page ? new Color("19446b") : new Color("12263c"),
                7, i == page ? Ui.Blue : new Color("274667"));
            style.ContentMarginLeft = style.ContentMarginRight = 7;
            style.ContentMarginTop = style.ContentMarginBottom = 3;
            btn.AddThemeStyleboxOverride("normal", style);
            btn.AddThemeColorOverride("font_color", i == page ? Ui.Gold : Ui.Text);
        }
    }

    public void RefreshAccount()
    {
        var profile = _main.Accounts.Profile;
        bool synced = _main.Accounts.IsDirectFirebaseSession;
        bool google = _main.GoogleIdentityKnown || synced;
        bool compact = GetViewport().GetVisibleRect().Size.X < 720;
        string name = profile?.Name ?? (google ? _main.GoogleIdentityName : "Mago invitado");
        if (string.IsNullOrWhiteSpace(name)) name = "Mago";
        int maxLength = compact ? 9 : Ui.IsMobile ? (GetViewport().GetVisibleRect().Size.X < 1000 ? 10 : 14) : 22;
        if (name.Length > maxLength) name = name[..(maxLength - 1)] + "…";
        string state = synced ? (compact ? "G ✓" : "Google ✓")
            : google ? (compact ? "G !" : Ui.IsMobile ? "Google sin perfil" : "Google · sin sincronizar")
            : compact ? "Sin G" : "Sin Google";
        // Las victorias, derrotas y el oro se muestran solo en Perfil/Inventario;
        // evitar el mismo bloque de estadísticas en todas las pantallas.
        _player.Text = name + " · " + state;
        _player.TooltipText = google ? "Estado de la cuenta Google. Consulta los detalles en Perfil." : "Sesión de invitado";
    }

    public override void _Process(double delta)
    {
        _refreshTimer += delta;
        if (_refreshTimer < 1.5) return;
        _refreshTimer = 0;
        RefreshAccount();
    }
}
