using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>
/// Opción A: marco visual único para todas las pantallas de menú.
/// No se instancia durante el combate; sus acciones reutilizan los controladores existentes.
/// </summary>
public partial class ArcanaMenuChrome : Control
{
    private readonly List<Button> _navigation = [];
    private Main _main = null!;
    private Label _player = null!;
    private double _refreshTimer;
    private MarginContainer? _reservedContent;
    private float _originalContentTop, _originalContentBottom;
    private GridContainer? _navigationGrid;
    private PanelContainer? _footer;
    private Label? _sectionLabel, _logoLabel;

    public const int HeaderHeight = 68;
    public const int FooterHeight = 72;
    public static readonly Color Backdrop = new("050d1b");
    public static readonly Color Surface = new("0c1a2e");
    public static readonly Color SurfaceRaised = new("122743");
    public static readonly Color Border = new("2a709d");

    public static ArcanaMenuChrome Attach(Control root, MarginContainer content, Main main,
        string section, int selected, Action<int> navigate)
    {
        // Reservamos espacio real: la barra no se superpone a los menús, al HUD ni a los botones.
        var chrome = new ArcanaMenuChrome
        {
            _reservedContent = content,
            _originalContentTop = content.OffsetTop,
            _originalContentBottom = content.OffsetBottom
        };
        root.AddChild(chrome);
        Ui.Full(chrome);
        chrome.MouseFilter = MouseFilterEnum.Ignore;
        chrome._main = main;
        chrome.Build(section, selected, navigate);
        chrome.GetViewport().SizeChanged += chrome.UpdateResponsive;
        chrome.UpdateResponsive();
        return chrome;
    }

    private void Build(string section, int selected, Action<int> navigate)
    {
        var header = new PanelContainer { Name = "OptionAHeader", MouseFilter = MouseFilterEnum.Stop };
        header.AnchorRight = 1;
        header.OffsetBottom = HeaderHeight;
        header.AddThemeStyleboxOverride("panel", BarStyle());
        // Si un texto cambia, la barra nunca debe ocupar la zona de juego.
        header.ClipContents = true;
        AddChild(header);

        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", Ui.IsMobile ? 10 : 20);
        header.AddChild(heading);
        _logoLabel = Ui.Label("✦  ARCANA ARENA", Ui.IsMobile ? 24 : 27, Ui.Gold);
        heading.AddChild(_logoLabel);
        var line = new VSeparator();
        heading.AddChild(line);
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

        var footer = new PanelContainer { Name = "OptionAFooter", MouseFilter = MouseFilterEnum.Stop };
        _footer = footer;
        footer.AnchorTop = footer.AnchorBottom = footer.AnchorRight = 1;
        footer.OffsetTop = -FooterHeight;
        footer.AddThemeStyleboxOverride("panel", BarStyle());
        AddChild(footer);
        var nav = new GridContainer { Columns = 7, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        _navigationGrid = nav;
        nav.AddThemeConstantOverride("h_separation", Ui.IsMobile ? 7 : 12);
        nav.AddThemeConstantOverride("v_separation", 7);
        footer.AddChild(nav);
        string[] pages = ["⌂  Inicio", "⚔  PvP", "✧  Versus IA", "♙  Perfil", "◇  Inventario", "?  Ayuda", "Salir"];
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
        Select(selected);
        RefreshAccount();
    }

    private void UpdateResponsive()
    {
        if (_reservedContent == null || _footer == null || _navigationGrid == null) return;
        float viewportWidth = GetViewport().GetVisibleRect().Size.X;
        int columns = ResponsiveLayout.Columns(Math.Max(1, viewportWidth - (Ui.IsMobile ? 24 : 50)),
            Ui.IsMobile ? 110 : 108, _navigation.Count, Ui.IsMobile ? 7 : 12);
        _navigationGrid.Columns = columns;
        int rows = ResponsiveLayout.Rows(_navigation.Count, columns);
        int height = Math.Max(FooterHeight, rows * (Ui.IsMobile ? 58 : 48) + (rows - 1) * 7 + 14);
        _footer.OffsetTop = -height;
        _reservedContent.OffsetTop = _originalContentTop + HeaderHeight + 5;
        _reservedContent.OffsetBottom = _originalContentBottom - height - 5;
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
            _player.Visible = true;
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
        int maxLength = compact ? 9 : Ui.IsMobile ? 13 : 22;
        if (name.Length > maxLength) name = name[..(maxLength - 1)] + "…";
        string state = synced ? (compact ? "G ✓" : "Google ✓")
            : google ? (compact ? "G !" : "Google · sin sincronizar")
            : compact ? "Sin G" : "Sin Google";
        _player.Text = compact
            ? $"{name} · {state}\nV:{profile?.Wins ?? 0} · D:{profile?.Losses ?? 0}"
            : $"{name} · {state}\nV: {profile?.Wins ?? 0} · D: {profile?.Losses ?? 0} · {profile?.Gold ?? 0} oro";
    }

    public override void _Process(double delta)
    {
        _refreshTimer += delta;
        if (_refreshTimer < 1.5) return;
        _refreshTimer = 0;
        RefreshAccount();
    }
}
