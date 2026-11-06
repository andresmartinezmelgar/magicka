using Godot;

namespace Arcana.Client;

internal partial class ScrollOptionButton : OptionButton
{
    public ScrollOptionButton() => Ui.EnableTouchScrollPopup(this, GetPopup());
}
