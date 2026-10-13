using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>
/// Perfil accesible en Android horizontal y vertical.
/// La autenticación Google y la autorización de Firestore son estados independientes.
/// </summary>
public partial class AccountProfilePanel : Control
{
    private Main _main = null!;
    private Label _identity = null!, _summary = null!, _status = null!, _hint = null!, _technical = null!;
    private Button _login = null!, _logout = null!, _rulesHelp = null!, _sourceRules = null!;
    private Button _retryFirebase = null!, _back = null!, _details = null!;
    private bool _busy;
    private bool _showTechnical;
    private string _message = "";
    private readonly CancellationTokenSource _lifetime = new();

    // Expuesto solo para las pruebas de geometría del menú, sin datos personales ni tokens.
    internal Button[] VisibleFooterButtons => new[] { _login, _logout, _rulesHelp, _retryFirebase, _back }
        .Where(b => GodotObject.IsInstanceValid(b) && b.IsVisibleInTree()).ToArray();

    public void Build(Main main)
    {
        _main = main;
        Name = "AccountProfilePanel";
        Theme = Ui.Theme();
        Ui.Full(this);
        var background = new ColorRect { Color = new(0.01f, 0.025f, 0.045f, 0.97f) };
        AddChild(background); Ui.Full(background);

        var margin = new MarginContainer();
        AddChild(margin); Ui.Full(margin);
        int padding = Ui.IsMobile ? 12 : 28;
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, padding);

