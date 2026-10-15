using Godot;

namespace Arcana.Client;

/// <summary>Deja que los gestos sobre tarjetas y fondos lleguen al scroll nativo.</summary>
public partial class ContentScrollContainer : ScrollContainer
{
    private SceneTree? _tree;

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
        // Solo presentación: botones, listas, campos y otros scrolls conservan su entrada.
        // NodeAdded también cubre las tarjetas que se regeneran al cambiar de sección.
        if (IsAncestorOf(node) && node is Control control &&
            control.MouseFilter == MouseFilterEnum.Stop &&
            (control is PanelContainer or Panel or ColorRect or TextureRect || control.GetType() == typeof(Control)))
            control.MouseFilter = MouseFilterEnum.Pass;
    }
}
