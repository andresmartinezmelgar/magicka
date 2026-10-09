using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private Label3D? _practiceCasterLabel;
    public string PracticeCasterDescription { get; set; } = "Mago lanzador →";
    private Label3D PracticeLabel(Node3D root, string name, string text, V2 at, float height = 2.8f)
    {
        var label = new Label3D { Name = name, Text = text, Position = World(at, height),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 42, PixelSize = 0.012f,
            Modulate = Ui.Text, OutlineSize = 4, NoDepthTest = true };
        root.AddChild(label); return label;
    }
    private void BuildPracticeMap(Node3D root)
    {
        var spawn = new Node3D { Name = "PracticeRespawn", Position = World(PracticeLayout.Respawn) }; root.AddChild(spawn);
        Ring(spawn, 2.7f, 0.12f, new(0, 0.08f, 0), Ui.Blue);
        PracticeLabel(spawn, "PracticeRespawnLabel", "REAPARICIÓN", V2.Zero, 1.2f);
        foreach (var station in PracticeLayout.Barriers)
        {
            Ring(root, 3.3f, 0.035f, World(station.Position, 0.06f), new Color(Ui.Gold, 0.5f));
            PracticeLabel(root, "PracticeBarrierLabel" + station.Index,
                station.Name + " · " + station.Combination.Keys + "\n" + SpellCatalogue.Name(station.Combination.Elements), station.Position);
        }
        PracticeLabel(root, "PracticeTargetsHeading", "ESTADOS Y PROTECCIONES", new(0, -46), 1.4f);
        PracticeLabel(root, "PracticeBarriersHeading", "BARRERAS Y MINAS · RESTITUCIÓN EN 2 s", new(0, 10), 1.4f);
        _practiceCasterLabel = PracticeLabel(root, "PracticeCasterLabel", PracticeCasterDescription, PracticeLayout.CasterPosition, 4.1f);
        var lane = new Node3D { Name = "PracticeFiringLane" }; root.AddChild(lane);
        for (int i = 0; i < 12; i++)
            Box(lane, new(1.3f, 0.02f, 0.08f), World(PracticeLayout.CasterPosition + new V2(3 + i * 2.4f, 0), 0.04f), Ui.Gold);
        PracticeLabel(root, "PracticeFiringLaneLabel", "PRUEBA DE DAÑO Y CURACIÓN →", PracticeLayout.CasterPosition + new V2(16, 3), 0.5f);
    }
}
