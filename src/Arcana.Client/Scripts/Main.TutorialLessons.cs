using System.Globalization;
using Arcana.Core;

namespace Arcana.Client;

public partial class Main
{
    private TutorialStep[] BuildTutorialSteps(GameWorld.TutorialScenario scenario)
    {
        var player = scenario.Player;
        var target = scenario.Target;
        string move = Ui.IsMobile ? "Usa el joystick de movimiento." : "Haz clic izquierdo en el suelo o usa las flechas.";
        string cast = Ui.IsMobile ? "Mantén el joystick de apuntado orientado hacia el objetivo." : "Apunta con el ratón y mantén el botón derecho.";
        string barrierCast = Ui.IsMobile ? "Orienta el joystick de apuntado y suéltalo para colocar la barrera." : cast;
        string self = Ui.IsMobile ? "Pulsa «Sobre mí»." : "Pulsa el botón central del ratón.";
        string clear = Ui.IsMobile ? "«Limpiar» borra la selección." : "Retroceso borra la selección.";
        string ElementControl(Element element) => Ui.IsMobile ? Ui.ElementName(element) : $"{Ui.ElementName(element)} ({Ui.ElementKey(element)})";
        string fireArcane = $"{ElementControl(Element.Fire)} y {ElementControl(Element.Arcane)}";
        string shield = ElementControl(Element.Shield), life = ElementControl(Element.Life);
        string water = ElementControl(Element.Water), lightning = ElementControl(Element.Lightning);
        string wetBonus = SpellBalance.WetLightning.ToString("0.##", CultureInfo.GetCultureInfo("es-ES"));
        V2? TargetMarker() => target is { Alive: true } ? target.Position : null;
        V2? FirstWaveMarker() => scenario.Target is { Alive: true } enemy ? enemy.Position : null;
        bool AimedAtTarget()
        {
            if (Ui.IsMobile && _touch?.Aiming != true) return false;
            var aim = (_aim - player.Position).Normalized;
            var direction = (target!.Position - player.Position).Normalized;
            return V2.Dot(aim, direction) > 0.985f;
        }
        void PrepareMovement()
        {
            scenario.ResetExercise();
            player.Position = GameWorld.TutorialScenario.MovementStart;
        }
        void PrepareAim()
        {
            scenario.ResetExercise();
            target!.Position = new(3, -2.5f);
        }
        bool Hits(WorldSnapshot snapshot, params Element[] elements) => snapshot.Beams.Any(beam =>
            beam.Owner == player.Id && elements.All(e => beam.Elements.Contains(e)) &&
            Geometry.DistanceToSegment(target!.Position, beam.From, beam.To) <= 0.85f);
        void PrepareDefenseLesson()
        {
            scenario.PrepareFirstWave();
            if (Ui.IsMobile) player.Health = player.MaxHealth * .55f;
        }
        void PrepareShieldedHealing()
        {
            scenario.PrepareFirstWave();
            player.Health = player.MaxHealth / 2;
            player.Shield = player.ShieldMax = 420;
            player.ShieldTime = 12;
        }
        TutorialStep[] shortcutLessons = Ui.IsMobile ?
        [
            new("Activa hechizos automáticos", "Abre «Menú», activa «Hechizos automáticos» y vuelve al combate. Los ocho elementos se sustituyen por Defensa y Ataque.",
                _ => _tutorialSpellShortcuts.Enabled && !_paused, Prepare: PrepareDefenseLesson),
            new("Defensa automática", "Tienes poca vida. Pulsa «Defensa» y después «Sobre mí» para crear un escudo que también te regenera.",
                _ => player.Shield > 0, FirstWaveMarker),
            new("Cúrate con escudo", "Ya tienes escudo y te falta vida. Pulsa «Defensa» para preparar Vida y luego «Sobre mí» para curarte.",
                _ => player.Health > player.MaxHealth * .65f, Prepare: PrepareShieldedHealing),
            new("Ataque automático", "Pulsa «Ataque» y lanza con el joystick derecho. La mezcla aprovecha tu equipo y el estado del objetivo. Una carga de Fuego basta para aplicar quemadura; las otras pueden aportar daño de otro elemento.",
                _ => _tutorialSpellShortcuts.Enabled && scenario.Target is { } enemy && enemy.Health < enemy.MaxHealth,
                FirstWaveMarker, scenario.PrepareFirstWave),
            new("Vuelve a elegir", $"Abre «Menú» y desactiva «Hechizos automáticos». Vuelve al combate y combina {fireArcane} con los botones de elementos.",
                _ => !_tutorialSpellShortcuts.Enabled && player.Selection.Elements.Contains(Element.Fire) &&
                    player.Selection.Elements.Contains(Element.Arcane), FirstWaveMarker, scenario.PrepareFirstWave)
        ] : [];

        return scenario.Chapter switch
        {
            TutorialChapter.Basics =>
            [
                new("Tu primer entrenamiento", "Practicarás los controles, los cuatro modos y el inventario. El avance se guarda por capítulos."),
                new("Moverte", $"{move} Lleva a tu mago hasta la marca «Objetivo».",
                    _ => V2.Distance(player.Position, GameWorld.TutorialScenario.MovementGoal) < 1.1f,
                    () => GameWorld.TutorialScenario.MovementGoal, PrepareMovement),
                new("Apuntar", Ui.IsMobile ? "Orienta el joystick de apuntado hacia el mago marcado. Este mismo control apunta y lanza." :
                    "Coloca el cursor sobre el mago marcado. El cursor indica hacia dónde saldrá el hechizo.",
                    _ => AimedAtTarget(), TargetMarker, PrepareAim),
                new("Combinar y lanzar", $"Selecciona {fireArcane} para formar un haz de fuego. {cast} Puedes combinar hasta tres elementos o repetirlos. Los incompatibles se cancelan. {clear}",
                    s => target!.Health < target.MaxHealth && Hits(s, Element.Fire, Element.Arcane), TargetMarker, () => scenario.ResetExercise()),
                new("Crear una barrera", $"Selecciona {shield} y lanza hacia delante. {barrierCast} La barrera intercepta hechizos. Escudo con Tierra crea un muro más resistente.",
                    s => s.Walls.Any(w => w.Owner == player.Id && w.Elements.Contains(Element.Shield)), TargetMarker, () => scenario.ResetExercise()),
                new("Protegerte", $"Selecciona {shield}. {self} Ahora el escudo va sobre tu mago y absorbe daño. Combinarlo con otro elemento cambia la protección.",
                    _ => player.Shield > 0, Prepare: () => scenario.ResetExercise()),
                new("Protección elemental", $"Combina {shield} con {water}. {self} La protección de Agua resiste mejor Fuego, pero es débil ante Frío.",
                    _ => player.Shield > 0 && player.Ward.Contains(Element.Water), Prepare: () => scenario.ResetExercise()),
                new("Curarte", $"Estás a media vida. Selecciona {life}. {self} Vida recupera salud y, lanzada hacia delante, puede curar a aliados.",
                    _ => player.Health > player.MaxHealth / 2, Prepare: () => scenario.ResetExercise(injured: true)),
                new("Mojar al objetivo", $"Selecciona {water} y moja al rival. {cast} Agua pura aplica Mojado y empuja. Con Fuego forma vapor y con Frío lanza fragmentos.",
                    _ => target!.Wet > 0, TargetMarker, () => scenario.ResetExercise()),
                new("Aprovechar una debilidad", $"Usa {lightning} contra el rival mojado. Si se seca, aplica Agua de nuevo antes de seleccionar Rayo. Mojado multiplica el daño eléctrico base por {wetBonus}. Si tú estás mojado, también puedes dañarte al lanzar Rayo.",
                    s => target!.Wet > 0 && target.Health < target.MaxHealth && Hits(s, Element.Lightning), TargetMarker),
                new("Contrarrestar estados", "Fuego sobre ti te seca y elimina el frío. Frío ralentiza y congela con más facilidad a un objetivo mojado. El tipo de mago también influye. El Piromante inflige un 25 % más de daño de fuego y recibe un 25 % más de daño de frío."),
                new("Práctica libre", "En el Laboratorio puedes repetir combinaciones, medir efectos sobre objetivos y probar barreras y minas. En Versus IA puedes practicar las reglas de cada modo con bots.")
            ],
            TutorialChapter.Duel =>
            [
                new("Duelo", "Dos magos se enfrentan. Cada baja enemiga suma un punto. Gana quien alcanza primero el límite de la sala. Una serie puede exigir ganar varias rondas."),
                new("Gana el duelo", $"Este duelo se decide con una baja. Apunta al rival marcado y derrótalo con un hechizo. {cast}",
                    s => s.MatchEnded && s.Winner == player.Team, TargetMarker)
            ],
            TutorialChapter.Arena =>
            [
                new("Todos contra todos", "Cada mago tiene su propio marcador. Todos los demás son rivales. Sumas una baja al derribar a un rival y conservas tus puntos al reaparecer."),
                new("Suma dos bajas", "Ataca a los rivales de la arena. En este ejercicio gana el primer mago que alcanza dos bajas.",
                    s => s.MatchEnded && s.Winner == player.Id,
                    () => scenario.World.Players.Values.FirstOrDefault(w => w.Id != player.Id && w.Alive)?.Position)
            ],
            TutorialChapter.Warfare =>
            [
                new("Guerra de puntos", "Controlas el equipo azul. Busca los círculos en el mapa. En la Arena clásica, la ventaja en puntos controlados reduce los tickets del rival. Las bajas también cuestan tickets."),
                new("Captura el punto central", $"{move} Entra en el círculo marcado y mantente dentro hasta que sea azul. Si un rival entra mientras estás dentro, el punto queda disputado y la captura se detiene.",
                    s => s.Points.Any(p => p.Id == 1 && p.Owner == player.Team), () => scenario.World.Map.ControlPoints[1]),
                new("Qué puntos necesita tu equipo", "En la Arena clásica conviene conservar más puntos que el rival. En Ciudadela hay que controlar los tres sin disputa durante 15 segundos. En Frente se capturan los cinco en orden desde tu base.")
            ],
            TutorialChapter.Survival =>
            [
                new("Primer encuentro", "Practica tres acciones con un enemigo de Supervivencia. La horda completa llega al iniciar una partida normal."),
                new("Ataca", $"Selecciona {ElementControl(Element.Fire)}. {cast} Alcanza al enemigo marcado.",
                    _ => scenario.Target is { } enemy && enemy.Health < enemy.MaxHealth, FirstWaveMarker),
                new("Frena al enemigo", $"Selecciona {ElementControl(Element.Cold)}. {cast} Frío ralentiza al enemigo y puede congelarlo si mantienes el chorro.",
                    _ => scenario.Target is { } enemy && (enemy.Chill > 0 || enemy.Frozen > 0), FirstWaveMarker, scenario.PrepareFirstWave),
                new("Protégete", $"Selecciona {shield}. {self} Deja que el enemigo se acerque y golpee tu escudo.",
                    _ => player.Shield > 0 && player.Shield < player.ShieldMax, FirstWaveMarker, PrepareDefenseLesson),
                .. shortcutLessons,
                new("Derrota al enemigo", $"Combina lo aprendido para terminar la primera oleada. {cast}",
                    _ => scenario.Target is { Alive: false }, FirstWaveMarker),
                new("Oleada superada", "En Supervivencia tienes dos vidas. Pasa por encima del botín para recogerlo y abre «Equipo / tienda» para mejorar. En Ayuda tienes la guía de elementos y el Laboratorio para probar mezclas.")
            ],
            TutorialChapter.Inventory =>
            [
                new("Inventario de práctica", "Aquí usarás piezas de ejemplo. En una partida de Supervivencia entras por «Equipo / tienda». El inventario permanente está en «Mi perfil» y se conserva entre partidas."),
                new("Equipar una pieza", "Pulsa la ranura Arma y equipa el arma de Oro que aumenta el daño de Fuego. Cada ranura tiene su propia pieza. Puedes comparar el objeto elegido con el equipado.",
                    _ => _tutorialInventory?.PreviewProfile?.Loadout.Weapon == scenario.TrainingWeaponId),
                new("Ver el total", "Con el arma de Oro elegida, pulsa «Ver estadísticas». Los bonos de las piezas equipadas aparecen en el total. El arma de ejemplo aporta un 18 % de daño de fuego.",
                    _ => _tutorialInventory is { TutorialTotalsVisible: true } inventory &&
                        inventory.PreviewProfile?.Loadout.Weapon == scenario.TrainingWeaponId),
                new("Ataque, movimiento y defensa", "Potencia de ataque mejora el daño general. Velocidad cambia cuánto avanzas. Alcance amplía la distancia de los hechizos. Defensa reduce el daño recibido. Potencia de curación aumenta la vida que recuperas."),
                new("Daño y estados elementales", "Fuego, Agua, Frío, Rayo, Arcano y Tierra potencian su parte de las mezclas. Las duraciones de Mojado y Quemadura alargan esos estados. Potencia de Frío acelera la acumulación y Duración de congelación prolonga el bloqueo. Rayo sobre mojados aumenta el daño en esa condición."),
                new("Escudo, energía y carga", "Capacidad de escudo aumenta lo que absorbe y Duración de escudo el tiempo que dura. Regeneración de energía acelera su recuperación y Eficiencia de Vida reduce el coste de Reanimar. Las velocidades de carga hacen que el haz o la roca alcancen antes su potencia."),
                new("Anillos y conjuntos", "El anillo de oro mejora la obtención de oro. El mágico refuerza el escudo y aporta un efecto según su familia y nivel. Dos o cuatro piezas míticas de la misma familia activan bonos de conjunto."),
                new("Entrenamiento completado", "Ya has practicado los controles, los modos y el equipo. Pulsa «Terminar tutorial» para guardar el recorrido. Puedes repetirlo desde el menú principal.")
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }
}
