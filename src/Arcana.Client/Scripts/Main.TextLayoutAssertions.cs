using Godot;

namespace Arcana.Client;

public partial class Main
{
    private static void AssertVisibleTextFits(Control root)
    {
        foreach (var control in root.FindChildren("*", "Control", true, false).OfType<Control>())
        {
            if (!control.IsVisibleInTree()) continue;
            // El contenido de un scroll puede ser mayor que su ventana visible.
            // Dentro del contenido, cada tarjeta debe contener a sus propios hijos.
            if (control.GetParent() is Container container && container is not ScrollContainer &&
                !container.GetGlobalRect().Grow(1).Encloses(control.GetGlobalRect()))
                throw new InvalidOperationException($"Control fuera de su contenedor: {control.GetPath()} {control.GetGlobalRect()} / {container.GetGlobalRect()}");
            if (control is not ElementTextLabel label || label.Text.Length == 0 || label.ClipText ||
                label.MaxLinesVisible >= 0 || label.TextOverrunBehavior != TextServer.OverrunBehavior.NoTrimming) continue;
            var content = label.RenderedTextSize;
            if (content.X > label.Size.X + 1 || content.Y > label.Size.Y + 1)
                throw new InvalidOperationException($"Texto recortado en {label.GetPath()}: necesita {content}, dispone de {label.Size}. Texto: {label.Text}");
        }
    }
}
