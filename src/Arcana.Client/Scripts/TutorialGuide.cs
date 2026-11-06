using Godot;

namespace Arcana.Client;

public partial class TutorialGuide : PanelContainer
{
    private ElementTextLabel _title = null!, _body = null!;
    private Button _next = null!, _retry = null!;

    internal void Build(Action next, Action retry)
    {
        Name = "TutorialGuide";
        Theme = Ui.Theme();
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", Ui.Panel(new Color("0d2239"), 10, Ui.Gold));
        var box = new VBoxContainer();
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
        _retry = Ui.Button("Repetir capítulo", retry);
        _retry.Name = "TutorialRetry";
        actions.AddChild(_retry);
        _next = Ui.Button("Continuar", next, true);
        _next.Name = "TutorialNext";
        actions.AddChild(_next);
        _retry.CustomMinimumSize = _next.CustomMinimumSize = new(0, Ui.IsMobile ? 44 : 36);
    }

    internal void ShowStep(string title, string body, string next, bool ready, bool retry)
    {
        _title.Text = title;
        _body.Text = body;
        _next.Text = next;
        _next.Disabled = !ready;
        _retry.Visible = retry;
    }

    internal bool Blocks(Vector2 point) => IsVisibleInTree() && GetGlobalRect().HasPoint(point);
}
