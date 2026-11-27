using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Perfil único: personaje local, cuenta Google y sus funciones.</summary>
public partial class AccountProfilePanel : Control
{
    private Main _main = null!;
    private ElementTextLabel _heading = null!, _status = null!;
    private Button _login = null!, _logout = null!, _checkUpdate = null!, _touchControls = null!, _bestiary = null!, _statistics = null!, _equipment = null!;
    private ElementTextLabel _updateStatus = null!;
    private GridContainer _profileActions = null!, _wizardFields = null!;
    private bool _checkingUpdate;
    private string _updateIdentity = "";
    private bool _busy;
    private string _message = "";
    private Button _deleteAccount = null!;
    private string _deletionMessage = "";
    private readonly CancellationTokenSource _lifetime = new();
    private FileDialog? _googleFileDialog;
    private PanelContainer? _responsiveCard;
    private int _responsivePadding;

    internal Button[] VisibleFooterButtons => new[] { _login, _logout, _equipment, _statistics, _checkUpdate, _touchControls, _bestiary, _deleteAccount, _advertisingToggle }
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

        var layout = new VBoxContainer(); layout.AddThemeConstantOverride("separation", 10); margin.AddChild(layout);
        var header = new HBoxContainer(); layout.AddChild(header);
        var title = Ui.TextBlock("MI PERFIL", Ui.IsMobile ? 22 : 28, Ui.Gold);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill; header.AddChild(title);
        header.AddChild(Ui.ExitButton("Volver", () => main.NavigateBack(), "ProfileBack"));
        var scroll = new ContentScrollContainer { Name = "ProfileContentScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        layout.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
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
        if (main.TutorialCompleted)
        {
            var replayTutorial = Ui.Button("Repetir tutorial", main.OpenTutorial);
            replayTutorial.Name = "ProfileReplayTutorial";
            Ui.TouchTarget(replayTutorial, 60);
            content.AddChild(replayTutorial);
        }
        BuildWizardSettings(content);
        main.BuildAudioControls(content);
        content.AddChild(Ui.TextBlock("CUENTA DE GOOGLE", Ui.IsMobile ? 20 : 19, Ui.Gold));
        _heading = Ui.TextBlock("", Ui.IsMobile ? 27 : 25, Ui.Text);
        _heading.HorizontalAlignment = HorizontalAlignment.Center;
        _heading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_heading);
        _status = Ui.TextBlock("", Ui.IsMobile ? 18 : 16, Ui.Muted);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(_status);

