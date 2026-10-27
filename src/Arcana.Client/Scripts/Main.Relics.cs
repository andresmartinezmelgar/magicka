using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private Button? _relicMenuButton;
    private AcceptDialog? _relicWindow;
    private bool HasRelics => _snapshot is { } state &&
        (state.Defense != null || state.Map is ArenaMapId.Relics or ArenaMapId.Ascension);
    private void OpenRelics()
    {
        if (!HasRelics) return;
        CloseRelics(); StopInput();
        var state = _connection?.Latest ?? _snapshot;
        var player = state?.Players.FirstOrDefault(p => p.Id == _localId);
        var buffs = player?.Buffs ?? [];
        _relicWindow = new AcceptDialog { Name = "CollectedRelics", Title = "Reliquias recogidas",
            OkButtonText = "Volver al menú", Exclusive = true };
        _ui.AddChild(_relicWindow);
        Ui.ConfigureDialog(_relicWindow);
        var scroll = new ContentScrollContainer { Name = "RelicsScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _relicWindow.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
        var entries = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        entries.AddThemeConstantOverride("separation", 14); scroll.AddChild(entries);
        entries.AddChild(Ui.Label("RELIQUIAS RECOGIDAS", 24, Ui.Gold));
        var details = Ui.Label(RelicBuffs.DescribeCollected(buffs, state?.Defense != null), Ui.IsMobile ? 18 : 17,
            buffs.Length > 0 ? Ui.Gold : Ui.Muted);
        details.Name = "CollectedRelicsDetails";
        details.AutowrapMode = TextServer.AutowrapMode.WordSmart; details.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        entries.AddChild(details);
        var viewport = GetViewport().GetVisibleRect().Size;
        var size = new Vector2I((int)Math.Min(680, viewport.X - 32), (int)Math.Min(560, viewport.Y - 48));
        scroll.CustomMinimumSize = new(Math.Max(220, size.X - 48), Math.Max(100, size.Y - 96));
        _relicWindow.PopupCentered(size); UpdateTouchVisibility();
    }
    private void CloseRelics()
    {
        if (GodotObject.IsInstanceValid(_relicWindow)) _relicWindow!.QueueFree();
        _relicWindow = null;
    }
}
