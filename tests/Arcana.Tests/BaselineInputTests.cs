using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselineInputTests
{
    public static void RunTimeoutAndBots(Action<string, Action> Test)
    {
        Test("Timeout de entrada interrumpe un canal abandonado", () =>
        {
            var (w, a, _) = Setup(); Send(w, a, "input"); Select(w, a, Element.Arcane); Send(w, a, "cast");
            for (int i = 0; i < 70; i++) w.Step();
            Check(a.Channel == null, "El jugador abandonado siguió lanzando.");
        });
        Test("Partida con ocho bots se mantiene válida durante un minuto simulado", () =>
        {
            var world = new GameWorld(new() { Bots = 8 });
            for (int i = 0; i < 3600; i++) world.Step();
            var snapshot = world.Snapshot();
            Check(snapshot.Players.All(p => p.Position.IsFinite && p.Health >= 0 && p.Health <= 1000 && ArenaMap.Inside(p.Position)), "Estado inválido tras combate.");
            Check(snapshot.Events.Any() && snapshot.Players.Any(p => p.Kills > 0 || p.Health < 1000), "Los bots no combatieron.");
        });
    }

    public static void RunReplicationAndCancellation(Action<string, Action> Test)
    {
        Test("Elementos preparados: selección, carga y canal se replican y sus arrays son independientes", () =>
        {
            var (w, a, _) = Setup(); Select(w, a, Element.Earth, Element.Earth, Element.Fire);
            Check(w.Snapshot().Players[0].DisplayElements.SequenceEqual(new[] { Element.Earth, Element.Earth, Element.Fire }), "No se ven los tres elementos preparados.");
            Send(w, a, "input"); Send(w, a, "cast"); var charging = w.Snapshot().Players[0];
            Check(charging.Selected.Length == 0 && charging.Casting && charging.DisplayElements.SequenceEqual(new[] { Element.Earth, Element.Earth, Element.Fire }),
                "Consumir la selección ocultó la roca mientras se carga.");
            charging.CastingElements![0] = Element.Life;
            Check(w.Snapshot().Players[0].CastingElements![0] == Element.Earth, "El snapshot modificó la roca en carga.");
            BaselineTestSupport.Run(w, a, 10); Send(w, a, "release");
            Check(w.Snapshot().Players[0].DisplayElements.Length == 0, "Soltar conservó elementos de la carga.");
            a.ActionCooldown = 0; Select(w, a, Element.Arcane, Element.Arcane); Send(w, a, "cast");
            var copy = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(w.Snapshot()))!; SnapshotValidation.Check(copy);
            Check(copy.Players[0].DisplayElements.SequenceEqual(new[] { Element.Arcane, Element.Arcane }), "La red perdió la combinación del canal.");
            Send(w, a, "release"); Select(w, a, Element.Fire); Select(w, a, Element.Cold);
            Check(w.Snapshot().Players[0].DisplayElements.Length == 0, "La cancelación no vació las casillas.");
        });

        Test("Cancelar una carga por falta de entrada, congelación, otro hechizo o muerte vacía su estado", () =>
        {
            foreach (string cause in new[] { "timeout", "freeze", "replace", "death" })
            {
                var (w, a, b) = Setup();
                Select(w, a, Element.Earth); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 12);
                Check(a.ChargeTime > 0, "La roca no se estaba cargando.");
                if (cause == "timeout") for (int i = 0; i < 40; i++) w.Step();
                if (cause == "freeze") { a.Frozen = 1; BaselineTestSupport.Run(w, a, 1); }
                if (cause == "replace") { Select(w, a, Element.Arcane); Send(w, a, "cast"); }
                if (cause == "death")
                {
                    a.Health = 1; b.Position = new(1, 0);
                    Select(w, b, Element.Arcane); Send(w, b, "nova");
                }
                var state = w.Snapshot().Players.Single(p => p.Id == a.Id);
                Check(a.Charging == null && a.ChargeTime == 0 && state.Charge == 0, "La carga sobrevivió a " + cause);
                if (cause == "death")
                {
                    for (int i = 0; i < 245; i++) w.Step();
                    Check(a.Alive && w.Snapshot().Players.Single(p => p.Id == a.Id).Charge == 0, "La carga reapareció con el mago.");
                }
            }
        });
    }
}
