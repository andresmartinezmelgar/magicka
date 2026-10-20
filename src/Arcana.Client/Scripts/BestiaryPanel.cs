using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class BestiaryPanel : Control
{
    private Main _main = null!;
    private VBoxContainer _entries = null!;
    private Label _status = null!;
    private Button _refresh = null!;
    private AccountProfile? _lastProfile;
    private bool _busy;
    public void Build(Main main)
    {
        _main = main; Name = "BestiaryPanel"; Theme = Ui.Theme(); Ui.Full(this);
        var shade = new ColorRect { Color = ArcanaMenuChrome.Backdrop }; AddChild(shade); Ui.Full(shade);
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 14 : 28);
        var layout = new VBoxContainer(); layout.AddThemeConstantOverride("separation", 12); margin.AddChild(layout);
        layout.AddChild(Ui.Label("MI PERFIL · BESTIARIO", Ui.IsMobile ? 22 : 28, Ui.Gold));
        _status = Ui.Label("", 16, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; layout.AddChild(_status);
        _refresh = Ui.Button("Actualizar bajas", () => _ = Refresh()); _refresh.Name = "BestiaryRefresh"; layout.AddChild(_refresh);
        var scroll = new ContentScrollContainer { Name = "BestiaryScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        layout.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
        _entries = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _entries.AddThemeConstantOverride("separation", 12); scroll.AddChild(_entries);
        ArcanaMenuChrome.Attach(this, margin, main, "Bestiario", 3, main.NavigateOptionAMenu);
        Render();
    }
    private void Render()
    {
        _lastProfile = _main.Accounts.Profile;
        foreach (var child in _entries.GetChildren()) { _entries.RemoveChild(child); child.QueueFree(); }
        _status.Text = _lastProfile == null ? "Conecta tu cuenta para guardar las bajas. El catálogo se puede consultar sin iniciar sesión."
            : "Bajas guardadas de " + _lastProfile.Name + ". Solo cuentan los enemigos eliminados por ti.";
        int pending = _main.PendingBestiaryRuns;
        if (pending > 0) _status.Text += $" Hay {pending} partida(s) pendientes de sincronizar.";
        foreach (var kind in Enum.GetValues<MinionKind>())
        {
            var card = new PanelContainer { Name = "Bestiary_" + kind, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            card.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("10263f"), 8, new Color("3375a6")));
            _entries.AddChild(card);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Ui.IsMobile ? 12 : 18); card.AddChild(row);
            var portrait = new BestiaryPortrait { SizeFlagsVertical = SizeFlags.ShrinkCenter };
            row.AddChild(portrait); portrait.Build(kind);
            var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 6); row.AddChild(body);
            long kills = _lastProfile?.Bestiary.GetValueOrDefault(BestiaryCatalogue.Key(kind)) ?? 0;
            var heading = Ui.Label(DefenseRules.MinionName(kind) + $" · Bajas: {kills}", 20, Ui.Gold);
            heading.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(heading);
            var description = Ui.Label(BestiaryCatalogue.Description(kind), Ui.IsMobile ? 17 : 18);
            description.AutowrapMode = TextServer.AutowrapMode.WordSmart; description.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(description);
        }
    }
    private async Task Refresh()
    {
        if (_busy) return; _busy = true; _refresh.Disabled = true;
        string message = await _main.SynchronizeBestiary();
        if (!IsInsideTree() || IsQueuedForDeletion()) return;
        Render(); _status.Text = message; _busy = false; _refresh.Disabled = false;
    }
    public override void _Process(double delta)
    {
        if (!_busy && !ReferenceEquals(_lastProfile, _main.Accounts.Profile)) Render();
    }
}
