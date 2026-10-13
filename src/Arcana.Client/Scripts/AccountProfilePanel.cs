using Godot;

namespace Arcana.Client;

/// <summary>Pantalla de cuenta: una acción Google contextual y estado mínimo.</summary>
public partial class AccountProfilePanel : Control
{
    private Main _main = null!;
    private Label _heading = null!, _status = null!;
    private Button _login = null!, _logout = null!;
    private bool _busy;
    private string _message = "";
    private readonly CancellationTokenSource _lifetime = new();

    internal Button[] VisibleFooterButtons => new[] { _login, _logout }
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
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, padding);

        var center = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddChild(center);
        var card = new PanelContainer
        {
            Name = "GoogleAccountCard",
            CustomMinimumSize = new Vector2(Math.Max(300, Math.Min(Ui.IsMobile ? 1050 : 860,
                GetViewport().GetVisibleRect().Size.X - 2 * padding)), 0)
        };
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
        foreach (var button in new[] { _login, _logout })
        {
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            Ui.TouchTarget(button, 76);
            content.AddChild(button);
        }
        ArcanaMenuChrome.Attach(this, margin, main, "Mi perfil", 3, main.NavigateOptionAMenu);
        _message = main.GoogleLoginStatus;
        Render();
    }

    public void UpdateGoogleStatus(string message, bool busy = false)
    {
        if (_lifetime.IsCancellationRequested || !IsInsideTree()) return;
        _message = message;
        _busy = busy;
        Render();
    }

    private void Render()
    {
        if (!IsInsideTree()) return;
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
                  _message.Contains("configur", StringComparison.OrdinalIgnoreCase)
                    ? _message
                    : "Inicia sesión para sincronizar tu perfil.";
        _login.Visible = !google;
        _logout.Visible = google;
        _login.Disabled = _busy;
        _logout.Disabled = _busy;
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
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
