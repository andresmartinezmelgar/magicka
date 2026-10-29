using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Identidad y aspecto real de un participante, sin consultar su inventario privado.</summary>
public partial class RoomWaitingPlayerCard : PanelContainer
{
    private EquipmentAvatar _portrait = null!;
    private ElementTextLabel _name = null!, _type = null!, _role = null!, _state = null!;
    private int _team = -1;
    private bool _local;
    private (string Name, WizardType Type, EquipmentAppearance? Equipment, int Team, bool Bot,
        int LocalId, int HostId, bool Ready)? _lastState;

    internal void Build(WizardSnapshot player)
    {
        Name = "WaitingPlayer" + player.Id;
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        AddChild(row);
        _portrait = new EquipmentAvatar();
        row.AddChild(_portrait);
        _portrait.BuildPortrait(player.WizardType, player.Equipment);
        _portrait.Name = "WaitingPortrait" + player.Id;
        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        details.AddThemeConstantOverride("separation", 4);
        row.AddChild(details);
        _name = Detail(details, "", 18, Ui.Text);
        _name.MaxLinesVisible = 2;
        _type = Detail(details, "", 14, Ui.Muted);
        _role = Detail(details, "", 13, Ui.Gold);
        _state = Detail(details, "", 15, Ui.Muted);
    }

    private static ElementTextLabel Detail(VBoxContainer parent, string text, int size, Color color)
    {
        var label = Ui.Label(text, size, color);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(label);
        return label;
    }

    internal void Update(WizardSnapshot player, int localId, int hostId, bool ready)
    {
        var state = (player.Name, player.WizardType, player.Equipment, player.Team, player.Bot, localId, hostId, ready);
        if (_lastState.HasValue && _lastState.Value == state) return;
        _lastState = state;
        bool local = player.Id == localId;
        var color = player.Team == 0 ? Ui.Blue : Ui.Red;
        if (_team != player.Team || _local != local)
        {
            _team = player.Team; _local = local;
            var style = Ui.Panel(new Color(local ? "183047" : "101e30"), 8,
                local ? Ui.Gold : new Color(color, .35f));
            style.ContentMarginLeft = style.ContentMarginRight = 8;
            style.ContentMarginTop = style.ContentMarginBottom = 6;
            AddThemeStyleboxOverride("panel", style);
        }
        _portrait.UpdatePortrait(player.WizardType, player.Equipment);
        _name.Text = player.Name;
        _type.Text = WizardTypes.Name(player.WizardType);
        _type.AddThemeColorOverride("font_color", Ui.WizardColor(player.WizardType));
        _role.Text = local ? player.Id == hostId ? "Tú · Anfitrión" : "Tú" :
            player.Id == hostId ? "Anfitrión" : "";
        _role.Visible = _role.Text.Length > 0;
        _state.Text = player.Bot ? "IA preparada" : ready ? "Preparado" : "Pendiente";
        _state.AddThemeColorOverride("font_color", player.Bot || ready ? Ui.Health : Ui.Muted);
    }
}
