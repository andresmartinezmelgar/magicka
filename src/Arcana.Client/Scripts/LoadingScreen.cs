using Godot;

namespace Arcana.Client;

/// <summary>Oculta la preparación real del mundo 3D, sin impedir que se dibuje detrás.</summary>
public partial class LoadingScreen : Control
{
    private Label _stage = null!, _percentage = null!;
    private ProgressBar _bar = null!;
    internal double Progress => _bar.Value;
    public void Build(string map, Action cancel)
    {
        Name = "GameLoadingScreen"; Theme = Ui.Theme(); Ui.Full(this);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new(0.02f, 0.035f, 0.06f, 1) }; AddChild(shade); Ui.Full(shade);
        var center = new CenterContainer(); AddChild(center); Ui.Full(center);
        var panel = new PanelContainer { CustomMinimumSize = new(Ui.IsMobile ? 720 : 620, 0) }; center.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 20); panel.AddChild(box);
        box.AddChild(Ui.Label("PREPARANDO PARTIDA", 28, Ui.Gold));
        box.AddChild(Ui.Label(map, 20));
        _stage = Ui.Label("Cargando recursos…", 18, Ui.Muted); _stage.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_stage);
        _bar = new ProgressBar { Name = "LoadingProgress", MinValue = 0, MaxValue = 100, ShowPercentage = false,
            CustomMinimumSize = new(0, 28) };
        _bar.AddThemeStyleboxOverride("background", Ui.Panel(new("101b2d"), 4));
        _bar.AddThemeStyleboxOverride("fill", Ui.Panel(Ui.Blue, 4)); box.AddChild(_bar);
        _percentage = Ui.Label("0 %", 18, Ui.Gold); _percentage.HorizontalAlignment = HorizontalAlignment.Right; box.AddChild(_percentage);
        box.AddChild(Ui.Button("Cancelar", cancel));
    }
    public void UpdateProgress(double progress, string stage)
    {
        _bar.Value = Math.Max(_bar.Value, Math.Clamp(progress, 0, 100));
        _percentage.Text = $"{Math.Floor(_bar.Value):0} %"; _stage.Text = stage;
    }
}
