using Arcana.Core;

namespace Arcana.Client;

/// <summary>Escena determinista utilizada por capturas y comprobaciones del HUD, nunca por partidas en red.</summary>
internal static class VisualFixture
{
    public static WorldSnapshot Spells(int frame, string view)
    {
        var state = Create("none");
        var local = state.Players[0] with { Position = V2.Zero, Selected = [], Shield = 0, ShieldMax = 0, ShieldSeconds = 0 };
        if (view == "barriers")
        {
            var walls = new List<WallSnapshot>
            {
                new(1, 1, new(-9,-3), new(-4,-3), 650, 7, [Element.Shield]),
                new(2, 1, new(-9,2), new(-4,2), 430, 7, [Element.Shield,Element.Cold]),
                new(3, 1, new(-9,6), new(-4,6), 960, 10, [Element.Shield,Element.Earth,Element.Fire])
            };
            for (int i = 0; i < 6; i++)
            {
                V2 center = new(5,2);
                walls.Add(new(10+i,1,center + new V2(1,0).Rotated(i*MathF.Tau/6)*2.5f,
                    center + new V2(1,0).Rotated((i+1)*MathF.Tau/6)*2.5f,650,7,[Element.Shield]));
            }
            return state with { Tick=frame, Time=frame/60.0, Mode=GameMode.Arena, Players=[local],
                Walls=walls.ToArray(), Beams=[], Fields=[], Projectiles=[], Points=[], Events=[] };
        }
        Element[][] recipes = [[Element.Arcane,Element.Arcane,Element.Arcane],
            [Element.Life,Element.Life,Element.Life], [Element.Arcane,Element.Fire,Element.Fire],
            [Element.Arcane,Element.Cold,Element.Cold], [Element.Arcane,Element.Lightning,Element.Lightning]];
        string[] names = ["Arcano","Curación","Arcano + fuego","Arcano + frío","Arcano + rayo"];
        return state with
        {
            Tick=frame, Time=frame/60.0, Mode=GameMode.Arena,
            Players=recipes.Select((elements,i) => local with { Id=i+1, Name=names[i], Position=new(-4,-8+i*4),
                Facing=new(1,0), Casting=true, CastingElements=elements }).ToArray(),
            Beams=recipes.Select((elements,i) => new BeamSnapshot(i+1,new(-3.3f,-8+i*4),new(5,-8+i*4),
                elements,SpellShape.Beam)).ToArray(),
            Walls=[], Fields=[], Projectiles=[], Points=[], Events=[]
        };
    }

