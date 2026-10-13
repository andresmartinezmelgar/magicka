using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Identidad de Google separada del inventario y del juego online.</summary>
public partial class AccountProfilePanel : Control
{
    private Main _main = null!;
    private Label _identity = null!, _status = null!;
    private Button _login = null!, _logout = null!;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;

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
        var center = new CenterContainer(); margin.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new(Ui.IsMobile ? 0 : 440, 0) };
        center.AddChild(panel);
        var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 18); panel.AddChild(content);
        content.AddChild(Ui.Label("MI PERFIL", 27, Ui.Gold));
        content.AddChild(Ui.Label("🧙", 42, Ui.Gold));
        _identity = Ui.Label("", 20); _identity.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_identity);
        _status = Ui.Label("", 15, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_status);
        _login = Ui.Button("Entrar o registrarse con Google", () => _ = Login(), true);
        content.AddChild(_login);
        _logout = Ui.Button("Cerrar sesión", () => _ = Logout());
        content.AddChild(_logout);
        content.AddChild(Ui.Button("Volver", () => { _lifetime.Cancel(); QueueFree(); }));
        Render();
    }

    private void Render()
    {
        if (!IsInsideTree()) return;
        var profile = _main.Accounts.Profile;
        bool signedIn = _main.Accounts.SignedIn;
        _identity.Text = signedIn && profile != null
            ? profile.Name + "\nCuenta de Google conectada\n" +
              profile.Wins + " victorias · " + profile.Losses + " derrotas · " + profile.Gold + " oro"
            : profile != null
                ? profile.Name + "\nPerfil e inventario guardados en este dispositivo. Modo sin conexión disponible."
                : "Sin sesión de Google. Puedes practicar contra IA sin conexión.";
        _login.Visible = !signedIn;
        _logout.Visible = signedIn;
        _login.Disabled = _logout.Disabled = _busy;
    }

    private void Message(string message)
    {
        _main.Post(() =>
        {
            if (_lifetime.IsCancellationRequested || !IsInsideTree()) return;
            _busy = false;
            _status.Text = message;
            Render();
        });
    }

    private async Task Login()
    {
        if (_busy) return;
        _busy = true; _status.Text = "Preparando el acceso con Google…"; Render();
        try
        {
            using var settings = System.Text.Json.JsonDocument.Parse(
                Godot.FileAccess.GetFileAsString("res://account-server.json"));
            string address = settings.RootElement.TryGetProperty("accountUrl", out var configuredUrl)
                ? configuredUrl.GetString()?.Trim() ?? "" : "";
            if (address.Length == 0)
                throw new InvalidOperationException("Google requiere el servidor independiente de cuentas. Configura accountUrl en account-server.json. Puedes jugar contra IA y consultar el inventario guardado sin conexión.");
            var config = await _main.Accounts.Configuration(address, _lifetime.Token);
            if (!config.Enabled) throw new InvalidOperationException(config.Message);
            var challenge = await _main.Accounts.BeginLogin(address, _lifetime.Token);
            _main.Post(() =>
            {
                if (_lifetime.IsCancellationRequested) return;
                OS.ShellOpen(challenge.Url);
                _status.Text = "Completa el acceso en el navegador y vuelve al juego.";
            });
            while (DateTimeOffset.UtcNow < challenge.ExpiresAt)
            {
                if (await _main.Accounts.PollLogin(challenge, _lifetime.Token))
                {
                    _main.SaveAccountCache();
                    Message("Sesión iniciada correctamente.");
                    return;
                }
                await Task.Delay(1000, _lifetime.Token);
            }
            throw new InvalidOperationException("El acceso ha caducado. Inténtalo de nuevo.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { Message(error.Message); }
    }

    private async Task Logout()
    {
        if (_busy) return;
        _busy = true; _status.Text = "Cerrando sesión…"; Render();
        try
        {
            await _main.Accounts.SignOut(_lifetime.Token);
            _main.SaveAccountCache();
            Message("Sesión cerrada.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { Message(error.Message); }
    }

    public override void _ExitTree()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
