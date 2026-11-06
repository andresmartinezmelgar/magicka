using Godot;

namespace Arcana.Client;

/// <summary>Comparte el gesto de arrastre entre el contenido y sus controles.</summary>
public partial class ContentScrollContainer : ScrollContainer
{
    private SceneTree? _tree;

    public ContentScrollContainer()
    {
        ScrollDeadzone = 10;
    }

    public override void _EnterTree()
    {
        _tree = GetTree();
        _tree.NodeAdded += PrepareControl;
        PrepareExistingChildren(this);
    }

    public override void _ExitTree()
    {
        if (_tree != null) _tree.NodeAdded -= PrepareControl;
        _tree = null;
    }

    private void PrepareExistingChildren(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            PrepareControl(child);
            PrepareExistingChildren(child);
        }
    }

    private void PrepareControl(Node node)
    {
        if (node is not Control control || !IsAncestorOf(control)) return;

        // El scroll nativo cancela el toque pendiente al empezar a arrastrar.
        // Los campos de edición y deslizadores conservan sus propios gestos.
        if (control is BaseButton button && button.MouseFilter != MouseFilterEnum.Ignore && Ui.UsesTouchScroll)
            button.ActionMode = BaseButton.ActionModeEnum.Release;

        if (control.MouseFilter == MouseFilterEnum.Stop &&
            (control is BaseButton or PanelContainer or Panel or ColorRect or TextureRect || control.GetType() == typeof(Control)))
            control.MouseFilter = MouseFilterEnum.Pass;
    }
}
