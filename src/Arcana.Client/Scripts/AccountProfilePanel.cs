using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Pantalla de cuenta: una acción Google contextual y estado mínimo.</summary>
public partial class AccountProfilePanel : Control
{
    private Main _main = null!;
    private ElementTextLabel _heading = null!, _status = null!;
    private Button _login = null!, _logout = null!, _checkUpdate = null!, _touchControls = null!, _bestiary = null!, _statistics = null!;
    private ElementTextLabel _updateStatus = null!;
    private GridContainer _profileActions = null!;
    private bool _checkingUpdate;
    private string _updateIdentity = "";
    private bool _busy;
    private string _message = "";
    private readonly CancellationTokenSource _lifetime = new();
    private FileDialog? _googleFileDialog;
    private PanelContainer? _responsiveCard;
    private int _responsivePadding;

    internal Button[] VisibleFooterButtons => new[] { _login, _logout, _statistics, _checkUpdate, _touchControls, _bestiary }
        .Where(b => GodotObject.IsInstanceValid(b) && b.IsVisibleInTree()).ToArray();

    public void Build(Main main)
    {
        _main = main;
        Name = "AccountProfilePanel";
        Theme = Ui.Theme();
        Ui.Full(this);
        var shade = new ColorRect { Color = ArcanaMenuChrome.Backdrop };
        AddChild(shade);
        Ui.Full(shade);

        var margin = new MarginContainer();
        AddChild(margin);
        Ui.Full(margin);
        int padding = Ui.IsMobile ? 12 : 24;
        _responsivePadding = padding;
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, padding);

