using Godot;

namespace Arcana.Client;

/// <summary>Panel amplio de perfil para la opción A, sin scroll y con navegación estable.</summary>
public partial class AccountProfilePanel
{
    private ArcanaMenuChrome? _optionAChrome;
    private Label? _optionAStats, _optionAGold, _optionAGoogle;

    private void ApplyOptionAProfile(MarginContainer margin, PanelContainer panel,
        VBoxContainer layout, ScrollContainer scroll, VBoxContainer originalDetails)
    {
        _optionAChrome = ArcanaMenuChrome.Attach(this, margin, _main, "Mi perfil", 3, _main.NavigateOptionAMenu);
        int width = Math.Max(360, Math.Min(Ui.IsMobile ? 1380 : 1510,
            (int)GetViewport().GetVisibleRect().Size.X - 28));
        panel.CustomMinimumSize = new Vector2(width, 0);
        panel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("0b1c31"), 12, new Color("3375a6")));
        layout.AddThemeConstantOverride("separation", 8);
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        var overview = new HBoxContainer
        {
            Name = "OptionAProfileColumns",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        overview.AddThemeConstantOverride("separation", Ui.IsMobile ? 10 : 18);
        scroll.AddChild(overview);
        var playerCard = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.6f, SizeFlagsVertical = SizeFlags.ExpandFill };
        playerCard.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("10263f"), 8, new Color("295e86")));
        overview.AddChild(playerCard);
        originalDetails.Reparent(playerCard);
        originalDetails.AddThemeConstantOverride("separation", 8);
        var statsCard = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.0f, SizeFlagsVertical = SizeFlags.ExpandFill };
        statsCard.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("102a42"), 8, new Color("2d789d")));
        overview.AddChild(statsCard);
        var stats = new VBoxContainer();
        stats.AddThemeConstantOverride("separation", Ui.IsMobile ? 9 : 16);
        statsCard.AddChild(stats);
        stats.AddChild(Ui.Label("ESTADÍSTICAS Y CUENTA", Ui.IsMobile ? 18 : 23, Ui.Gold));
        _optionAStats = Ui.Label("", Ui.IsMobile ? 18 : 23, Ui.Text);
        _optionAStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stats.AddChild(_optionAStats);
        stats.AddChild(new HSeparator());
        _optionAGold = Ui.Label("", Ui.IsMobile ? 22 : 27, Ui.Gold);
        stats.AddChild(_optionAGold);
        _optionAGoogle = Ui.Label("", Ui.IsMobile ? 16 : 18, Ui.Muted);
        _optionAGoogle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stats.AddChild(_optionAGoogle);
        UpdateOptionAProfile();
    }

    private void UpdateOptionAProfile()
    {
        if (_optionAStats == null) return;
        var data = _main.Accounts.Profile;
        _optionAStats.Text = data == null ? "Victorias    —\nDerrotas     —\nObjetos      —"
            : $"Victorias    {data.Wins}\nDerrotas     {data.Losses}\nObjetos      {data.Items.Length}";
        _optionAGold!.Text = data == null ? "●  0 oro" : $"●  {data.Gold:N0} oro";
        _optionAGoogle!.Text = _main.GoogleIdentityKnown || _main.Accounts.SignedIn
            ? "Google autenticado\n" + (_main.Accounts.SignedIn ? "Firestore: sincronizado" : "Firestore: pendiente de permisos")
            : "Google: desconectado\nPuedes entrenar sin conexión.";
        _optionAChrome?.RefreshAccount();
    }
}
