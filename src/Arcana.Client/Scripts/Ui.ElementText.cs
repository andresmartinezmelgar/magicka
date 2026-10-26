using Godot;
using Arcana.Core;

namespace Arcana.Client;

public static partial class Ui
{
    public static Color ElementTextColor(Element element) => element == Element.Fire ? new("ff625c") : ElementColor(element);

    public static ElementTextLabel ElementTooltip(string text)
    {
        var label = Label(text, IsMobile ? 18 : 16);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new(IsMobile ? 400 : 480, 0);
        return label;
    }
}
