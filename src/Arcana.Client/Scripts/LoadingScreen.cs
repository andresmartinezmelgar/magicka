using Godot;

namespace Arcana.Client;

/// <summary>Oculta la preparación real del mundo 3D, sin impedir que se dibuje detrás.</summary>
public partial class LoadingScreen : Control
{
    private ElementTextLabel _stage = null!, _percentage = null!;
    private ProgressBar _bar = null!;
    private PanelContainer _panel = null!;
    private ContentScrollContainer _scroll = null!;
    internal double Progress => _bar.Value;
    public void Build(string map, Action cancel)
    {
        Name = "GameLoadingScreen"; Theme = Ui.Theme(); Ui.Full(this);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new(0.02f, 0.035f, 0.06f, 1) }; AddChild(shade); Ui.Full(shade);
        var center = new CenterContainer(); AddChild(center); Ui.Full(center);
        _panel = new PanelContainer(); center.AddChild(_panel);
        var frame = new VBoxContainer(); frame.AddThemeConstantOverride("separation", 8); _panel.AddChild(frame);
        var header = new HBoxContainer(); frame.AddChild(header);
        var title = Ui.Label("PREPARANDO PARTIDA", 22, Ui.Gold); title.ClipText = true;
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill; header.AddChild(title);
        header.AddChild(Ui.ExitButton("Cancelar", cancel, "LoadingCancel"));
        _scroll = new ContentScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        frame.AddChild(_scroll); Ui.VisibleVerticalScroll(_scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 12); _scroll.AddChild(box);
        var mapName = Ui.Label(map, 20); mapName.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(mapName);
        _stage = Ui.Label("Cargando recursos…", 18, Ui.Muted); _stage.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_stage);
        _bar = new ProgressBar { Name = "LoadingProgress", MinValue = 0, MaxValue = 100, ShowPercentage = false,
            CustomMinimumSize = new(0, 28) };
        _bar.AddThemeStyleboxOverride("background", Ui.Panel(new("101b2d"), 4));
        _bar.AddThemeStyleboxOverride("fill", Ui.Panel(Ui.Blue, 4)); box.AddChild(_bar);
        _percentage = Ui.Label("0 %", 18, Ui.Gold); _percentage.HorizontalAlignment = HorizontalAlignment.Right; box.AddChild(_percentage);
        Resize(); GetViewport().SizeChanged += Resize;
    }
    private void Resize()
    {
        var size = GetViewport().GetVisibleRect().Size;
        float width = Math.Max(240, Math.Min(680, size.X - 32));
        _panel.CustomMinimumSize = new(width, 0);
        _scroll.CustomMinimumSize = new(width - 32, Math.Max(90, Math.Min(230, size.Y - 140)));
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= Resize;
    public void UpdateProgress(double progress, string stage)
    {
        _bar.Value = Math.Max(_bar.Value, Math.Clamp(progress, 0, 100));
        _percentage.Text = $"{Math.Floor(_bar.Value):0} %"; _stage.Text = stage;
    }
}
