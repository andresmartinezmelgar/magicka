using Arcana.Core;

internal static class PracticeTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Send(PracticeSession session, string type, Element element = Element.Water, V2? aim = null)
    {
        var player = session.Player; var at = aim ?? player.Position + new V2(10, 0);
        session.World.ApplyMessage(player.Id, new() { Type = type, Sequence = player.LastSequence + 1, Element = element,
            AimX = at.X, AimY = at.Y });
    }
    private static void Cast(PracticeSession session, string keys, V2 at)
    {
        Send(session, "input", aim: at); Send(session, "clear");
        foreach (var e in SpellCatalogue.FromKeys(keys)!.Elements) Send(session, "element", e);
        Send(session, "cast");
    }
    private static void Steps(PracticeSession session, int count, V2? aim = null)
    { for (int i = 0; i < count; i++) { Send(session, "input", aim: aim); session.Step(); } }
    private static PracticeSession Create()
    { var session = new PracticeSession("Prueba"); session.SetCasterEnabled(false); session.Player.Invulnerable = 0; return session; }
    public static void Run(Action<string, Action> test)
    {
        test("Laboratorio: todas las estaciones, combinaciones y un único respawn sin captura", () =>
        {
            var s = Create(); var map = s.World.Map; var state = s.World.Snapshot();
            Check(state.Players.Length == 16 && s.Targets.Count == 14 && s.Targets.All(t => t.Wizard.Team == 1 && !t.Wizard.Bot), "Faltan objetivos quietos y enemigos.");
            Check(state.Points.Length == 0 && state.Pickups!.Length == 0 && s.World.Scores.All(v => v == 0), "El laboratorio heredó objetivos o puntuación.");
            Check(map.Teleports.Count == 0 && map.EventGates.Count == 0, "El entrenamiento tiene eventos ambientales.");
            for (int team = 0; team < 2; team++) for (int i = 0; i < 16; i++)
                Check(map.Spawn(team, i) == PracticeLayout.Respawn, "Hay más de un punto de respawn.");
            Check(s.Targets.All(t => map.Walkable(t.Definition.Position)) && map.Walkable(PracticeLayout.CasterPosition), "Estación fuera del suelo.");
            Check(PracticeLayout.Barriers.Select(b => b.Combination.Keys).SequenceEqual(SpellCatalogue.All.Where(c => c.Recipe.Shape == SpellShape.Barrier).Select(c => c.Keys)), "Falta una combinación de barrera.");
            foreach (var station in PracticeLayout.Barriers)
                Check(state.Walls.Any(w => (w.A + w.B) / 2 == station.Position && w.Elements.SequenceEqual(station.Combination.Elements)) ||
                    state.Fields.Any(f => f.Position == station.Position && f.Elements.SequenceEqual(station.Combination.Elements)), "Falta la estación " + station.Combination.Keys);
            SnapshotValidation.Check(Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(state))!);
        });
        test("Laboratorio: estados y protecciones no caducan y los objetivos no se mueven", () =>
        {
            var s = Create(); Steps(s, 900);
            foreach (var target in s.Targets)
            {
                var p = target.Wizard; Check(p.Alive && p.Position == target.Definition.Position, "El objetivo desapareció o se movió.");
                Check(target.Definition.Status switch {
                    PracticeStatus.Wet => p.Wet > 0, PracticeStatus.Burning => p.Burning > 0 && p.Health < 1000,
                    PracticeStatus.Chilled => p.Chill > 50, PracticeStatus.Frozen => p.Frozen > 0,
                    PracticeStatus.Haste => p.Haste > 0, PracticeStatus.Protection => p.Shield > 0 && p.ShieldTime > 0,
                    _ => p.Wet == 0 && p.Burning == 0 }, "Caducó un estado inicial: " + target.Definition.Name);
            }
            SnapshotValidation.Check(s.World.Snapshot());
        });
        test("Laboratorio: fuego seca mojado, agua apaga quemado y restablecer devuelve el estado", () =>
        {
            var s = Create(); var wet = s.Targets.Single(t => t.Definition.Status == PracticeStatus.Wet);
            s.Player.Position = wet.Wizard.Position - new V2(3, 0); Cast(s, "FFF", wet.Wizard.Position);
            Steps(s, 40, wet.Wizard.Position); Send(s, "release");
            Check(wet.Wizard.Wet == 0 && wet.Wizard.Burning > 0, "El mantenimiento anuló la interacción entre fuego y agua.");
            s.ResetTarget(wet.Wizard.Id); Check(wet.Wizard.Wet > 0 && wet.Wizard.Burning == 0 && wet.Wizard.Health == 1000, "No se recuperó el objetivo mojado.");
            var burning = s.Targets.Single(t => t.Definition.Status == PracticeStatus.Burning);
            s.Player.Position = burning.Wizard.Position - new V2(3, 0); Cast(s, "Q", burning.Wizard.Position);
            Steps(s, 5, burning.Wizard.Position); Send(s, "release"); Steps(s, 10);
            Check(burning.Wizard.Burning == 0 && burning.Wizard.Wet > 0, "El fuego volvió a encenderse durante la prueba de agua.");
        });
        test("Laboratorio: muerte real, reaparición fija, protección recuperada y sin victoria por bajas", () =>
        {
            var s = Create(); var target = s.Targets.Single(t => t.Definition.Keys == "E");
            s.Player.Position = target.Wizard.Position - new V2(3, 0);
            for (int shot = 0; shot < 4 && target.Wizard.Alive; shot++)
            { Cast(s, "DDD", target.Wizard.Position); Steps(s, 120, target.Wizard.Position); }
            Check(!target.Wizard.Alive && target.Wizard.Deaths == 1 && s.Player.Kills == 1, "No ocurrió una muerte real.");
            Send(s, "release"); Steps(s, 62);
            Check(target.Wizard.Alive && target.Wizard.Position == target.Definition.Position && target.Wizard.Shield == 420 && target.Wizard.Invulnerable == 0, "No volvió la protección en la misma estación.");
            var reading = s.World.PracticeReading(target.Wizard.Id);
            Check(Math.Abs(reading.Damage - 1000) < 0.03 && Math.Abs(reading.Absorbed - 420) < 0.03, "La reaparición borró la medición o contó el exceso de daño.");
            s.Player.Kills = 10000; Steps(s, 600);
            Check(!s.World.MatchEnded && s.World.Scores.All(v => v == 0), "La práctica termina por bajas o tickets.");
        });
        test("Laboratorio: absorción de escudo y curación efectiva se miden por separado", () =>
        {
            var s = Create(); var target = s.Targets.Single(t => t.Definition.Keys == "E");
            s.Player.Position = target.Wizard.Position - new V2(3, 0); Cast(s, "S", target.Wizard.Position);
            Steps(s, 60, target.Wizard.Position); Send(s, "release");
            var meter = s.World.PracticeReading(target.Wizard.Id);
            Check(meter.Damage == 0 && meter.Absorbed > 0 && Math.Abs(meter.Absorbed - (420 - target.Wizard.Shield)) < 0.02, "El escudo se contó como pérdida de vida.");
            s.Player.Health = 990; Send(s, "element", Element.Life); Send(s, "self");
            Check(s.Player.Health == 1000 && Math.Abs(s.World.PracticeReading(s.Player.Id).Healing - 10) < 0.001, "Se contó la sobrecuración.");
        });
        test("Laboratorio: daño y curación simultáneos conservan sus cantidades reales", () =>
        {
            var s = Create(); s.PrepareHealing(); s.Player.Position = PracticeLayout.CasterPosition + new V2(3, 0);
            s.SetCasterSpell("WF"); s.SetCasterEnabled(true); Steps(s, 60);
            var m = s.World.PracticeReading(s.Player.Id);
            Check(m.Damage > 0 && m.Healing > 0 && Math.Abs(s.Player.Health - (400 - m.Damage + m.Healing)) < 0.03, "Solo se midió el cambio neto de vida.");
        });
        test("Laboratorio: regeneración usa las mismas cifras que el combate", () =>
        {
            var s = Create(); var target = s.Targets.Single(t => t.Definition.Keys == "EW"); target.Wizard.Health = 500;
            Steps(s, 60); var meter = s.World.PracticeReading(target.Wizard.Id);
            Check(Math.Abs(target.Wizard.Health - 538) < 0.01 && Math.Abs(meter.Healing - 38) < 0.01, "La regeneración cambió sus valores.");
        });
        test("Laboratorio: mina de vida cura al jugador y se repone sin duplicarse", () =>
        {
            var s = Create(); s.PrepareHealing(); var station = PracticeLayout.Barriers.Single(b => b.Combination.Keys == "WE");
            s.Player.Position = station.Position; Steps(s, 26);
            Check(s.Player.Health == 652 && s.World.PracticeReading(s.Player.Id).Healing == 252, "La mina no curó con su valor normal.");
            Check(!s.World.Snapshot().Fields.Any(f => f.Position == station.Position), "La mina no se consumió.");
            s.Player.Position = PracticeLayout.Respawn; Steps(s, 123);
            Check(s.World.Snapshot().Fields.Count(f => f.Position == station.Position) == 1, "La mina no volvió o se duplicó.");
        });
        test("Laboratorio: barrera destruida se reconstruye tras dos segundos y espera si hay un jugador encima", () =>
        {
            var s = Create(); var station = PracticeLayout.Barriers.Single(b => b.Combination.Keys == "E");
            var wall = s.World.Snapshot().Walls.Single(w => (w.A + w.B) / 2 == station.Position);
            s.Player.Position = station.Position - new V2(3, 0); Cast(s, "DDD", station.Position);
            for (int i = 0; i < 110 && s.World.Snapshot().Walls.Any(w => w.Id == wall.Id); i++) Steps(s, 1, station.Position);
            Send(s, "release"); Check(!s.World.Snapshot().Walls.Any(w => w.Id == wall.Id), "La barrera no se pudo destruir.");
            Steps(s, 119); Check(!s.World.Snapshot().Walls.Any(w => (w.A + w.B) / 2 == station.Position), "La barrera volvió antes de dos segundos.");
            s.Player.Position = station.Position; Steps(s, 15);
            Check(!s.World.Snapshot().Walls.Any(w => (w.A + w.B) / 2 == station.Position), "La barrera encerró al jugador.");
            s.Player.Position -= new V2(3, 0); Steps(s, 1);
            Check(s.World.Snapshot().Walls.Count(w => (w.A + w.B) / 2 == station.Position) == 1, "La barrera no volvió al dejar espacio.");
        });
        test("Laboratorio: lanzador mantiene rayos, conos y cadenas sin interrupciones ni giro", () =>
        {
            foreach (var keys in new[] { "SS", "W", "FFF", "QFF", "AA" })
            {
                var s = Create(); s.SetCasterSpell(keys); s.SetCasterEnabled(true); var castIds = new HashSet<long>();
                for (int i = 0; i < 240; i++)
                {
                    Steps(s, 1); var state = s.World.Snapshot();
                    Check(s.Caster.Channel != null && s.Caster.Facing == PracticeLayout.CasterDirection && s.Caster.Position == PracticeLayout.CasterPosition, "El lanzamiento se cortó o giró: " + keys);
                    Check(state.Beams.Any(b => b.Owner == s.Caster.Id), "Falta un tick de canal: " + keys);
                    foreach (var e in state.Events.Where(e => e.Kind == "cast" && e.Position == PracticeLayout.CasterPosition)) castIds.Add(e.Id);
                    if (i % 60 == 0) SnapshotValidation.Check(state);
                }
                Check(castIds.Count == 1, "El canal se relanzó y perdió su carga.");
            }
        });
        test("Laboratorio: rocas y fragmentos salen exactamente cada 120 ticks", () =>
        {
            foreach (var keys in new[] { "DDD", "DF", "QR" })
            {
                var s = Create(); s.SetCasterSpell(keys); s.SetCasterEnabled(true);
                var seen = new HashSet<long>(); var ticks = new List<long>();
                for (int i = 0; i < 540; i++)
                {
                    Steps(s, 1);
                    foreach (var e in s.World.Snapshot().Events.Where(e => e.Kind == "cast" && e.Position == PracticeLayout.CasterPosition))
                        if (seen.Add(e.Id)) ticks.Add(s.World.Tick);
                }
                Check(ticks.Count >= 4 && ticks.Zip(ticks.Skip(1)).All(p => p.Second - p.First == 120), "La cadencia no es de dos segundos: " + keys);
            }
        });
        test("Laboratorio: toda combinación del lanzador genera un estado válido y se puede pausar", () =>
        {
            var s = Create(); s.SetCasterEnabled(true);
            foreach (var spell in PracticeLayout.CasterSpells)
            {
                s.SetCasterSpell(spell.Keys); Steps(s, 90); SnapshotValidation.Check(s.World.Snapshot());
            }
            s.SetCasterEnabled(false); Steps(s, 2);
            Check(s.Caster.Channel == null && s.Caster.Charging == null && !s.World.Snapshot().Projectiles.Any(p => p.Owner == s.Caster.Id), "Pausar conserva un lanzamiento anterior.");
        });
        test("Laboratorio: el jugador vuelve al único respawn y no se anuncia como sala online", () =>
        {
            var s = Create(); s.Player.Position = PracticeLayout.CasterPosition + new V2(3, 0); s.Player.Health = 1;
            s.SetCasterEnabled(true); Steps(s, 1); Check(!s.Player.Alive, "El lanzador no daña al jugador.");
            s.SetCasterEnabled(false); Steps(s, 62);
            Check(s.Player.Alive && s.Player.Position == PracticeLayout.Respawn && s.Player.Health == 1000, "El respawn cambió de sitio.");
            bool rejected = false;
            try { LobbyValidation.Check(new RoomInfo("practice", "Laboratorio", GameMode.Warfare, 0, 8, 0, true, 15, 75, false, ArenaMapId.Practice)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Se aceptó el laboratorio local como sala online.");
        });
    }
}