    public static WorldSnapshot Wizards(int frame, string? view = null)
    {
        var world = new GameWorld(new() { Mode = GameMode.Warfare, MaxPlayers = 16 });
        for (int team = 0; team < 2; team++)
            foreach (WizardType type in Enum.GetValues<WizardType>()) world.AddPlayer(team == 0 ? "Azul" : "Rojo", team, wizardType: type);
        var state = world.Snapshot();
        int row = view switch { "front" => 0, "west" => 1, "back" => 2, "east" => 3, _ => frame / 60 % 4 };
        V2 facing = row switch { 1 => new(-1, 0), 2 => new(0, -1), 3 => new(1, 0), _ => new(0, 1) };
        int phase = frame % 60;
        var players = state.Players.Select((p, index) => p with
        {
            Team = index < 5 ? 0 : 1,
            Position = new(-9 + (index % 5) * 4.5f + (view == null && phase is >= 20 and < 45 ? (phase - 20) * 0.05f : 0), index < 5 ? -2.5f : 4.5f),
            Facing = facing, Health = 830, Shield = index is 2 or 8 ? 210 : 0, ShieldMax = 420, ShieldSeconds = 6.4f,
            Casting = view == null ? phase >= 45 : index is 1 or 8,
            Frozen = view == null && frame is >= 242 and <= 250 ? 1 : 0,
            Alive = !(view == null && frame is >= 252 and <= 260), Respawn = 2,
            Selected = [Element.Shield, Element.Arcane, Element.Fire]
        }).ToArray();
        return state with { Tick = frame, Time = frame / 60.0, Players = players,
            Events = view == null && frame % 60 == 6 ? players.Select(p => new GameEvent(frame * 100 + p.Id, "self", p.Position, [Element.Shield], "")).ToArray() : [], Beams = [] };
    }
    private static readonly string[] EffectKinds = ["cast", "self", "nova", "impact", "explosion", "shield_hit", "shield_break", "hurt", "blink", "blink_arrive", "barrier", "death"];
    public static WorldSnapshot Effects(int frame)
    {
        var state = Create("half"); bool idle = frame >= 160;
        int charges = frame < 50 ? 1 : frame < 100 ? 2 : 3;
        Element[] Repeated(Element element) => Enumerable.Repeat(element, charges).ToArray();
        var players = state.Players.Select((p, i) => p with
        {
            WizardType = (WizardType)(i + 1), Casting = !idle && i == 0,
            CastingElements = Repeated(Element.Fire),
            Burning = !idle && i == 1 ? 2 : 0, Frozen = !idle && i == 2 ? 1 : 0,
            Wet = !idle && i == 3 ? 3 : 0, Haste = !idle && i == 0 ? 2 : 0
        }).ToArray();
        int eventIndex = Math.Max(0, (frame - 1) / 10);
        string kind = EffectKinds[eventIndex % EffectKinds.Length];
        V2 position = kind == "explosion" ? new(0, 4) : kind == "impact" ? new(3, 0) : players[eventIndex % 4].Position;
        Element[] elements = Repeated(kind is "shield_hit" or "shield_break" or "self" ? Element.Shield : kind == "impact" ? Element.Cold : Element.Fire);
        return state with
        {
            Tick = frame, Time = frame / 60.0, Players = players,
            Projectiles = idle ? [] : [new(charges*10+1, 1, new(-2 + frame % 90 / 15f, 1), new(1, 0), Repeated(Element.Earth), 0.3f),
                new(charges*10+2, 2, new(2 - frame % 90 / 15f, 3), new(-1, 0), Repeated(Element.Cold), 0.16f)],
            Fields = idle ? [] : [new(1, 1, new(0, 4), 3, 0.8f, [Element.Earth, Element.Fire], "meteor")],
            Beams = idle ? [] : [new(1, new(-3.2f, -1), new(2.8f, -1), Repeated(Element.Fire), SpellShape.Spray),
                new(4, new(3.2f, 5), new(-2.8f, 5), Repeated(Element.Lightning), SpellShape.Lightning)],
            Events = idle ? [] : [new(eventIndex + 1, kind, position, elements, "")]
        };
    }
    public static WorldSnapshot Strength(int frame, int charges)
    {
        var state = Create("none");
        Element[] Repeated(Element element) => Enumerable.Repeat(element, charges).ToArray();
        var kinds = new[] { Element.Fire, Element.Arcane, Element.Lightning, Element.Earth };
        var players = state.Players.Select((p, i) => p with
        {
            Position = new(-7, -5.4f + i * 3.6f), Name = Ui.ElementName(kinds[i]), Team = 0,
            Selected = [], CastingElements = Repeated(kinds[i]), Shield = 0, ShieldMax = 0, ShieldSeconds = 0,
            Health = 1000, Casting = true, Charge = i == 3 ? 0.65f : 0, Facing = new(1, 0)
        }).ToArray();
        return state with
        {
            Tick = frame, Time = frame / 60.0, Mode = GameMode.Arena, Players = players, Points = [],
            Beams = [new(1, new(-6.3f,-5.4f), new(6,-5.4f), Repeated(Element.Fire), SpellShape.Spray),
                new(2, new(-6.3f,-1.8f), new(6,-1.8f), Repeated(Element.Arcane), SpellShape.Beam),
                new(3, new(-6.3f,1.8f), new(6,1.8f), Repeated(Element.Lightning), SpellShape.Lightning)],
            Projectiles = [new(1, 4, new(-4 + frame % 80 * 0.12f,5.4f), new(1,0), Repeated(Element.Earth),0.3f)],
            Fields = [], Events = frame >= 86 ? [new(1,"nova",new(4,5.4f),Repeated(Element.Fire),"",2)] : []
        };
    }
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
