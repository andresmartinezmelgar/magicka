using Godot;
using Arcana.Core;

namespace Arcana.Client;

public partial class Main
{
    private async Task RunElementTextSmoke()
    {
        try
        {
            var label = Ui.Label("Escudo; Fuego + Vida-Vida; área eléctrica; rayo arcano", 18);
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
            label.AutowrapMode = TextServer.AutowrapMode.Off; label.ClipText = true;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.Size = new(80, 100); label.Text = "Traslación demasiado larga\n50";
            if (!label.GetParsedText().Contains('…') || !label.GetParsedText().EndsWith("\n50", StringComparison.Ordinal))
                throw new InvalidDataException("Recortar un nombre elimina la segunda línea del coste o la recarga.");
            label.QueueFree();
            var button = Ui.Button("Fuego + Escudo", () => { }); button.Size = new(320, 60); _ui.AddChild(button);
            button._Notification((int)CanvasItem.NotificationDraw);
            if (button.GetChildren().OfType<ElementTextLabel>().Single().GetParsedText() != button.Text)
                throw new InvalidDataException("El título del botón no conserva el texto visible.");
            button.Text = "Continuar"; button._Notification((int)CanvasItem.NotificationDraw);
            if (button.GetThemeColor("font_color").A == 0 || button.GetChildren().OfType<ElementTextLabel>().Single().Visible)
                throw new InvalidDataException("El botón no restaura su título al cambiar a texto sin elementos.");
            button.QueueFree();
            var book = new SpellBook { ShowPreview = false }; _ui.AddChild(book); book.SelectCombination("SF");
            var recipeTitle = (ElementTextLabel)book.FindChild("SpellCombinationTitle", true, false);
            if (recipeTitle.SemanticColor != Ui.SpellColor([Element.Arcane, Element.Fire]) || recipeTitle.FormattedText.Contains("[color=#" + Ui.ElementTextColor(Element.Fire).ToHtml(false)))
                throw new InvalidDataException("La combinación no usa un único color principal.");
            book.QueueFree();
            VerifyRelicStatisticsSmoke();
            VerifyDefenseInterfaceSmoke();
            GD.Print("ELEMENT_TEXT_SMOKE_OK: colores, recetas mixtas, cambios dinámicos, texto externo, medidas y botones verificados.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("ELEMENT_TEXT_SMOKE_FAILED: " + error); GetTree().Quit(1); }
    }
    private void VerifyRelicStatisticsSmoke()
    {
        var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, Bots = 0, MaxPlayers = 1 });
        var player = world.AddPlayer("Reliquias")!;
        player.BuffStacks[(int)BuffKind.Regeneration] = 2;
        player.Health = 500; player.Shield = 0;
        world.Step();
        var state = world.Snapshot();
        var table = Ui.Table(2, "RelicStatisticsSmoke"); _ui.AddChild(table);
        try
        {
            EquipmentPanel.RenderProgressStats(table, state.Defense!.Defenders.Single(p => p.PlayerId == player.Id), state.Players.Single(p => p.Id == player.Id));
            var effect = (ElementTextLabel)table.FindChild("RelicStatisticsSmoke_Effect_RelicRegeneration", true, false);
            var origin = (ElementTextLabel)table.FindChild("RelicStatisticsSmoke_Source_RelicRegeneration", true, false);
            if (player.Health <= 500 || !effect.Text.Contains("40 vida/s") || !effect.Text.Contains("sin escudo") ||
                !origin.Text.StartsWith("1.", StringComparison.Ordinal) || !origin.Text.Contains("×2"))
                throw new InvalidDataException("La tabla no explica la regeneración de reliquias sin escudo ni sus acumulaciones.");
        }
        finally { table.QueueFree(); }
    }
    private void VerifyDefenseInterfaceSmoke()
    {
        var previous = (_practice, _snapshot, _playing, _paused, _localId, _hud, _lastDefenseInterface, _localSequence);
        bool helpVisible = _help.Visible;
        var score = new Hud();
        try
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, Bots = 1, MaxPlayers = 4 });
            var player = world.AddPlayer("Consulta")!;
            _practice = world; _snapshot = world.Snapshot(); _playing = true; _localId = player.Id; _hud = score; _lastDefenseInterface = null;
            for (int panel = 0; panel < 3; panel++)
            {
                _paused = panel == 0; _help.Visible = panel == 1; score.ScoreOpen = panel == 2;
                long tick = world.Tick; _PhysicsProcess(1 / 60.0);
                if (!world.DefensePaused || world.Tick != tick) throw new InvalidDataException("El menú, ayuda o clasificación no pausa con un humano y bots.");
                _paused = false; _help.Visible = false; score.ScoreOpen = false; _PhysicsProcess(1 / 60.0);
                if (world.DefensePaused || world.Tick != tick + 1) throw new InvalidDataException("Cerrar la pantalla no reanuda Supervivencia.");
            }
            world.AddPlayer("Otro humano"); _snapshot = world.Snapshot(); _paused = true;
            long before = world.Tick; _PhysicsProcess(1 / 60.0);
            if (world.DefensePaused || world.Tick != before + 1) throw new InvalidDataException("El menú detiene Supervivencia con dos humanos.");
            GD.Print("SURVIVAL_INTERFACE_OK: pausa con un humano y bots; combate continuo con varios humanos.");
        }
        finally
        {
            (_practice, _snapshot, _playing, _paused, _localId, _hud, _lastDefenseInterface, _localSequence) = previous;
            _help.Visible = helpVisible; score.Free();
        }
    }

}
