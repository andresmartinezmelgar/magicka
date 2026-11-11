using Godot;

namespace Arcana.Client;

public partial class TutorialGuide : PanelContainer
{
    private ElementTextLabel _title = null!, _body = null!;
    private Button _next = null!, _retry = null!;
    private Button? _exit;
    private VBoxContainer _box = null!;
    private HBoxContainer? _practiceLinks;

    internal void Build(Action next, Action retry, Action? exit = null)
    {
        Name = "TutorialGuide";
        Theme = Ui.Theme();
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", Ui.Panel(new Color("0d2239"), 10, Ui.Gold));
        var box = _box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 5);
        AddChild(box);
        _title = Ui.Label("", Ui.IsMobile ? 15 : 18, Ui.Gold);
        _title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_title);
        _body = Ui.Label("", Ui.IsMobile ? 14 : 16);
        _body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_body);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        box.AddChild(actions);
        if (exit != null)
        {
            var leave = Ui.Button("Salir", exit);
            _exit = leave;
            leave.Name = "FirstWaveExit";
            leave.CustomMinimumSize = new(0, Ui.IsMobile ? 44 : 36);
            actions.AddChild(leave);
        }
        _retry = Ui.Button(exit == null ? "Repetir capítulo" : "Repetir", retry);
        _retry.Name = "TutorialRetry";
        actions.AddChild(_retry);
        _next = Ui.Button("Continuar", next, true);
        _next.Name = "TutorialNext";
        actions.AddChild(_next);
        _retry.CustomMinimumSize = _next.CustomMinimumSize = new(0, Ui.IsMobile ? 44 : 36);
    }

    internal void BuildPracticeLinks(Action elements, Action lab)
    {
        _practiceLinks = new HBoxContainer { Visible = false };
        _box.AddChild(_practiceLinks);
        foreach (var (title, open) in new[] { ("Elementos", elements), ("Laboratorio", lab) })
        {
            var link = Ui.Button(title, open);
            link.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            link.CustomMinimumSize = new(0, Ui.IsMobile ? 44 : 36);
            _practiceLinks.AddChild(link);
        }
    }

    internal void ShowStep(string title, string body, string next, bool ready, bool retry, bool showResources = false)
    {
        _title.Text = title;
        _body.Text = body;
        _next.Text = next;
        _next.Disabled = !ready;
        _retry.Visible = retry;
        if (_exit != null) _exit.Visible = !showResources;
        if (_practiceLinks != null) _practiceLinks.Visible = showResources;
    }

    internal bool Blocks(Vector2 point) => IsVisibleInTree() && GetGlobalRect().HasPoint(point);
}