        // Aprovecha el ancho panorámico del móvil: antes la tarjeta se limitaba a 520 px
        // y obligaba a mostrar una columna de texto excesivamente larga.
        var center = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddChild(center);
        center.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var width = Math.Max(240, Math.Min(Ui.IsMobile ? 1020f : 780f,
            GetViewport().GetVisibleRect().Size.X - 2 * padding));
        var panel = new PanelContainer
        {
            Name = "ResponsiveAccountCard",
            CustomMinimumSize = new(width, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        panel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("0b1729"), 14, new Color("385371")));
        center.AddChild(panel);
        center.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        var layout = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        layout.AddThemeConstantOverride("separation", Ui.IsMobile ? 12 : 15);
        panel.AddChild(layout);

        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 12);
        layout.AddChild(heading);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddChild(titles);
        titles.AddChild(Ui.Label("ARCANA ARENA  /  CUENTA", 14, Ui.Gold));
        titles.AddChild(Ui.Label("MI PERFIL", Ui.IsMobile ? 28 : 25, Ui.Text));
        _summary = Ui.Label("Estado de Google", Ui.IsMobile ? 17 : 15, Ui.Gold);
        _summary.HorizontalAlignment = HorizontalAlignment.Right;
        heading.AddChild(_summary);
        layout.AddChild(new HSeparator());

        // El texto largo se desplaza; la botonera queda anclada y siempre visible.
        var scroll = new ScrollContainer
        {
            Name = "AccountDetailsScroll",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        layout.AddChild(scroll);
        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddThemeConstantOverride("separation", Ui.IsMobile ? 12 : 14);
        scroll.AddChild(details);

        _identity = Ui.Label("", Ui.IsMobile ? 24 : 21, Ui.Text);
        _identity.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        details.AddChild(_identity);
        _status = Ui.Label("", Ui.IsMobile ? 18 : 16, Ui.Muted);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        details.AddChild(_status);
        _hint = Ui.Label("", Ui.IsMobile ? 17 : 15, Ui.Muted);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        details.AddChild(_hint);

        _sourceRules = Ui.Button("Ver reglas que deben publicarse", () =>
            OS.ShellOpen("https://github.com/andresmartinezmelgar/magicka/blob/main/firebase/firestore.rules"));
        details.AddChild(_sourceRules);
        _details = Ui.Button("Mostrar diagnóstico técnico", ToggleDetails);
        details.AddChild(_details);
        _technical = Ui.Label("", 14, Ui.Muted);
        _technical.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        details.AddChild(_technical);

        var footer = new VBoxContainer { Name = "AccountFixedActions" };
        footer.AddThemeConstantOverride("separation", Ui.IsMobile ? 8 : 10);
        layout.AddChild(new HSeparator());
        layout.AddChild(footer);
        var mainActions = new HBoxContainer();
        mainActions.AddThemeConstantOverride("separation", Ui.IsMobile ? 9 : 12);
        footer.AddChild(mainActions);
        _login = Ui.Button("Entrar con Google", Login, true);
        mainActions.AddChild(_login);
        _retryFirebase = Ui.Button("Reintentar Firebase", main.RetryGoogleSession, true);
        mainActions.AddChild(_retryFirebase);
        _rulesHelp = Ui.Button("Abrir Firebase Console", () =>
            OS.ShellOpen("https://console.firebase.google.com/project/arcana-arena-f2997/firestore/rules"));
        mainActions.AddChild(_rulesHelp);

        var secondary = new HBoxContainer();
        secondary.AddThemeConstantOverride("separation", Ui.IsMobile ? 9 : 12);
        footer.AddChild(secondary);
        _logout = Ui.Button("Cerrar sesión", () => _ = Logout());
        secondary.AddChild(_logout);
        _back = Ui.Button("Volver al menú", Close);
        secondary.AddChild(_back);
        foreach (var button in new[] { _login, _retryFirebase, _rulesHelp, _logout, _back })
        {
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            Ui.TouchTarget(button, 70);
        }
        _message = main.GoogleLoginStatus;
        ApplyOptionAProfile(margin, panel, layout, scroll, details);
        Render();
    }

    private void ToggleDetails()
    {
        _showTechnical = !_showTechnical;
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
        var profile = _main.Accounts.Profile;
        bool signedIn = _main.Accounts.SignedIn;
        bool googleKnown = _main.GoogleIdentityKnown || signedIn;
        bool firestoreError = _message.StartsWith("Error al leer Firestore:", StringComparison.Ordinal);
        bool forbidden = _message.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase);
        bool unauthorized = _message.Contains("UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase);
        bool missing = _message.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase);

        _summary.Text = signedIn ? "●  Cuenta sincronizada"
            : googleKnown ? "●  Google identificado"
            : "○  Sin conexión";
        _summary.AddThemeColorOverride("font_color", signedIn || googleKnown ? Ui.Gold : Ui.Muted);

        if (signedIn && profile != null)
        {
            _identity.Text = profile.Name + "  ·  " + profile.Gold + " oro";
            _status.Text = profile.Wins + " victorias  ·  " + profile.Losses + " derrotas  ·  " +
                profile.Items.Length + " objetos guardados.";
            _hint.Text = "Tu inventario está sincronizado con Firestore. Los premios y las ventas se verifican en el servidor del juego.";
        }
        else if (googleKnown)
        {
            _identity.Text = "Google conectado" +
                (_main.GoogleIdentityName.Length > 0 ? "  ·  " + _main.GoogleIdentityName : "");
            if (forbidden)
            {
                _status.Text = "Firestore no permite leer tu perfil (error 403).";
                _hint.Text = "La sesión de Google es válida. Falta comprobar y publicar las reglas de acceso del proyecto en Firebase Console. " +
                    "Después pulsa Reintentar Firebase.";
            }
            else if (unauthorized)
            {
                _status.Text = "Firestore no acepta esta sesión (error 401).";
                _hint.Text = "Cierra sesión y vuelve a entrar con Google. Comprueba que la aplicación usa el proyecto correcto.";
            }
            else if (missing)
            {
                _status.Text = "No se ha encontrado la base de datos o el perfil (error 404).";
                _hint.Text = "Comprueba que existe Firestore (default) en el proyecto arcana-arena-f2997.";
            }
            else
            {
                _status.Text = firestoreError ? "No se ha podido cargar el perfil." : "Comprobando tu perfil de Firebase…";
                _hint.Text = firestoreError ? "Reintenta la conexión. Si continúa fallando, abre el diagnóstico técnico."
                    : "El acceso a Google no necesita que esté iniciado el servidor C#.";
            }
        }
        else
        {
            _identity.Text = "Todavía no has conectado Google";
            _status.Text = profile != null
                ? "Puedes seguir practicando con el inventario guardado en este dispositivo."
                : "Inicia sesión para consultar tu oro, tu perfil y tus objetos.";
            _hint.Text = "Las partidas contra IA también funcionan sin iniciar sesión.";
        }

        _sourceRules.Visible = forbidden;
        _rulesHelp.Visible = forbidden;
        _retryFirebase.Visible = firestoreError;
        _retryFirebase.Disabled = _busy || !_main.GoogleLoginAvailable;
        _login.Visible = !googleKnown;
        _login.Disabled = _busy || !_main.GoogleLoginAvailable;
        _logout.Visible = googleKnown;
        _logout.Disabled = _busy;
        _details.Visible = firestoreError;
        _technical.Visible = firestoreError && _showTechnical;
        _technical.Text = _technical.Visible ? _message : "";
        _details.Text = _showTechnical ? "Ocultar diagnóstico técnico" : "Mostrar diagnóstico técnico";
        UpdateOptionAProfile();
    }

    private void Login()
    {
        if (!_busy) _main.StartGoogleLogin();
    }

    private async Task Logout()
    {
        if (_busy) return;
        _busy = true;
        Render();
        try
        {
            await _main.StopGoogleSession();
            UpdateGoogleStatus("Sesión de Google cerrada.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { UpdateGoogleStatus("No se pudo cerrar sesión: " + error.Message); }
    }

    private void Close() => QueueFree();

    public override void _ExitTree()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
