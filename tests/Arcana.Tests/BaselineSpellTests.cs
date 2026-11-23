using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselineSpellTests
{
    public static void RunBasics(Action<string, Action> Test)
    {
        Test("Límite de tres elementos y cancelación de incompatibles", () =>
        {
            var buffer = new ElementBuffer();
            buffer.Add(Element.Fire); buffer.Add(Element.Fire); buffer.Add(Element.Fire);
            Check(!buffer.Add(Element.Earth) && buffer.Elements.Count == 3, "Se admitió un cuarto elemento.");
            buffer.Add(Element.Cold);
            Check(buffer.Elements.Count == 2 && buffer.Elements.All(e => e == Element.Fire), "Frío no anuló un fuego.");
            buffer.Clear(); buffer.Add(Element.Water); buffer.Add(Element.Lightning);
            Check(buffer.Elements.Count == 0, "Agua y rayo no se anularon.");
            buffer.Add(Element.Shield); buffer.Add(Element.Shield);
            Check(buffer.Elements.Count == 0, "Dos escudos no se anularon.");
        });
        Test("Vapor, hielo y prioridad de forma del hechizo", () =>
        {
            Check(new SpellRecipe([Element.Water, Element.Fire]).Steam, "No se resolvió vapor.");
            Check(new SpellRecipe([Element.Water, Element.Cold]).Shape == SpellShape.Shards, "No se resolvió hielo.");
            Check(new SpellRecipe([Element.Earth, Element.Fire, Element.Arcane]).Shape == SpellShape.Rock, "Tierra perdió prioridad.");
            Check(new SpellRecipe([Element.Shield, Element.Fire]).Shape == SpellShape.Barrier, "Escudo perdió prioridad.");
        });
        Test("Toda selección de hasta tres teclas resuelve y serializa", () =>
        {
            foreach (var a in Enum.GetValues<Element>())
            foreach (var b in Enum.GetValues<Element>())
            foreach (var c in Enum.GetValues<Element>())
            {
                var buffer = new ElementBuffer(); buffer.Add(a); buffer.Add(b); buffer.Add(c);
                var recipe = new SpellRecipe(buffer.Elements);
                Check(Enum.IsDefined(recipe.Shape) && recipe.Elements.Length <= 3, "Combinación sin resolver.");
            }
        });
        Test("Snapshot JSON sin recursión y copia independiente del estado", () =>
        {
            var (w, a, _) = Setup();
            var snapshot = w.Snapshot();
            var json = Protocol.Serialize(new ServerMessage { Type = "snapshot", Snapshot = snapshot });
            var copy = Protocol.Deserialize<ServerMessage>(json)!.Snapshot!;
            a.Health = 400; a.Position = new(10, 10);
            Check(copy.Players[0].Health == 1000 && snapshot.Players[0].Position.X == -2, "La copia se modificó con el mundo.");
            Check(!json.Contains("normalized"), "Se serializaron propiedades calculadas del vector.");
        });
        Test("El servidor rechaza NaN y secuencias repetidas", () =>
        {
            var (w, a, _) = Setup();
            Check(!w.ApplyMessage(a.Id, new() { Type = "input", Sequence = 1, AimX = float.NaN }), "Se aceptó una coordenada no finita.");
            Check(w.ApplyMessage(a.Id, new() { Type = "element", Sequence = 2, Element = Element.Fire }), "No se aceptó una intención válida.");
            Check(!w.ApplyMessage(a.Id, new() { Type = "element", Sequence = 2, Element = Element.Fire }), "Se repitió una intención.");
        });
        Test("El rayo arcano aplica daño; al soltar deja de aplicarlo", () =>
        {
            var (w, a, b) = Setup();
            Send(w, a, "input"); w.Step(); Select(w, a, Element.Arcane, Element.Arcane, Element.Arcane);
            Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 30);
            Check(Math.Abs(b.Health - 921.5125f) < 0.05f, "Daño continuo inesperado durante los primeros 0,5 s de carga.");
            Send(w, a, "release"); float health = b.Health; BaselineTestSupport.Run(w, a, 20);
            Check(Math.Abs(health - b.Health) < 0.01, "El hechizo siguió tras soltarlo.");
        });
        Test("Agua y frío congelan; fuego propio seca al mago mojado", () =>
        {
            var (w, a, b) = Setup();
            Select(w, a, Element.Water); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 3); Send(w, a, "release");
            Check(b.Wet > 0, "El agua no mojó al objetivo.");
            Select(w, a, Element.Cold, Element.Cold, Element.Cold); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 45);
            Check(b.Frozen > 0, "El objetivo mojado no se congeló.");
            Send(w, a, "release"); a.Wet = 5; a.Burning = 0; float hp = a.Health;
            Select(w, a, Element.Fire); Send(w, a, "self");
            Check(a.Wet == 0 && a.Health == hp, "Secarse provocó daño o no eliminó mojado.");
        });
        Test("Curación propia, escudo y guarda elemental", () =>
        {
            var (w, a, _) = Setup(); a.Health = 500;
            Select(w, a, Element.Life, Element.Life); Send(w, a, "self");
            Check(a.Health > 700, "No se curó al mago."); BaselineTestSupport.Run(w, a, 30);
            Select(w, a, Element.Shield, Element.Fire); Send(w, a, "self");
            Check(a.Shield > 0 && a.Ward.Contains(Element.Fire), "No se creó la guarda.");
        });

        Test("Escudo: capacidad real, absorción, duración, expiración y cambio de guarda", () =>
        {
            var (w, a, b) = Setup(); Select(w, a, Element.Shield); Send(w, a, "self");
            var state = w.Snapshot().Players.First(p => p.Id == a.Id);
            Check(state.Shield == 420 && state.ShieldMax == 420 && state.ShieldSeconds == 12, "El escudo normal no empieza al 100%.");
            Send(w, b, "input", aim: a.Position); w.Step(); Select(w, b, Element.Arcane, Element.Arcane, Element.Arcane); Send(w, b, "cast");
            for (int i = 0; i < 35; i++) { Send(w, b, "input", aim: a.Position); w.Step(); }
            state = w.Snapshot().Players.First(p => p.Id == a.Id);
            Check(state.Shield is > 0 and < 420 && state.ShieldMax == 420 && a.Health == 1000 && state.ShieldSeconds < 12, "El daño no consumió primero el escudo manteniendo su capacidad.");
            var roundTrip = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(w.Snapshot()))!;
            Check(roundTrip.Players[0].Shield == state.Shield && roundTrip.Players[0].ShieldMax == 420 && roundTrip.Players[0].ShieldSeconds == state.ShieldSeconds, "Se perdieron los valores del escudo al enviarlos.");
            Send(w, b, "release"); for (int i = 0; i < 725; i++) w.Step();
            state = w.Snapshot().Players[0]; Check(state.Shield == 0 && state.ShieldMax == 0 && state.ShieldSeconds == 0, "Quedó un escudo activo tras expirar.");
            Select(w, a, Element.Shield, Element.Fire); Send(w, a, "self");
            state = w.Snapshot().Players[0]; Check(state.Shield == 180 && state.ShieldMax == 180, "La guarda elemental usa una escala incorrecta.");
            for (int i = 0; i < 30; i++) w.Step(); Select(w, a, Element.Shield, Element.Earth); Send(w, a, "self");
            state = w.Snapshot().Players[0]; Check(state.Shield == 650 && state.ShieldMax == 650 && state.ShieldSeconds == 12, "Reaplicar tierra no actualizó la capacidad.");
            SnapshotValidation.Check(w.Snapshot());
        });
        Test("La roca cargada se lanza al soltar y colisiona con el enemigo", () =>
        {
            var (w, a, b) = Setup(); Send(w, a, "input"); w.Step();
            Select(w, a, Element.Earth, Element.Earth, Element.Earth); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 35);
            Check(w.Snapshot().Projectiles.Length == 0 && a.ChargeTime > 0.5f, "Se lanzó antes de soltar.");
            Send(w, a, "release"); BaselineTestSupport.Run(w, a, 20);
            Check(b.Health < 650, "La roca atravesó al objetivo sin aplicar daño.");
        });
    }

    public static void RunAffinities(Action<string, Action> Test)
    {
        Test("Afinidades: bonus y debilidad por elemento para todos los tipos", () =>
        {
            foreach (var type in Enum.GetValues<WizardType>().Where(t => t != WizardType.Balanced))
            {
                var element = WizardTypes.Affinity(type)!.Value; var opposite = WizardTypes.Weakness(type)!.Value;
                float normal = NovaDamage(WizardType.Balanced, WizardType.Balanced, element);
                Check(Math.Abs(NovaDamage(type, WizardType.Balanced, element) - normal * 1.25f) < 0.01f, "No se aplica el bonus de " + type);
                float counter = NovaDamage(WizardType.Balanced, WizardType.Balanced, opposite);
                Check(Math.Abs(NovaDamage(WizardType.Balanced, type, opposite) - counter * 1.25f) < 0.01f, "No se aplica la debilidad de " + type);
            }
            Check(Math.Abs(NovaDamage(WizardType.Pyromancer, WizardType.Cryomancer, Element.Fire) - 58 * 1.5625f) < 0.01f, "Bonus y debilidad no se multiplican.");
        });
        Test("Combinaciones: el bonus no multiplica todo el hechizo ni la curación", () =>
        {
            float mixed = NovaDamage(WizardType.Pyromancer, WizardType.Balanced, Element.Fire, Element.Arcane);
            Check(Math.Abs(mixed - (58 * 1.25f + 115)) < 0.01f, "El bonus de fuego multiplicó el componente arcano.");
            Check(Math.Abs(NovaDamage(WizardType.Pyromancer, WizardType.Balanced, Element.Water, Element.Fire) - (80 * 1.25f + 80)) < 0.01f, "El bonus de fuego multiplicó también el agua del vapor.");
            var w = new GameWorld(); var p = w.AddPlayer("Piromante", wizardType: WizardType.Pyromancer)!; p.Health = 500;
            Select(w, p, Element.Life); Send(w, p, "self"); Check(p.Health == 645, "La afinidad alteró la curación.");
        });
        Test("La quemadura conserva la potencia del piromante al desconectarse", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Arena });
            var a = w.AddPlayer("Fuego", wizardType: WizardType.Pyromancer)!; var b = w.AddPlayer("Hielo", wizardType: WizardType.Cryomancer)!;
            a.Position = new(-2, 0); b.Position = new(1, 0); a.Invulnerable = b.Invulnerable = 0;
            Select(w, a, Element.Fire); Send(w, a, "nova"); float health = b.Health; w.RemovePlayer(a.Id);
            for (int i = 0; i < 60; i++) w.Step();
            Check(Math.Abs(health - b.Health - 28 * 1.5625f) < 0.02f, "La quemadura perdió el bonus o la debilidad.");
        });
        Test("La debilidad consume más escudo y los efectos de impacto tienen límite", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Arena });
            var a = w.AddPlayer("Hielo", wizardType: WizardType.Cryomancer)!; var b = w.AddPlayer("Fuego", wizardType: WizardType.Pyromancer)!;
            a.Position = new(-2, 0); b.Position = new(1, 0); a.Invulnerable = b.Invulnerable = 0;
            b.Shield = b.ShieldMax = 420; b.ShieldTime = 12;
            Select(w, a, Element.Cold); Send(w, a, "nova");
            Check(Math.Abs(b.Shield - (420 - 24 * 1.5625f)) < 0.01f && b.Health == 1000, "La debilidad ignoró el escudo.");
            Check(w.Snapshot().Events.Any(e => e.Kind == "shield_hit"), "Falta el impacto de escudo.");
            a.ActionCooldown = 0; b.Shield = 1; Select(w, a, Element.Cold); Send(w, a, "nova");
            Check(w.Snapshot().Events.Count(e => e.Kind == "shield_break") == 1 && b.Shield == 0, "La rotura se perdió por el límite de efectos.");
            b.Health = 1000; b.Chill = b.Frozen = 0; a.ActionCooldown = 0;
            Select(w, a, Element.Arcane); Send(w, a, "cast");
            for (int i = 0; i < 60; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
            Check(w.Snapshot().Events.Count(e => e.Kind == "hurt") <= 9, "Se emite un impacto por cada tick del rayo.");
        });
        Test("El tipo es válido, se replica y no puede cambiar con una intención", () =>
        {
            var w = new GameWorld(); Check(w.AddPlayer("Inválido", wizardType: (WizardType)999) == null, "Se admitió un tipo desconocido.");
            var p = w.AddPlayer("Mago", wizardType: WizardType.Geomancer)!;
            w.ApplyMessage(p.Id, new() { Type = "input", Sequence = 1, WizardType = WizardType.Pyromancer });
            var state = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(w.Snapshot()))!;
            Check(state.Players[0].WizardType == WizardType.Geomancer, "Una intención cambió el tipo o no se replicó.");
            bool rejected = false;
            try { SnapshotValidation.Check(state with { Players = [state.Players[0] with { WizardType = (WizardType)999 }] }); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "El cliente aceptó un tipo desconocido.");
        });
    }

    public static void RunFieldsAndBeams(Action<string, Action> Test)
    {
        Test("Escudo de vida: regeneración gradual, límite, rotura y sustitución", () =>
        {
            var (world, a, _) = Setup(); a.Health = 500;
            Select(world, a, Element.Shield, Element.Life); Send(world, a, "self");
            Check(a.Health == 500 && a.RegenerationRate == 38, "El escudo curó instantáneamente o no activó regeneración.");
            for (int i = 0; i < 120; i++) world.Step();
            Check(Math.Abs(a.Health - 576) < 0.03f, "La regeneración no depende del tiempo simulado.");
            var copy = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()))!;
            Check(copy.Players[0].RegenerationRate == 38, "La tasa no se replica.");
            a.Shield = 0; float hp = a.Health; world.Step();
            Check(a.Health == hp && a.RegenerationRate == 0, "Se siguió curando sin escudo.");
            a.Health = 990; Select(world, a, Element.Shield, Element.Life, Element.Life); Send(world, a, "self");
            for (int i = 0; i < 60; i++) world.Step();
            Check(a.Health == 1000 && a.RegenerationRate == 76, "La curación superó el máximo o no cuenta dos vidas.");
            Select(world, a, Element.Shield); Send(world, a, "self");
            Check(a.RegenerationRate == 0, "Sustituir el escudo conservó regeneración.");
        });
        Test("Mina de vida: ignora enemigos y cura aliados y propietario una sola vez", () =>
        {
            var (world, a, b) = Setup(GameMode.Warfare, false);
            var ally = world.AddPlayer("Aliado", 0)!; ally.Position = new(8, 0); ally.Invulnerable = 0;
            a.Position = new(-5, 0); a.Facing = new(1, 0); a.Health = 500;
            b.Position = new(-1.8f, 0); b.Health = 500;
            Select(world, a, Element.Shield, Element.Life); Send(world, a, "cast");
            Check(!world.Snapshot().Fields[0].Armed, "La mina apareció armada sin demora.");
            for (int i = 0; i < 40; i++) world.Step();
            Check(world.Snapshot().Fields.Length == 1 && b.Health == 500, "Un enemigo consumió o se benefició de la mina de vida.");
            ally.Position = new(-1.8f, 0); ally.Health = 600; a.Position = new(-2.4f, 0);
            world.Step();
            Check(world.Snapshot().Fields.Length == 0 && Math.Abs(ally.Health - 852) < 0.01f && Math.Abs(a.Health - 752) < 0.01f && b.Health == 500,
                "La mina no respetó los destinatarios de la curación.");
            float health = ally.Health; world.Step(); Check(ally.Health == health, "Una mina explotó dos veces.");
            Check(world.Snapshot().Events.Any(e => e.Kind == "mine_heal" && e.Radius == 2.4f), "Falta la señal visual con su radio real.");
        });
        Test("Mina ofensiva: activación por enemigo, caducidad y limpieza al desconectar", () =>
        {
            var (world, a, b) = Setup(GameMode.Warfare, true);
            var ally = world.AddPlayer("Aliado", 0)!; ally.Invulnerable = 0;
            a.Position = new(-5, 0); a.Facing = new(1, 0); b.Position = new(9, 0); ally.Position = new(-1.8f, 0);
            Select(world, a, Element.Shield, Element.Arcane); Send(world, a, "cast");
            for (int i = 0; i < 40; i++) world.Step();
            Check(world.Snapshot().Fields.Length == 1 && ally.Health == 1000, "Un aliado activó la mina ofensiva.");
            b.Position = new(-1.8f, 0); world.Step();
            Check(b.Health < 1000 && world.Snapshot().Fields.Length == 0, "El enemigo no activó la mina.");
            a.ActionCooldown = 0; b.Position = ally.Position = new(9, 0);
            Select(world, a, Element.Shield, Element.Arcane); Send(world, a, "cast");
            for (int i = 0; i < 725; i++) world.Step();
            Check(world.Snapshot().Fields.Length == 0, "La mina no caducó.");
            a.ActionCooldown = 0; Select(world, a, Element.Shield, Element.Arcane); Send(world, a, "cast");
            world.RemovePlayer(a.Id); Check(world.Snapshot().Fields.Length == 0, "Una mina sobrevivió a su propietario desconectado.");
        });
        Test("Rocas: impacto directo, splash selectivo y suelo con caducidad", () =>
        {
            foreach (var elements in new Element[][] { [Element.Earth], [Element.Earth, Element.Arcane], [Element.Earth, Element.Fire], [Element.Earth, Element.Water], [Element.Earth, Element.Cold] })
            {
                var (world, a, b) = Setup(); var neighbor = world.AddPlayer("Vecino")!;
                a.Position = new(-5, 0); b.Position = new(-1.5f, 0); neighbor.Position = new(-1.2f, 1.3f); neighbor.Invulnerable = 0;
                Select(world, a, elements); Send(world, a, "cast"); BaselineTestSupport.Run(world, a, 40); Send(world, a, "release");
                for (int i = 0; i < 35 && !world.Snapshot().Events.Any(e => e.Kind == "impact"); i++) { Send(world, a, "input"); world.Step(); }
                Check(b.Health < 1000, "La roca no golpeó al objetivo.");
                if (elements.Length == 1) Check(neighbor.Health == 1000 && world.Snapshot().Fields.Length == 0, "Tierra pura creó un splash.");
                else
                {
                    Check(neighbor.Health < 1000 && world.Snapshot().Events.Any(e => e.Kind == "splash" && e.Radius > 0), "Falta el splash de una roca imbuida.");
                    string? kind = new SpellRecipe(elements).GroundKind;
                    Check(kind == null ? world.Snapshot().Fields.Length == 0 : world.Snapshot().Fields.Any(f => f.Kind == kind), "El suelo no corresponde a la combinación.");
                }
                for (int i = 0; i < 250; i++) world.Step();
                Check(world.Snapshot().Fields.Length == 0, "Un campo de suelo no terminó.");
                SnapshotValidation.Check(world.Snapshot());
            }
        });
        Test("Dos rayos frontales suman efectos y desvían la salida sin atravesar el cruce", () =>
        {
            var (world, a, b) = Setup(); var target = world.AddPlayer("Salida")!;
            a.Position = new(-5, 0); b.Position = new(5, 0); target.Position = new(0, 4); target.Invulnerable = 0;
            Send(world, a, "input", aim: b.Position); Send(world, b, "input", aim: a.Position); world.Step();
            Select(world, a, Element.Arcane); Select(world, b, Element.Arcane); Send(world, a, "cast"); Send(world, b, "cast");
            for (int i = 0; i < 30; i++) { Send(world, a, "input", aim: b.Position); Send(world, b, "input", aim: a.Position); world.Step(); }
            var merged = world.Snapshot().Beams.Single(ray => ray.Interaction == "merged");
            Check(merged.Elements.SequenceEqual(new[] { Element.Arcane, Element.Arcane }) && Math.Abs(merged.Power - 1.2f) < 0.0001f && merged.To.Y > merged.From.Y,
                "No se sumaron los componentes o no se desvió el rayo.");
            Check(a.Health == 1000 && b.Health == 1000 && Math.Abs(target.Health - 947.675f) < 0.05f, "La salida no suma ambos daños o las entradas atraviesan el cruce.");
            Check(world.Snapshot().Events.Any(e => e.Kind == "beam_merge"), "No se replicó el destello del cruce.");
            SnapshotValidation.Check(Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()))!);
        });
        Test("Láser: el daño aumenta con el tiempo, alcanza el 150 % en dos segundos y se mantiene", () =>
        {
            var (w, a, b) = Setup(); b.MaxHealth = b.Health = 10000;
            Select(w, a, Element.Arcane); Send(w, a, "cast");
            BaselineTestSupport.Run(w, a, 1);
            Check(Math.Abs(10000 - b.Health - 115 * 0.31f / 60) < 0.002f, "El primer paso no empieza con daño bajo.");
            Check(Math.Abs(w.Snapshot().Beams.Single().Power - 0.31f) < 0.0001f, "La intensidad inicial no se replica.");
            BaselineTestSupport.Run(w, a, 59);
            float before = b.Health; BaselineTestSupport.Run(w, a, 1);
            Check(Math.Abs(before - b.Health - 115 * 0.91f / 60) < 0.002f && Math.Abs(a.BeamCharge - 61f / 120) < 0.0001f,
                "El daño o la barra de carga no progresan a mitad del lanzamiento.");
            BaselineTestSupport.Run(w, a, 59);
            var full = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(w.Snapshot()))!;
            Check(full.Players.Single(p => p.Id == a.Id).Charge == 1 && Math.Abs(full.Beams.Single().Power - 1.5f) < 0.0001f,
                "La carga completa no llega a la red a los dos segundos.");
            before = b.Health; BaselineTestSupport.Run(w, a, 180);
            Check(a.BeamTicks == 120 && Math.Abs(before - b.Health - 172.5f * 3) < 0.15f,
                "El láser supera el límite o no mantiene 172,5 daño/s a plena carga.");
            SnapshotValidation.Check(full);
        });
        Test("Láser: soltar, sustituir, autocast, timeout, congelación y muerte reinician la carga", () =>
        {
            foreach (string cause in new[] { "release", "recast", "replace", "self", "timeout", "freeze", "death" })
            {
                var (w, a, b) = Setup(); b.Position = new(18, 8);
                Select(w, a, Element.Arcane); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 120);
                Check(a.BeamCharge == 1, "No llegó a carga completa antes de " + cause);
                switch (cause)
                {
                    case "release": Send(w, a, "release"); break;
                    case "recast": Select(w, a, Element.Arcane); Send(w, a, "cast"); break;
                    case "replace": Select(w, a, Element.Earth); Send(w, a, "cast"); break;
                    case "self": Select(w, a, Element.Life); Send(w, a, "self"); break;
                    case "timeout": for (int i = 0; i < 40; i++) w.Step(); break;
                    case "freeze": a.Frozen = 1; BaselineTestSupport.Run(w, a, 1); break;
                    case "death":
                        a.Health = 1; b.Position = new(1, 0); Select(w, b, Element.Arcane); Send(w, b, "nova"); break;
                }
                Check(a.BeamTicks == 0 && w.Snapshot().Players.Single(p => p.Id == a.Id).Charge == 0,
                    "La carga se conservó tras " + cause);
                if (cause == "death") for (int i = 0; i < 245; i++) w.Step();
                a.Frozen = 0; a.ActionCooldown = 0;
                Select(w, a, Element.Arcane); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 1);
                Check(Math.Abs(w.Snapshot().Beams.Single(ray => ray.Owner == a.Id && ray.Interaction == "direct").Power - 0.31f) < 0.0001f,
                    "El siguiente láser heredó la potencia tras " + cause);
            }
        });
        Test("Láseres cruzados conservan el tiempo individual y los reflejados conservan la potencia", () =>
        {
            var (w, a, b) = Setup(); var target = w.AddPlayer("Salida")!;
            a.Position = new(-5, 0); b.Position = new(5, 0); target.Position = new(0, 4); target.Invulnerable = 0;
            Send(w, a, "input", aim: b.Position); w.Step(); Select(w, a, Element.Arcane); Send(w, a, "cast");
            for (int i = 0; i < 119; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
            Send(w, b, "input", aim: a.Position); w.Step(); Select(w, b, Element.Arcane); Send(w, b, "cast");
            float before = target.Health;
            Send(w, a, "input", aim: b.Position); Send(w, b, "input", aim: a.Position); w.Step();
            Check(Math.Abs(w.Snapshot().Beams.Single(ray => ray.Interaction == "merged").Power - 1.81f) < 0.0001f &&
                Math.Abs(before - target.Health - 115 * 1.81f / 60) < 0.001f, "El cruce igualó la carga de los dos magos.");
            var (shielded, caster, defender) = Setup(); defender.Position = new(18, 8);
            Select(shielded, caster, Element.Arcane); Send(shielded, caster, "cast"); BaselineTestSupport.Run(shielded, caster, 120);
            defender.Position = new(2, 0); Select(shielded, defender, Element.Shield); Send(shielded, defender, "self");
            float health = caster.Health, shield = defender.Shield; BaselineTestSupport.Run(shielded, caster, 1);
            Check(Math.Abs(shielded.Snapshot().Beams.Single(ray => ray.Interaction == "reflected").Power - 1.2f) < 0.0001f &&
                Math.Abs(health - caster.Health - 115 * 1.2f / 60) < 0.001f &&
                Math.Abs(shield - defender.Shield - 115 * 1.5f * 0.5f * 0.2f / 60) < 0.001f,
                "La reflexión reinició la carga o calculó mal daño y presión.");
        });
        Test("La carga del láser aumenta solo el daño: curación y frío conservan su tasa", () =>
        {
            foreach (var elements in new[] { new[] { Element.Life, Element.Fire }, new[] { Element.Arcane, Element.Cold } })
            {
                var (w, a, b) = Setup(); b.Position = new(18, 8);
                Select(w, a, elements); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 120);
                b.Position = new(2, 0); b.Health = 500; b.Chill = 0; BaselineTestSupport.Run(w, a, 1);
                if (elements.Contains(Element.Life))
                    Check(Math.Abs(b.Health - (500 + (140 - 58 * 1.5f) / 60)) < 0.001f,
                        "La carga multiplicó también la curación o la quemadura inmediata.");
                else Check(Math.Abs(b.Chill - 38f / 60) < 0.001f, "El frío se aplica más rápido por la carga del láser.");
            }
        });
        Test("Rayos y escudos: reflexión, refracción elemental y rebotes acotados", () =>
        {
            foreach (bool elemental in new[] { false, true })
            {
                var (world, a, b) = Setup(); Send(world, a, "input", aim: b.Position); Send(world, b, "input", aim: a.Position); world.Step();
                Select(world, b, elemental ? [Element.Shield, Element.Fire] : [Element.Shield]); Send(world, b, "self");
                Select(world, a, Element.Arcane, Element.Arcane); Send(world, a, "cast");
                for (int i = 0; i < 20; i++) { Send(world, a, "input", aim: b.Position); world.Step(); }
                var diverted = world.Snapshot().Beams.FirstOrDefault(ray => ray.Interaction == (elemental ? "refracted" : "reflected"));
                Check(diverted != null && b.Shield < b.ShieldMax && b.Health == 1000, "El escudo no desvió o no consumió energía.");
                if (elemental) Check(diverted!.Elements.Contains(Element.Fire) && Math.Abs(diverted.To.Y - diverted.From.Y) > 0.2f, "La guarda no añadió su elemento y ángulo.");
                else Check(a.Health < 1000, "La reflexión no puede alcanzar al lanzador.");
                Check(world.Snapshot().Beams.Length <= 3, "El rayo produjo una cadena de rebotes sin límite.");
                SnapshotValidation.Check(world.Snapshot());
            }
        });
        Test("Una barrera impide combinar rayos al otro lado", () =>
        {
            var (world, a, b) = Setup(); a.Position = new(-5, 0); b.Position = new(5, 0);
            Send(world, b, "input", aim: a.Position); world.Step(); Select(world, b, Element.Shield, Element.Earth); Send(world, b, "cast");
            for (int i = 0; i < 45; i++) world.Step();
            Send(world, a, "input", aim: b.Position); Send(world, b, "input", aim: a.Position); world.Step();
            Select(world, a, Element.Arcane); Select(world, b, Element.Arcane); Send(world, a, "cast"); Send(world, b, "cast"); world.Step();
            Check(!world.Snapshot().Beams.Any(ray => ray.Interaction == "merged"), "Los rayos se combinaron a través de la barrera.");
        });
    }

    public static void RunImpactRegressions(Action<string, Action> Test)
    {
        Test("Una descarga de hielo crea todos los fragmentos y un único evento de lanzamiento", () =>
        {
            var (w, a, _) = Setup(); Select(w, a, Element.Water, Element.Cold, Element.Cold); Send(w, a, "cast");
            var state = w.Snapshot();
            Check(state.Projectiles.Length == 5, "Cambió el número de fragmentos.");
            Check(state.Events.Count(e => e.Kind == "cast") == 1, "Cada fragmento duplicó el sonido y los efectos del lanzamiento.");
        });
        Test("El empuje no permite saltar pilares ni muros entre dos posiciones transitables", () =>
        {
            var (w, a, _) = Setup(); a.Position = new(10, 0); a.Knockback = new(360, 0); BaselineTestSupport.Run(w, a, 1);
            Check(a.Position.X < 13 - 1.3f - ArenaMap.WizardRadius, "El empuje atravesó un pilar.");
            var (walls, caster, _) = Setup(); Select(walls, caster, Element.Shield, Element.Earth); Send(walls, caster, "cast");
            Check(walls.Snapshot().Walls.Length == 1, "Falta el muro de prueba.");
            caster.Knockback = new(360, 0); BaselineTestSupport.Run(walls, caster, 1);
            Check(caster.Position.X < 1.2f - ArenaMap.WizardRadius - 0.18f, "El empuje atravesó un muro.");
        });
        Test("La roca impacta exactamente en la superficie de un muro", () =>
        {
            var (w, a, b) = Setup(); b.Position = new(8, 8);
            Select(w, a, Element.Shield, Element.Fire); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 43);
            Select(w, a, Element.Earth); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 40); Send(w, a, "release");
            float radius = w.Snapshot().Projectiles.Single().Radius;
            for (int i = 0; i < 20 && !w.Snapshot().Events.Any(e => e.Kind == "impact"); i++) BaselineTestSupport.Run(w, a, 1);
            var impact = w.Snapshot().Events.Single(e => e.Kind == "impact");
            Check(Math.Abs(impact.Position.X - (1.2f - radius - 0.2f)) < 0.001f, "La colisión depende del muestreo y penetra en el muro.");
        });
        Test("Un muro destruido o caducado deja de aplicar efectos de contacto en ese paso", () =>
        {
            foreach (bool destroyed in new[] { true, false })
            {
                var (w, a, b) = Setup(); b.Position = new(8, 8);
                Select(w, a, Element.Shield, Element.Fire); Send(w, a, "cast"); BaselineTestSupport.Run(w, a, 43);
                if (destroyed) { Select(w, a, Element.Water, Element.Water, Element.Water); Send(w, a, "cast"); }
                float pressure = 110 * 3 * 1.5f * GameWorld.StepSeconds;
                for (int i = 0; i < 450; i++)
                {
                    var wall = w.Snapshot().Walls.Single();
                    if (destroyed ? wall.Health <= pressure : wall.Lifetime <= GameWorld.StepSeconds) break;
                    BaselineTestSupport.Run(w, a, 1);
                }
                b.Position = new(1.2f, 2.95f); b.Invulnerable = 0;
                BaselineTestSupport.Run(w, a, 1);
                Check(w.Snapshot().Walls.Length == 0, "El muro no terminó en el paso esperado.");
                Check(b.Health == 1000 && b.Burning == 0, "Un muro que ya terminó todavía quemó al mago.");
            }
        });
        Test("Cada componente de un rayo combinado conserva el bonus de daño de su autor contra un escudo", () =>
        {
            foreach (bool firstBoosted in new[] { true, false })
            {
                var w = new GameWorld(new() { Mode = GameMode.Arena, Map = ArenaMapId.Relics });
                var a = w.AddPlayer("A")!; var b = w.AddPlayer("B")!; var target = w.AddPlayer("Escudo")!;
                a.Position = new(-5, 0); b.Position = new(5, 0); target.Position = new(0, 4);
                a.Invulnerable = b.Invulnerable = target.Invulnerable = 0;
                (firstBoosted ? a : b).BuffStacks[(int)BuffKind.Power] = 3;
                Send(w, a, "input", aim: b.Position); Send(w, b, "input", aim: a.Position); w.Step();
                target.Shield = target.ShieldMax = 420; target.ShieldTime = 12;
                Select(w, a, Element.Arcane, Element.Arcane); Select(w, b, Element.Arcane, Element.Arcane);
                Send(w, a, "cast"); Send(w, b, "cast");
                Send(w, a, "input", aim: b.Position); Send(w, b, "input", aim: a.Position); w.Step();
                float expected = 230 * (1 + RelicBuffs.Power(firstBoosted ? a : b)) * 0.31f * 0.5f * 0.2f * GameWorld.StepSeconds;
                Check(Math.Abs(420 - target.Shield - expected) < 0.001f, "El bonus del primer mago se aplicó a ambos rayos.");
            }
        });
    }
}
