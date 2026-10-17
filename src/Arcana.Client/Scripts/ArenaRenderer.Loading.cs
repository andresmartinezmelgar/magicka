using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    internal IEnumerable<(string Name, Action Work)> PrepareVisuals(WorldSnapshot initial, int localId)
    {
        foreach (var step in PrepareMap(initial.Map)) yield return ("Construyendo el mapa…", step);
        if (initial.Map == ArenaMapId.Bastion)
            foreach (var step in PrepareSurvivalSectors()) yield return step;
        foreach (var type in Enum.GetValues<WizardType>())
            yield return ("Cargando los magos…", () => WizardSprites.Load(type));
        yield return ("Preparando los iconos del inventario…", EquipmentPanel.PrepareIcons);
        foreach (var element in Enum.GetValues<Element>())
            yield return ("Preparando los sonidos…", () => _audio.Prepare(element.ToString()));
        yield return ("Preparando los sonidos…", () => _audio.Prepare("boom"));
        for (int i = 0; i < CombatEffects.ParticleLimit / 32; i++)
            yield return ("Preparando los efectos…", _effects.PreparePoolBatch);

        var player = initial.Players.First(p => p.Id == localId);
        V2 center = player.Position;
        if (initial.Defense != null)
            foreach (var step in PrepareDefenseMinions(center)) yield return step;
        var empty = initial with { Players = [], Projectiles = [], Walls = [], Fields = [], Beams = [], Events = [], Pickups = [],
            Defense = initial.Defense is { } d ? d with { Minions = [], Drops = [] } : null };
        if (initial.Map != ArenaMapId.Bastion)
        {
            var definition = ArenaMap.Get(initial.Map);
            for (float x = -definition.HalfWidth + 12; x < definition.HalfWidth; x += 24)
                for (float y = -definition.HalfHeight + 12; y < definition.HalfHeight; y += 24)
                {
                    var view = new V2(x, y);
                    if (!definition.Floors.Any(f => Math.Abs(view.X - f.Center.X) <= f.Width / 2 + 12 &&
                        Math.Abs(view.Y - f.Center.Y) <= f.Height / 2 + 12)) continue;
                    yield return ("Preparando las zonas del mapa…", () => DrawLoadingView(empty, view));
                }
        }
        var actors = Enum.GetValues<WizardType>().SelectMany(type => new[] { 0, 1 }.Select(team => player with
        {
            Id = 1000 + (int)type * 2 + team, WizardType = type, Team = team, Name = "Mago",
            Position = center + new V2(((int)type - 2) * 2, team * 3 - 1.5f), Facing = new(1, 0),
            Health = player.MaxHealth, Alive = true, Respawn = 0, Shield = 420, ShieldMax = 420, ShieldSeconds = 6,
            Ward = [Element.Earth, Element.Fire], ShieldCharges = 3, Selected = [Element.Water, Element.Cold, Element.Lightning],
            CastingElements = [Element.Arcane, Element.Fire], Burning = 2, Wet = 2, Chill = 0, Frozen = 0,
            Haste = 2, RegenerationRate = 50, InvulnerableSeconds = 1, Buffs = [], Cooldowns = [0, 0, 0, 0]
        })).ToArray();
        var scene = empty with { Players = actors };
        yield return ("Preparando magos y protecciones…", () => DrawLoadingView(scene, center));
        var ghosts = scene with { Players = actors.Select(p => p with { Health = 0, Alive = false,
            Shield = 0, InvulnerableSeconds = 0, Respawn = 1 }).ToArray() };
        yield return ("Preparando las animaciones…", () => DrawLoadingView(ghosts, center));
        yield return ("Preparando congelación y efectos de estado…", () => DrawLoadingView(scene with
            { Players = actors.Select(p => p with { Frozen = 2, Chill = 3, Ward = [Element.Cold] }).ToArray() }, center));

        foreach (var group in SpellCatalogue.All.Where(c => c.Recipe.Shape is SpellShape.Beam or SpellShape.Spray or SpellShape.Lightning).Chunk(16))
        {
            var beams = group.Select((c, i) => new BeamSnapshot(actors[i % actors.Length].Id,
                center + new V2(-3, (i % 4 - 1.5f) * 2), center + new V2(3, (i % 4 - 1.5f) * 2), c.Elements, c.Recipe.Shape, 1.5f)).ToArray();
            yield return ("Preparando rayos y conos…", () => DrawLoadingView(scene with { Beams = beams }, center));
        }
        foreach (var group in SpellCatalogue.All.Where(c => c.Recipe.Shape is SpellShape.Rock or SpellShape.Shards).Chunk(12))
        {
            var shots = group.Select((c, i) => new ProjectileSnapshot(2000 + i, actors[0].Id,
                center + new V2((i % 4 - 1.5f) * 3, (i / 4 - 1) * 3), new(1, 0), c.Elements, 0.4f)).ToArray();
            yield return ("Preparando los proyectiles…", () => { ClearLoadingObjects(); DrawLoadingView(scene with { Projectiles = shots }, center); });
        }
        foreach (var group in PracticeLayout.Barriers.Chunk(4))
        {
            var walls = new List<WallSnapshot>(); var fields = new List<FieldSnapshot>();
            for (int i = 0; i < group.Length; i++)
            {
                var recipe = group[i].Combination.Recipe;
                var at = center + new V2((i % 2 - 0.5f) * 7, (i / 2 - 0.5f) * 6);
                if (recipe.Healing || recipe.Has(Element.Arcane))
                    fields.Add(new(3000 + i, actors[0].Id, at, 1.8f, 7, recipe.Elements, recipe.Healing ? "healing_mine" : "mine"));
                else walls.Add(new(3000 + i, actors[0].Id, at - new V2(2, 0), at + new V2(2, 0), 650, 7, recipe.Elements));
            }
            yield return ("Preparando barreras y minas…", () =>
            { ClearLoadingObjects(); DrawLoadingView(scene with { Walls = walls.ToArray(), Fields = fields.ToArray() }, center); });
        }
        FieldSnapshot[] ground = [
            new(4001, actors[0].Id, center + new V2(-5, -3), 2, 0.2f, [Element.Earth, Element.Fire], "meteor"),
            new(4002, actors[0].Id, center + new V2(4, -3), 2, 6, [Element.Fire], "burning_ground"),
            new(4003, actors[0].Id, center + new V2(-5, 4), 2, 6, [Element.Cold], "frost_ground"),
            new(4004, actors[0].Id, center + new V2(4, 4), 2, 6, [Element.Water], "water_pool")];
        var bursts = new[] { Element.Fire, Element.Cold, Element.Life, Element.Shield, Element.Earth }.Select((e, i) =>
            new GameEvent(10000 + i, i == 4 ? "explosion" : "nova", center + new V2(i - 2, 0), [e], i == 4 ? "Meteoro" : "", 3)).ToArray();
        yield return ("Preparando meteoros y explosiones…", () =>
        { ClearLoadingObjects(); DrawLoadingView(scene with { Fields = ground, Events = bursts }, center); });
        foreach (var point in initial.Points)
            yield return ("Preparando los puntos del mapa…", () => DrawLoadingView(empty, point.Position));
        if (initial.Pickups is { Length: > 0 })
            foreach (var group in initial.Pickups.Chunk(4))
                yield return ("Preparando las reliquias…", () => DrawLoadingView(empty with
                { Pickups = group.Select((p, i) => p with { Position = center + new V2(i * 2 - 3, 0) }).ToArray() }, center));
        if (initial.Defense is { } defense)
        {
            var minions = Enum.GetValues<MinionKind>().Select((kind, i) => new MinionSnapshot(7500 + i, kind,
                center + new V2((i % 3 - 1) * 4, (i / 3 - 0.5f) * 5), new(1, 0), 1000, 1000,
                0, 0, 0, 0, kind == MinionKind.Mage, [Element.Arcane, Element.Fire], true)
                { AttackWarningSeconds = DefenseRules.AttackWindup(kind) }).ToArray();
            var preview = empty with { Players = [player], Defense = defense with { Minions = minions, Drops = [],
                Defenders = defense.Defenders.Where(p => p.PlayerId == localId).Select(p => p with
                    { Auras = [1, 1, 1], BerserkerRemaining = 2 }).ToArray() },
                Events = [new(20001,"nova",center,[Element.Fire,Element.Arcane],"Berserker",1.8f),
                    new(20002,"passive_chain",center,[Element.Lightning],""),
                    new(20003,"hurt",center+new V2(3,0),[Element.Lightning],"Relámpago")] };
            yield return ("Preparando enemigos, auras y habilidades…", () => DrawLoadingView(preview, center));
            yield return ("Preparando los estados de los enemigos…", () => DrawLoadingView(preview with
                { Defense = preview.Defense! with { Minions = minions.Select(m => m with
                    { Frozen = 2, Chill = 3, SpeedBoostSeconds = 2, Casting = false }).ToArray() } }, center));
            yield return ("Preparando el fuego y la regeneración…", () => DrawLoadingView(preview with
                { Defense = preview.Defense! with { Minions = minions.Select(m => m with
                    { Burning = 2 }).ToArray() } }, center));
            var drops = new List<DefenseDropSnapshot>
            { new(7000, 0, center, DefenseDropKind.Gold, 10, default, null, null) };
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
                drops.Add(new(7001 + (int)slot, 0, center + new V2((int)slot * 2 - 3, -2),
                    DefenseDropKind.Equipment, 0, default, slot, ItemTier.Gold));
            foreach (var buff in Enum.GetValues<BuffKind>())
                drops.Add(new(7010 + (int)buff, 0, center + new V2((int)buff * 2 - 6, 2),
                    DefenseDropKind.Relic, 0, buff, null, null));
            yield return ("Preparando el botín…", () => DrawLoadingView(empty with
                { Defense = defense with { Minions = [], Drops = drops.ToArray() } }, center));
        }
        yield return ("Preparando la partida…", Clear);
    }

    private void ClearLoadingObjects()
    {
        foreach (var item in _projectiles.Values) item.QueueFree(); _projectiles.Clear();
        foreach (var item in _walls.Values) item.QueueFree(); _walls.Clear();
        foreach (var item in _fields.Values) item.QueueFree(); _fields.Clear();
        // Los eventos de ensayo no deben ocultar los eventos reales de la sala.
        _lastEvent = 0; _effects.Clear();
    }
    private void DrawLoadingView(WorldSnapshot snapshot, V2 center)
    {
        Render(snapshot, 0, 1 / 60.0, true);
        FocusLoadingView(center);
    }
    private void FocusLoadingView(V2 center)
    {
        var focus = World(center); Camera.Position = focus + new Vector3(0, 27, 21);
        Camera.LookAt(focus); Camera.Size = 27;
    }
}
