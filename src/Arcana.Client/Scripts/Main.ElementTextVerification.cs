using Godot;
using Arcana.Core;

namespace Arcana.Client;

public partial class Main
{
    private async Task RunElementTextSmoke()
    {
        try
        {
            var label = Ui.Label("Escudo + Fuego + Vida-Vida; área eléctrica; rayo arcano", 18);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.Size = new(320, 200);
            _ui.AddChild(label);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (label.PlainColor != Ui.Text) throw new InvalidDataException("El texto sin elemento pierde contraste con el fondo.");
            if (label.GetParsedText() != label.Text || !label.FormattedText.Contains("[color=#" + Ui.ElementTextColor(Element.Shield).ToHtml(false) + "]Escudo"))
                throw new InvalidDataException("La etiqueta muestra marcado o pierde el color del escudo.");
            using var measure = new Label { Text = label.Text, Size = label.Size, AutowrapMode = label.AutowrapMode };
            measure.AddThemeFontOverride("font", label.GetThemeFont("font"));
            measure.AddThemeFontSizeOverride("font_size", 18);
            _ui.AddChild(measure); measure.Hide();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (label.GetMinimumSize().DistanceTo(measure.GetMinimumSize()) > 1)
                throw new InvalidDataException($"El texto con colores cambia las medidas de la etiqueta: {label.GetMinimumSize()} frente a {measure.GetMinimumSize()}, tamaño {label.Size}.");
            label.Text = "[color=red]Fuego[/color] + Vida";
            if (label.GetParsedText() != label.Text) throw new InvalidDataException("El nombre externo puede inyectar marcado.");
            label.Text = "";
            if (label.GetParsedText() != "") throw new InvalidDataException("Queda contenido después de vaciar la etiqueta.");
            label.QueueFree();
            var button = Ui.Button("Fuego + Escudo", () => { }); button.Size = new(320, 60); _ui.AddChild(button);
            button._Notification((int)CanvasItem.NotificationDraw);
            if (button.GetChildren().OfType<ElementTextLabel>().Single().GetParsedText() != button.Text)
                throw new InvalidDataException("El título del botón no conserva el texto visible.");
            button.Text = "Continuar"; button._Notification((int)CanvasItem.NotificationDraw);
            if (button.GetThemeColor("font_color").A == 0 || button.GetChildren().OfType<ElementTextLabel>().Single().Visible)
                throw new InvalidDataException("El botón no restaura su título al cambiar a texto sin elementos.");
            button.QueueFree();
            GD.Print("ELEMENT_TEXT_SMOKE_OK: colores, recetas mixtas, cambios dinámicos, texto externo, medidas y botones verificados.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("ELEMENT_TEXT_SMOKE_FAILED: " + error); GetTree().Quit(1); }
    }
}
