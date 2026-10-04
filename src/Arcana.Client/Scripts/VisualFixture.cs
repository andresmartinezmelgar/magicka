using Arcana.Core;

namespace Arcana.Client;

/// <summary>Escena determinista utilizada por capturas y comprobaciones del HUD, nunca por partidas en red.</summary>
internal static class VisualFixture
{
    public static WorldSnapshot Create(string shieldState)
    {
        var world = new GameWorld(new() { Mode = GameMode.Warfare, MaxPlayers = 8 });
        var local = world.AddPlayer("Aprendiz azul", 0)!; local.Position = new(-4, -1); local.Health = 830;
        var rival = world.AddPlayer("Rival rojo", 1)!; rival.Position = new(4, -1); rival.Health = 650;
        var ally = world.AddPlayer("Aliado azul", 0)!; ally.Position = new(-4, 5); ally.Health = 920;
        var other = world.AddPlayer("Rival rojo 2", 1)!; other.Position = new(4, 5); other.Health = 760;
        var snapshot = world.Snapshot();
        var localState = snapshot.Players[0] with { Selected = [Element.Shield, Element.Arcane, Element.Fire] };
        localState = shieldState switch
        {
            "full" => localState with { Shield = 420, ShieldMax = 420, ShieldSeconds = 12 },
            "half" => localState with { Shield = 210, ShieldMax = 420, ShieldSeconds = 6.4f },
            "low" => localState with { Shield = 40, ShieldMax = 420, ShieldSeconds = 2.3f },
            "ward" => localState with { Shield = 90, ShieldMax = 180, ShieldSeconds = 5.6f, Ward = [Element.Fire] },
            "stone" => localState with { Shield = 325, ShieldMax = 650, ShieldSeconds = 4.2f, Ward = [Element.Earth] },
            _ => localState with { Shield = 0, ShieldMax = 0, ShieldSeconds = 0 }
        };
        return snapshot with
        {
            Players = [localState, snapshot.Players[1] with { Shield = 130, ShieldMax = 420, ShieldSeconds = 6 },
                snapshot.Players[2] with { Shield = 650, ShieldMax = 650, ShieldSeconds = 10, Ward = [Element.Earth] }, snapshot.Players[3]],
            Points = [snapshot.Points[0] with { Owner = 0 }, snapshot.Points[1], snapshot.Points[2] with { Owner = 1 }],
            Beams = [new(local.Id, new(-3.2f, -1), new(2.8f, -1), [Element.Arcane, Element.Fire], SpellShape.Beam),
                new(other.Id, new(3.2f, 5), new(-2.8f, 5), [Element.Lightning], SpellShape.Lightning)]
        };
    }
}
