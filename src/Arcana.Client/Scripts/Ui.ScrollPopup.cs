using Godot;

namespace Arcana.Client;

public static partial class Ui
{
    internal const string TouchScrollPopupName = "TouchScrollPopup";

    internal static void EnableTouchScrollPopup(BaseButton owner, PopupMenu menu)
    {
        if (!UsesTouchScroll) return;
        owner.ActionMode = BaseButton.ActionModeEnum.Release;
        menu.AboutToPopup += () => Callable.From(() => ShowTouchScrollPopup(owner, menu)).CallDeferred();
    }

    private static async void ShowTouchScrollPopup(BaseButton owner, PopupMenu menu)
    {
        if (!GodotObject.IsInstanceValid(owner) || !owner.IsVisibleInTree() || owner.IsQueuedForDeletion() ||
            !menu.Visible || menu.ItemCount == 0) return;

        // El PopupMenu sigue siendo el modelo. Las filas táctiles usan el mismo
        // ScrollContainer que el resto de la interfaz y confirman al soltar.
        var size = menu.Size;
        menu.Hide();
        var popup = new PopupPanel { Name = TouchScrollPopupName, Visible = false, Theme = Theme() };
        var list = new ScrollChoiceList { Name = "TouchOptionsScroll", ActivateOnTap = true };
        popup.AddChild(list);
        owner.AddChild(popup);

        for (int index = 0; index < menu.ItemCount; index++)
            list.AddItem(menu.GetItemText(index), menu.GetItemIcon(index), menu.IsItemDisabled(index) || menu.IsItemSeparator(index));
        if (owner is OptionButton option) list.Select(option.Selected);
        list.ItemActivated += index =>
        {
            if (index >= menu.ItemCount || menu.IsItemDisabled(index) || menu.IsItemSeparator(index)) return;
            int id = menu.GetItemId(index);
            popup.Hide();
            if (owner is OptionButton) menu.EmitSignal(PopupMenu.SignalName.IndexPressed, (long)index);
            else menu.EmitSignal(PopupMenu.SignalName.IdPressed, (long)(id >= 0 ? id : index));
        };

        void CloseIfHidden()
        {
            if (!owner.IsVisibleInTree()) popup.Hide();
        }

        owner.VisibilityChanged += CloseIfHidden;
        popup.PopupHide += () =>
        {
            owner.VisibilityChanged -= CloseIfHidden;
            owner.SetPressedNoSignal(false);
            popup.QueueFree();
        };
        owner.SetPressedNoSignal(true);
        var margins = popup.GetThemeStylebox("panel", "PopupPanel").GetMinimumSize();
        popup.PopupCenteredClamped(new(Math.Max(size.X + ScrollBarThickness, 260),
            (int)Math.Ceiling(list.ContentHeight + margins.Y)), 0.9f);
        await popup.ToSignal(popup.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (GodotObject.IsInstanceValid(popup) && popup.Visible) list.FocusSelection();
    }
}
