using Arcana.Core;

namespace Arcana.Client;

internal static partial class VisualFixture
{
    internal static readonly V2[] AreaPositions = [new(-6,-3), new(0,-3), new(6,-3), new(-6,4), new(0,4), new(6,4)];
    internal static readonly V2[] AuraPositions = [new(-5,-3), new(5,-3), new(-5,5), new(5,5)];
    internal static readonly string[] AreaNames = ["Suelo ardiente", "Agua", "Hielo", "Mina", "Mina curativa", "Mina · 3 cargas"];
    internal static readonly string[] AuraNames = ["Aura de fuego", "Aura eléctrica", "Regeneración", "Auras + Berserker"];
    internal static readonly string[] PassiveNames = ["Meteorito", "Berserker", "Pulso glacial", "Relámpagos"];
    private static WorldSnapshot? _areaDefenseBase;

    internal static WorldSnapshot AreaAndPassiveScene(int frame, string view)
    {
        if (_areaDefenseBase == null)
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4, Seed = 271 });
            for (int i = 0; i < 4; i++) world.AddPlayer("Mago " + i);
            _areaDefenseBase = world.Snapshot();
        }
        var basis = _areaDefenseBase;
        var local = basis.Players[0] with { Position = new(-20,10), Selected = [], Shield = 0,
            ShieldMax = 0, ShieldSeconds = 0, Ward = [], Casting = false, InvulnerableSeconds = 0 };
        if (view == "areas")
        {
            string[] kinds = ["burning_ground", "water_pool", "frost_ground", "mine", "healing_mine", "mine"];
            Element[][] elements = [[Element.Fire], [Element.Water], [Element.Cold], [Element.Shield, Element.Arcane],
                [Element.Shield, Element.Life], [Element.Shield, Element.Arcane, Element.Fire]];
            return basis with { Tick = frame, Time = frame / 60.0,
                Players = [local], Walls = [], Beams = [], Projectiles = [], Points = [], Events = [], Pickups = [],
                Defense = basis.Defense! with { Defenders = basis.Defense.Defenders.Where(d => d.PlayerId == local.Id).ToArray(), HumanCount = 1 },
                Fields = kinds.Select((kind, i) => new FieldSnapshot(i + 1, 1, AreaPositions[i], 2.4f, 7, elements[i], kind,
                    0.85f, frame % 120 > 30)).ToArray() };
        }
        var players = basis.Players.Select((p, i) => p with { Position = AuraPositions[i], Name = "",
            InvulnerableSeconds = 0, Selected = [], Facing = new(0,1) }).ToArray();
        var defenders = basis.Defense!.Defenders.Select((d, i) => d with
        {
            Auras = view != "auras" ? [0,0,0] : i == 3 ? [2,2,2] : Enumerable.Range(0,3).Select(a => a == i ? 2 : 0).ToArray(),
            BerserkerRemaining = view == "auras" ? i == 3 ? 4 : 0 : i == 1 ? 4 : 0,
            Passives = [1,1,1,1]
        }).ToArray();
        int burst = (frame + 14) / 90; long id = burst * 10L + 1;
        var chainStart = AuraPositions[3];
        V2[] targets = [chainStart+new V2(2,0), chainStart+new V2(0,-2), chainStart+new V2(-2,0)];
        var events = new List<GameEvent>
        {
            new(id,"nova",AuraPositions[1],[Element.Fire,Element.Arcane],"Berserker",1.8f),
            new(id+1,"nova",AuraPositions[2],[Element.Cold],"Pulso glacial",5)
        };
        if ((frame+14)%90 >= 8)
        {
            events.Add(new(id+2,"passive_chain",chainStart,[Element.Lightning],""));
            events.AddRange(targets.Select((at,i) => new GameEvent(id+3+i,"hurt",at,[Element.Lightning],"Relámpago")));
        }
        return basis with { Tick = frame, Time = frame / 60.0, Players = players,
            Defense = basis.Defense with { Minions = view != "passives" ? [] : targets.Select((at,i) =>
                new MinionSnapshot(100+i,MinionKind.Melee,at,new(0,1),700,1000,0,0,0,0,false,[],false)).ToArray(),
                Drops = [], Defenders = defenders },
            Fields = view == "passives" ? [new(1 + (frame+60)/90, 1, AuraPositions[0], 3, 1.5f - (frame+60)%90/60f,
                [Element.Earth,Element.Fire], "defense_meteor")] : [],
            Events = view != "passives" ? [] : events.ToArray()
        };
    }
}
