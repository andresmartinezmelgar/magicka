using Arcana.Client;
using Arcana.Core;

internal static class AdaptiveAudioTests
{
    private static void Check(bool condition, string reason)
    { if (!condition) throw new Exception(reason); }

    public static void Run(Action<string, Action> test)
    {
        var world = new GameWorld(new() { Bots = 0 });
        int id = world.AddPlayer("Oyente", 0)!.Id;
        var state = world.Snapshot();
        var local = state.Players.Single(p => p.Id == id) with
            { Alive = true, Health = 100, MaxHealth = 100, Position = V2.Zero, Casting = false };
        state = state with { Players = [local], WaitingForPlayers = false, MatchEnded = false, Defense = null };

        test("Audio: aliados, muertos y enemigos lejanos no activan el combate", () =>
        {
            var friend = local with { Id = id + 1, Position = new(2, 0) };
            var enemy = local with { Id = id + 2, Team = 1, Position = new(2, 0) };
            var baseline = state with { Mode = GameMode.Warfare, Players = [local, friend,
                enemy with { Alive = false }, enemy with { Id = id + 3, Position = new(40, 0) }] };
            Check(AdaptiveAudioMix.Evaluate(baseline, id, true, false).Battle == 0, "Amenaza inexistente.");
            Check(AdaptiveAudioMix.Evaluate(baseline with { Players = [local, enemy] }, id, true, false).Battle > 0,
                "No se oye la proximidad del rival.");
            Check(AdaptiveAudioMix.Evaluate(state with { Mode = GameMode.Arena, Players = [local, friend] }, id, true, false).Battle > 0,
                "Todos contra todos usa el equipo en lugar del jugador.");
        });
        test("Audio: poca vida y jefes cercanos elevan el peligro; la pausa lo retira", () =>
        {
            var wounded = state with { Players = [local with { Health = 10 }] };
            Check(AdaptiveAudioMix.Evaluate(wounded, id, true, false).Danger > .9f, "Falta peligro por vida baja.");
            var boss = new MinionSnapshot(9000, MinionKind.Boss, new(8, 0), new(1, 0), 500, 500,
                0, 0, 0, 0, false, [Element.Earth], false);
            var defense = new DefenseSnapshot(0, 1, 0, [boss], [], [], false, 1);
            var encounter = state with { Defense = defense };
            Check(AdaptiveAudioMix.Evaluate(encounter, id, true, false).Danger >= .8f, "Falta peligro de jefe.");
            Check(AdaptiveAudioMix.Evaluate(encounter with { Defense = defense with
                { Minions = [boss with { Position = new(40, 0) }] } }, id, true, false).Danger == 0, "Jefe lejano audible.");
            foreach (var paused in new[] { wounded with { WaitingForPlayers = true }, wounded with { MatchEnded = true },
                encounter with { Defense = defense with { Paused = true } }, wounded with { Players = [local with { Alive = false }] } })
            {
                var levels = AdaptiveAudioMix.Evaluate(paused, id, true, false);
                Check(levels.Battle == 0 && levels.Danger == 0, "La pausa, espera o muerte mantiene la tensión.");
            }
            var quiet = AdaptiveAudioMix.Evaluate(wounded, id, true, true);
            Check(quiet.Battle == 0 && quiet.Danger == 0, "El menú mantiene la tensión.");
        });
        test("Audio: los portales no suenan a Agua; llegada y salida tienen voces reservadas", () =>
        {
            foreach (string kind in new[] { "blink", "blink_arrive", "shield_break" })
            {
                var keys = AdaptiveAudioMix.SoundKeys(new(1, kind, V2.Zero, [], ""));
                Check(keys.SequenceEqual(new[] { kind }) && AdaptiveAudioMix.Critical(kind), "Se pierde el aviso " + kind);
            }
            Check(AdaptiveAudioMix.SoundKeys(new(1, "join", V2.Zero, [], "")).Length == 0, "Evento vacío convertido en Agua.");
            Check(AdaptiveAudioMix.SoundKeys(new(1, "cast", V2.Zero, [], "")).Length == 0, "Hechizo vacío convertido en Agua.");
        });
        test("Audio: mezclas de elementos, barreras y efectos cubiertos por la precarga", () =>
        {
            var bank = AdaptiveAudioMix.PreparationKeys().ToHashSet();
            foreach (string kind in new[] { "cast", "self", "barrier", "mine_place", "impact", "splash", "mine_heal",
                "beam_refract", "beam_merge", "nova", "passive_chain", "arcane_burst", "explosion", "magick" })
                foreach (var element in Enum.GetValues<Element>())
                {
                    var keys = AdaptiveAudioMix.SoundKeys(new(1, kind, V2.Zero, [element], ""));
                    Check(keys.Length > 0 && keys.All(bank.Contains), "Falta precarga para " + kind + "/" + element);
                }
            var mixed = AdaptiveAudioMix.SoundKeys(new(1, "cast", V2.Zero, [Element.Earth, Element.Fire, Element.Arcane], ""));
            Check(mixed.Length == 3 && mixed.Contains("cast:Fire") && mixed.Contains("cast:Arcane"), "Solo suena el primer elemento.");
            Check(AdaptiveAudioMix.SoundKeys(new(1, "cast", V2.Zero, [Element.Fire, Element.Fire, Element.Fire], "")).Length == 1,
                "Las cargas repetidas saturan las voces.");
        });
        test("Audio: el final distingue victoria por equipo y por jugador en todos contra todos", () =>
        {
            Check(AdaptiveAudioMix.Won(state with { Mode = GameMode.Warfare, Winner = local.Team }, local), "Victoria de equipo perdida.");
            Check(AdaptiveAudioMix.Won(state with { Mode = GameMode.Arena, Winner = local.Id }, local), "Victoria individual perdida.");
            Check(!AdaptiveAudioMix.Won(state with { Mode = GameMode.Arena, Winner = local.Id + 1 }, local), "Se anuncia la victoria de otro jugador.");
        });
    }
}
