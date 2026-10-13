using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Google y Firestore funcionan sin servidores de juego; las transacciones siguen protegidas.</summary>
public partial class AccountProfilePanel : Control
{
    private Main _main = null!;
    private Label _identity = null!, _status = null!;
    private Button _login = null!, _logout = null!;
    private bool _busy;
    private readonly CancellationTokenSource _lifetime = new();

    public void Build(Main main)
    {
        _main = main;
        Name = "AccountProfilePanel";
        Theme = Ui.Theme();
        Ui.Full(this);
        var background = new ColorRect { Color = new(0.01f, 0.025f, 0.045f, 0.98f) };
        AddChild(background); Ui.Full(background);
        var margin = new MarginContainer();
        AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 12 : 32);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        margin.AddChild(scroll);
        var center = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(center);
        var panel = new PanelContainer
        {
            CustomMinimumSize = new(Ui.IsMobile ? 520 : 440, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        center.AddChild(panel);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", Ui.IsMobile ? 14 : 18);
        panel.AddChild(content);
        content.AddChild(Ui.Label("MI PERFIL", 27, Ui.Gold));
        content.AddChild(Ui.Label("🧙", Ui.IsMobile ? 32 : 42, Ui.Gold));
        _identity = Ui.Label("", Ui.IsMobile ? 22 : 20);
        _identity.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_identity);
        _status = Ui.Label(main.GoogleLoginStatus, Ui.IsMobile ? 17 : 15, Ui.Muted);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_status);
        var info = Ui.Label("Google identifica al jugador y Firebase guarda su perfil. No necesitas iniciar el servidor C# para entrar y consultar tus objetos. Las ventas, las recompensas y las demás operaciones protegidas siguen requiriendo validación del servidor.", 15, Ui.Muted);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(info);
        _login = Ui.Button("Entrar o registrarse con Google", Login, true);
        _login.CustomMinimumSize = new(0, Ui.IsMobile ? 64 : 48);
        content.AddChild(_login);
        _logout = Ui.Button("Cerrar sesión", () => _ = Logout());
        _logout.CustomMinimumSize = new(0, Ui.IsMobile ? 64 : 48);
        content.AddChild(_logout);
        var back = Ui.Button("Volver", () => { _lifetime.Cancel(); QueueFree(); });
        back.CustomMinimumSize = new(0, Ui.IsMobile ? 64 : 48);
        content.AddChild(back);
        Render();
    }

    public void UpdateGoogleStatus(string message, bool busy = false)
    {
        if (_lifetime.IsCancellationRequested || !IsInsideTree()) return;
        _status.Text = message;
        _busy = busy;
        Render();
    }

    private void Render()
    {
        if (!IsInsideTree()) return;
        var profile = _main.Accounts.Profile;
        bool signedIn = _main.Accounts.SignedIn;
        _identity.Text = signedIn && profile != null
            ? profile.Name + "\nCuenta de Google conectada\n" +
              profile.Wins + " victorias · " + profile.Losses + " derrotas · " + profile.Gold + " oro\n" +
              profile.Items.Length + " objetos en inventario"
            : signedIn ? "Google conectado. Esperando datos de Firestore…"
            : profile != null
                ? profile.Name + "\nPerfil guardado en este dispositivo para practicar sin conexión."
                : "Sin sesión de Google. Puedes practicar contra IA sin conexión.";
        _login.Visible = !signedIn;
        _logout.Visible = signedIn;
        _login.Disabled = _logout.Disabled = _busy;
    }

    private void Login()
    {
        if (_busy) return;
        _main.StartGoogleLogin();
    }

    private async Task Logout()
    {
        if (_busy) return;
        _busy = true;
        _status.Text = "Cerrando sesión…";
        Render();
        try
        {
            await _main.StopGoogleSession();
            UpdateGoogleStatus("Sesión de Google cerrada.");
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