        _login = Ui.Button("Entrar con Google", () => { if (!_busy) _main.StartGoogleLogin(); }, true);
        _logout = Ui.Button("Cerrar sesión", () => _ = Logout(), true);
        _deleteAccount = Ui.Button("Eliminar cuenta y datos", ConfirmAccountDeletion);
        _deleteAccount.Name = "ProfileDeleteAccount";
        _checkUpdate = Ui.Button("Buscar actualización", () => _ = CheckUpdate());
        _checkUpdate.Name = "ProfileCheckUpdates";
        _equipment = Ui.Button("Inventario y equipamiento", () => main.OpenEquipment("automatic"));
        _equipment.Name = "ProfileEquipment";
        _bestiary = Ui.Button("Bestiario y bajas", main.OpenBestiary);
        _bestiary.Name = "ProfileBestiary";
        _statistics = Ui.Button("Estadísticas de partidas", main.OpenMatchStatistics);
        _statistics.Name = "ProfileMatchStatistics";
        _statistics.TooltipText = "Gráficas de las partidas guardadas en Firebase.";
        _touchControls = Ui.Button("Configurar joystick y botones", main.OpenTouchSettings);
        _touchControls.Name = "ProfileTouchControls";
        _touchControls.TooltipText = "Tamaño, posiciones, zona muerta, sensibilidad y modo zurdo. Se guardan en este dispositivo.";
        _advertisingToggle = Ui.Button("Consultar anuncios globales", () => _ = ChangeAdvertising());
        _advertisingToggle.Name = "ProfileAdvertisingToggle";
        _advertisingToggle.TooltipText = "Activa o desactiva la publicidad para los jugadores. Administradores y cuentas sin anuncios quedan siempre exentos.";
        _profileActions = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _profileActions.AddThemeConstantOverride("h_separation", 10); _profileActions.AddThemeConstantOverride("v_separation", 8);
        content.AddChild(_profileActions);
        foreach (var button in new[] { _login, _logout, _equipment, _statistics, _checkUpdate, _touchControls, _bestiary, _deleteAccount, _advertisingToggle })
        {
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 18 : 19);
            Ui.TouchTarget(button, 76);
            _profileActions.AddChild(button);
        }
        _updateStatus = Ui.TextBlock("", Ui.IsMobile ? 17 : 16, Ui.Muted);
        _updateStatus.Name = "ProfileUpdateStatus";
        _updateStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _updateStatus.HorizontalAlignment = HorizontalAlignment.Center; content.AddChild(_updateStatus);
        _advertisingStatus = Ui.TextBlock("", Ui.IsMobile ? 17 : 16, Ui.Muted);
        _advertisingStatus.Name = "ProfileAdvertisingStatus";
        _advertisingStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_advertisingStatus);
        ArcanaMenuChrome.Attach(this, margin, main, "Mi perfil", 3, main.NavigateOptionAMenu);
        _message = main.GoogleLoginStatus;
        GetViewport().SizeChanged += ResizeProfileCard;
        ResizeProfileCard();
        Render();
    }

    private void BuildWizardSettings(VBoxContainer content)
    {
        content.AddChild(Ui.TextBlock("TU PERSONAJE", Ui.IsMobile ? 20 : 19, Ui.Gold));
        _wizardFields = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _wizardFields.AddThemeConstantOverride("h_separation", 12); content.AddChild(_wizardFields);
        var nameColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var typeColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _wizardFields.AddChild(nameColumn); _wizardFields.AddChild(typeColumn);
        nameColumn.AddChild(Ui.TextBlock("Nombre del mago", 16, Ui.Muted));
        var name = new LineEdit { Name = "ProfileWizardName", MaxLength = 24, Text = _main.LocalWizardName,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Ui.TouchTarget(name); nameColumn.AddChild(name);
        typeColumn.AddChild(Ui.TextBlock("Tipo de mago", 16, Ui.Muted));
        var wizardType = new ScrollOptionButton { Name = "WizardTypeChoice", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var type in Enum.GetValues<WizardType>()) wizardType.AddItem(WizardTypes.Name(type), (int)type);
        wizardType.Select(wizardType.GetItemIndex((int)_main.InventoryWizardType));
        Ui.TouchTarget(wizardType); typeColumn.AddChild(wizardType);
        var description = Ui.TextBlock(WizardTypes.Description(_main.InventoryWizardType), 15, Ui.Muted);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart; content.AddChild(description);
        var savedHint = Ui.TextBlock("Los cambios del personaje se guardan automáticamente.", 14, Ui.Muted);
        savedHint.AutowrapMode = TextServer.AutowrapMode.WordSmart; content.AddChild(savedHint);
        void Save() => _main.SetWizardProfile(name.Text, (WizardType)wizardType.GetSelectedId());
        name.TextChanged += _ => Save();
        wizardType.ItemSelected += _ =>
        {
            description.Text = WizardTypes.Description((WizardType)wizardType.GetSelectedId());
            Save();
        };
    }

    private void ResizeProfileCard()
    {
        if (_responsiveCard == null) return;
        float width = GetViewport().GetVisibleRect().Size.X;
        _responsiveCard.CustomMinimumSize = new Vector2(
            Math.Max(220, Math.Min(Ui.IsMobile ? 1050 : 860, width - 2 * _responsivePadding - 36)), 0);
        _profileActions.Columns = width < 560 ? 1 : 2;
        _wizardFields.Columns = width < 560 ? 1 : 2;
        float height = GetViewport().GetVisibleRect().Size.Y < 640 ? 60 : 76;
        foreach (var button in new[] { _login, _logout, _equipment, _statistics, _checkUpdate, _touchControls, _bestiary, _deleteAccount, _advertisingToggle }) button.CustomMinimumSize = new(0, height);
    }

}