        var scroll = new ContentScrollContainer { Name = "ProfileContentScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        margin.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
        var center = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.AddChild(center);
        var card = new PanelContainer
        {
            Name = "GoogleAccountCard",
            CustomMinimumSize = new Vector2(Math.Max(300, Math.Min(Ui.IsMobile ? 1050 : 860,
                GetViewport().GetVisibleRect().Size.X - 2 * padding)), 0)
        };
        _responsiveCard = card;
        card.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("10263f"), 12, new Color("3375a6")));
        center.AddChild(card);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", Ui.IsMobile ? 14 : 20);
        card.AddChild(content);
        content.AddChild(Ui.Label("MI PERFIL  /  GOOGLE", Ui.IsMobile ? 20 : 19, Ui.Gold));
        _heading = Ui.Label("", Ui.IsMobile ? 27 : 25, Ui.Text);
        _heading.HorizontalAlignment = HorizontalAlignment.Center;
        _heading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_heading);
        _status = Ui.Label("", Ui.IsMobile ? 18 : 16, Ui.Muted);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(_status);

        _login = Ui.Button("Entrar con Google", () => { if (!_busy) _main.StartGoogleLogin(); }, true);
        _logout = Ui.Button("Cerrar sesión", () => _ = Logout(), true);
        _checkUpdate = Ui.Button("Buscar actualización", () => _ = CheckUpdate());
        _checkUpdate.Name = "ProfileCheckUpdates";
        _bestiary = Ui.Button("Bestiario y bajas", main.OpenBestiary);
        _bestiary.Name = "ProfileBestiary";
        _statistics = Ui.Button("Estadísticas de partidas", main.OpenMatchStatistics);
        _statistics.Name = "ProfileMatchStatistics";
        _statistics.TooltipText = "Gráficas de las partidas guardadas en este dispositivo.";
        _touchControls = Ui.Button("Configurar joystick y botones", main.OpenTouchSettings);
        _touchControls.Name = "ProfileTouchControls";
        _touchControls.TooltipText = "Tamaño, posiciones, zona muerta, sensibilidad y modo zurdo. Se guardan en este dispositivo.";
        _profileActions = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _profileActions.AddThemeConstantOverride("h_separation", 10); _profileActions.AddThemeConstantOverride("v_separation", 8);
        content.AddChild(_profileActions);
        foreach (var button in new[] { _login, _logout, _statistics, _checkUpdate, _touchControls, _bestiary })
        {
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 18 : 19);
            Ui.TouchTarget(button, 76);
            _profileActions.AddChild(button);
        }
        _updateStatus = Ui.Label("", Ui.IsMobile ? 17 : 16, Ui.Muted);
        _updateStatus.Name = "ProfileUpdateStatus";
        _updateStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _updateStatus.HorizontalAlignment = HorizontalAlignment.Center; content.AddChild(_updateStatus);
        ArcanaMenuChrome.Attach(this, margin, main, "Mi perfil", 3, main.NavigateOptionAMenu);
        _message = main.GoogleLoginStatus;
        GetViewport().SizeChanged += ResizeProfileCard;
        ResizeProfileCard();
        Render();
    }

    private void ResizeProfileCard()
    {
        if (_responsiveCard == null) return;
        float width = GetViewport().GetVisibleRect().Size.X;
        _responsiveCard.CustomMinimumSize = new Vector2(
            Math.Max(220, Math.Min(Ui.IsMobile ? 1050 : 860, width - 2 * _responsivePadding - 36)), 0);
        _profileActions.Columns = width < 560 ? 1 : 2;
        float height = GetViewport().GetVisibleRect().Size.Y < 640 ? 60 : 76;
        foreach (var button in new[] { _login, _logout, _statistics, _checkUpdate, _touchControls, _bestiary }) button.CustomMinimumSize = new(0, height);
    }

    /// <summary>Selecciona el JSON OAuth sin copiarlo ni renombrarlo manualmente.</summary>
    public void OpenDesktopGoogleConfiguration()
    {
        if (_lifetime.IsCancellationRequested || !IsInsideTree()) return;
        if (!GodotObject.IsInstanceValid(_googleFileDialog))
        {
            _googleFileDialog = new FileDialog
            {
                Title = "Seleccionar JSON OAuth (Aplicación de escritorio)",
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Filters = new[] { "*.json ; Credenciales OAuth de Google (*.json)" }
            };
            AddChild(_googleFileDialog);
            _googleFileDialog.FileSelected += path => _main.ImportDesktopGoogleConfiguration(path);
        }
        _googleFileDialog!.PopupCentered(new Vector2I(880, 540));
    }

    public void UpdateGoogleStatus(string message, bool busy = false)
    {
        if (_lifetime.IsCancellationRequested || !IsInsideTree()) return;
        _message = message;
        _busy = busy;
        Render();
    }

    public void RefreshStatisticsAccess() => Render();

    private void Render()
    {
        if (!IsInsideTree()) return;
        _statistics.Visible = _main.CanViewMatchStatistics;
        bool google = _main.GoogleIdentityKnown || _main.Accounts.SignedIn;
        bool firestoreError = _message.StartsWith("Error al leer Firestore:", StringComparison.Ordinal);
        _heading.Text = google
            ? "Google conectado" + (_main.GoogleIdentityName.Length > 0 ? " · " + _main.GoogleIdentityName : "")
            : "Conecta tu cuenta de Google";
        _status.Text = firestoreError
            ? _message.Contains("403", StringComparison.Ordinal) || _message.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase)
                ? "Firestore: error 403. Google está conectado, pero faltan permisos de lectura en Firebase."
                : "Google está conectado, pero no se ha podido sincronizar Firestore. " + _message
            : _main.Accounts.SignedIn
                ? "Cuenta e inventario sincronizados con Firebase."
                : google ? "Google autenticado. Esperando datos de Firebase."
                : _message.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ||
                  _message.Contains("configur", StringComparison.OrdinalIgnoreCase) ||
                  _message.Contains("JSON OAuth", StringComparison.OrdinalIgnoreCase)
                    ? _message
                    : "Inicia sesión para sincronizar tu perfil.";
        _login.Visible = !google;
        _login.Text = _main.DesktopGoogleNeedsConfiguration ? "Configurar y entrar con Google" : "Entrar con Google";
        _logout.Visible = google;
        _login.Disabled = _busy;
        _logout.Disabled = _busy;
        _checkUpdate.Disabled = _checkingUpdate;
        _checkUpdate.Text = _checkingUpdate ? "Buscando…" : "Buscar actualización";
        if (!_checkingUpdate && (_updateIdentity != _main.UpdateIdentityUid || _updateStatus.Text.Length == 0))
        {
            _updateIdentity = _main.UpdateIdentityUid;
            _updateStatus.Text = "Versión instalada: " + BuildInfo.Version;
        }
    }

    private async Task CheckUpdate()
    {
        if (_checkingUpdate || _lifetime.IsCancellationRequested) return;
        _checkingUpdate = true; _updateStatus.Text = "Buscando actualizaciones…"; Render();
        string message;
        try { message = await _main.CheckForUpdates(); }
        catch (Exception) { message = "No se ha podido comprobar la actualización."; }
        if (_lifetime.IsCancellationRequested || !IsInsideTree()) return;
        _checkingUpdate = false; _updateIdentity = _main.UpdateIdentityUid;
        _updateStatus.Text = message; Render();
    }

    private async Task Logout()
    {
        if (_busy) return;
        _busy = true;
        Render();
        try
        {
            await _main.StopGoogleSession();
            UpdateGoogleStatus("Sesión cerrada.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { UpdateGoogleStatus("No se pudo cerrar sesión: " + error.Message); }
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= ResizeProfileCard;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
