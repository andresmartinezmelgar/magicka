using Arcana.Core;
using System.Globalization;

namespace Arcana.Client;

internal enum HelpTopic { Start, Controls, Spells, Elements, Modes, Equipment, Relics, Wizards }
internal sealed record HelpSection(HelpTopic Id, string Title, string Summary);
internal sealed record HelpArticle(HelpTopic Topic, string Id, string Title, string Body);

/// <summary>Una sola fuente de contenido para el índice, la búsqueda y la ayuda en partida.</summary>
internal static class HelpCatalogue
{
    internal static readonly HelpSection[] Sections =
    [
        new(HelpTopic.Start, "Primeros pasos", "Aprende a moverte, atacar y protegerte."),
        new(HelpTopic.Controls, "Controles", "Qué hace cada botón en tu dispositivo."),
        new(HelpTopic.Spells, "Crear hechizos", "Combina elementos y prueba el resultado."),
        new(HelpTopic.Elements, "Elementos y defensas", "Ventajas, estados y protecciones."),
        new(HelpTopic.Modes, "Modos de juego", "Objetivos, supervivencia y prácticas."),
        new(HelpTopic.Equipment, "Equipo y mejoras", "Atributos, anillos y efectos especiales."),
        new(HelpTopic.Relics, "Reliquias", "Qué recoges y cuánto duran sus efectos."),
        new(HelpTopic.Wizards, "Tipos de mago", "Elige afinidad y conoce su debilidad.")
    ];

    internal static HelpArticle[] Build(bool mobile)
    {
        var articles = new List<HelpArticle>();
        void Add(HelpTopic topic, string id, string title, string body) => articles.Add(new(topic, id, title, body));
        Add(HelpTopic.Start, "first-spell", "1. Muévete y lanza tu primer hechizo",
            mobile ? "Arrastra el joystick izquierdo para moverte. Pulsa Fuego hasta tres veces y mantén el joystick derecho hacia un objetivo para lanzar. Suéltalo para parar."
                : "Camina con clic izquierdo o las flechas. Pulsa F tres veces para preparar Fuego y mantén clic derecho hacia un objetivo para lanzar. Suelta para parar.");
        Add(HelpTopic.Start, "protect", "2. Cúrate y protégete",
            mobile ? "Selecciona Vida y pulsa «Sobre mí» para curarte. Con Escudo y «Sobre mí» creas una protección personal. Vacía la mezcla anterior antes de preparar otra."
                : "Pulsa W y el botón central para curarte. Pulsa E y el botón central para crear un escudo personal. Retroceso vacía la mezcla anterior.");
        Add(HelpTopic.Start, "next-step", "3. Practica antes de entrar en combate",
            "El tutorial te guía con objetivos cortos. En Prácticas puedes probar daño, curación y barreras. Después, entra en Versus IA o PvP y consulta el objetivo de la partida antes de empezar.");

        Add(HelpTopic.Controls, "move", "Moverse y apuntar", mobile
            ? "Joystick izquierdo: arrastra para moverte; soltar detiene al mago.\nJoystick derecho: apunta y lanza mientras mantienes el dedo."
            : "Clic izquierdo: camina hacia el cursor.\nFlechas: movimiento directo.\nRueda: acerca o aleja la cámara.");
        Add(HelpTopic.Controls, "cast", "Seleccionar elementos y lanzar", mobile
            ? "Pulsa los ocho botones de elementos para preparar hasta tres cargas. Puedes repetir un elemento.\nMantén el joystick derecho para lanzar; al soltar, interrumpes el hechizo o disparas la roca cargada.\n«Sobre mí»: curación, escudo o secado.\n«En área»: pulso alrededor del mago."
            : "Q: Agua · W: Vida · E: Escudo · R: Frío\nA: Rayo · S: Arcano · D: Tierra · F: Fuego\nMantener clic derecho: lanzar o cargar una roca.\nSoltar clic derecho: interrumpir o disparar la roca cargada.\nBotón central: lanzar sobre ti.\nMayús + clic derecho: lanzar en área.\nMayús + clic izquierdo: golpe de bastón.\nRetroceso: vaciar los elementos.");
        Add(HelpTopic.Controls, "specials", "Especiales, marcador y menú", mobile
            ? "Los cuatro botones especiales activan tus magias equipadas: celeridad, traslación, meteoro o reanimación.\n«Tabla» abre la clasificación. «Ayuda» abre esta guía. «Menú» abre la pausa. Atrás cierra primero la pantalla que tienes abierta."
            : "1, 2, 3 y 4: activar las magias especiales equipadas.\nTAB: clasificación.\nF1: esta guía.\nESC: cerrar la pantalla actual o abrir el menú de partida.");
        Add(HelpTopic.Controls, "custom-controls", "Cambiar joystick y botones",
            "Entra en Mi perfil → Configurar joystick y botones. Ajusta posición, tamaño y comportamiento de los controles y guarda los cambios antes de volver.");

        Add(HelpTopic.Spells, "mix", "Cómo funciona una mezcla",
            "Selecciona hasta tres elementos, incluidos repetidos. Después elige cómo lanzar: Frontal hacia el objetivo, Sobre mí para aplicarlo a tu mago o En área a tu alrededor. El mismo grupo de elementos puede producir efectos distintos según el lanzamiento.");
        Add(HelpTopic.Spells, "opposites", "Por qué desaparece un elemento",
            "Los opuestos se anulan al seleccionarlos: Fuego y Frío, Vida y Arcano, Agua y Rayo, Tierra y Rayo. Dos Escudos también se anulan. Puedes repetir los demás elementos hasta llenar las tres cargas.");
        Add(HelpTopic.Spells, "recipes", "Mezclas para empezar",
            "Fuego + Fuego + Fuego: chorro de fuego.\nVida + Vida, Sobre mí: curación.\nEscudo, Sobre mí: escudo personal.\nAgua + Frío: hielo.\nAgua + Fuego: vapor.\nTierra + Tierra + Fuego: roca incendiaria.\nAgua y después Frío: moja al objetivo antes de congelarlo.");
        Add(HelpTopic.Spells, "allies", "Fuego amigo y barreras",
            "Las magias pueden afectar también a tus aliados. El escudo básico deja pasar tus propios hechizos; las barreras imbuidas también pueden bloquearlos. Todas las barreras bloquean el movimiento. Arcano avanza recto y puede reflejarse en escudos.");

        string[] roles = ["Moja al objetivo; no quita salud al romper una protección.", "Cura; usarla sobre un aliado conserva su escudo.",
            "Crea protecciones personales, barreras o guardas según la mezcla y el lanzamiento.", "Enfría; sobre un objetivo mojado permite congelarlo.",
            "Descarga electricidad; evita usarlo mientras estás mojado.", "Forma haces de energía que avanzan en línea recta.",
            "Crea proyectiles de roca; mantén el lanzamiento para cargarlos y suelta para disparar.", "Quema y sirve para secarte al aplicarlo sobre ti."];
        foreach (var element in Enum.GetValues<Element>())
            Add(HelpTopic.Elements, "element-" + element, Ui.ElementName(element),
                roles[(int)element] + "\n\nSupera a " + Ui.ElementName(ElementInteractions.StrongAgainst(element)) +
                ". Es vulnerable a " + Ui.ElementName(ElementInteractions.Weakness(element)) + ".");
        Add(HelpTopic.Elements, "protection", "Cómo se calcula una protección elemental",
            "Contra una protección: +50 % al superar su elemento, −50 % si te contrarresta y −82 % contra el mismo elemento. Cada componente de una mezcla se compara con la defensa por separado. La afinidad y la debilidad del mago se aplican además. Agua y Vida desgastan protecciones con sus ventajas, pero esa presión no daña la salud al romperlas.");

        foreach (var mode in new[] { GameMode.Defense, GameMode.Duel, GameMode.Arena, GameMode.Warfare })
            Add(HelpTopic.Modes, "mode-" + mode, Ui.ModeName(mode),
                MatchRules.Objective(mode, 15, 75) + "\n\n" + MatchRules.Details(mode));
        Add(HelpTopic.Modes, "practice", "Prácticas", MatchRules.Objective(GameMode.Warfare, 15, 75, ArenaMapId.Practice) +
            "\n\n" + MatchRules.Details(GameMode.Warfare, ArenaMapId.Practice));
        foreach (var (map, name) in new[] { (ArenaMapId.Citadel, "Ciudadela de los Pasillos"),
            (ArenaMapId.Frontline, "Frente de batalla"), (ArenaMapId.Relics, "Santuario de las Reliquias"),
            (ArenaMapId.Ascension, "Coliseo de la Ascensión") })
            Add(HelpTopic.Modes, "map-" + map, name, MatchRules.Objective(GameMode.Warfare, 15, 75, map) +
                "\n\n" + MatchRules.Details(GameMode.Warfare, map));
        Add(HelpTopic.Modes, "survival-save", "Guardar una Supervivencia local",
            "La partida local se autoguarda cada 20 segundos y al cambiar de aplicación. En el menú puedes usar «Guardar partida» o «Guardar y salir». «Continuar» recupera el último punto guardado en este dispositivo.");
        Add(HelpTopic.Modes, "online-menu", "Abrir menús durante una partida",
            "En Supervivencia con un solo humano, los menús pausan la simulación. Con otros humanos la partida continúa: tu mago se protege mientras consultas el inventario. Cierra la pantalla para recuperar los controles.");

        Add(HelpTopic.Equipment, "equip", "Equipar, comparar y mejorar",
            "Abre Inventario, elige una ranura y selecciona un objeto. Compara sus atributos antes de equiparlo. «Suma» reúne lo que aporta tu equipo y «Aplicado» muestra el valor final usado en la partida. Las bonificaciones requieren llevar la pieza equipada.");
        Add(HelpTopic.Equipment, "defense-shield", "Defensa y capacidad de escudo",
            EquipmentCatalogue.StatExample(EquipmentStat.Defense) + "\n\n" + EquipmentCatalogue.StatExample(EquipmentStat.ShieldCapacity) +
            "\nLa defensa reduce el daño recibido; no añade puntos al escudo. Absorción del escudo es el daño que este ha detenido.");
        Add(HelpTopic.Equipment, "sell", "Favoritos y venta automática",
            "Los objetos equipados y favoritos están protegidos frente a la venta. En Supervivencia, una pieza que hayas llevado equipada se conserva al sustituirla. La configuración predeterminada elige equipo según tu elemento y prioridades; puedes equipar manualmente. Los míticos nunca se venden automáticamente.");
        Add(HelpTopic.Equipment, "rings", "Los dos anillos",
            "Anillo de oro: en nivel 0 da una moneda extra cada 11 monedas base; cada mejora reduce el intervalo y en nivel 10 da una por moneda.\nAnillo mágico: aporta capacidad de escudo y una habilidad elemental, sin afijos. Renueva el escudo cada 16–8 s; desde nivel 6 duplica el elemento.\nAmbos admiten diez mejoras y no cuentan para conjuntos. Consulta los valores exactos en la ficha.");
        Add(HelpTopic.Equipment, "affixes", "Cómo se activan los efectos especiales",
            "Diamante y Mítico añaden un efecto especial, excepto el anillo mágico. Si repites un efecto, solo se aplica la copia más fuerte. Marca arcana y Eco arcano son efectos distintos y pueden coexistir. Última barrera conserva su recarga al cambiar equipo o reconectar.");
        Add(HelpTopic.Equipment, "mythic-eyes", "Ojos míticos",
            "Seis piezas míticas equipadas, incluidos los anillos, encienden llamitas en los ojos. Sus colores mezclan las familias del equipo. Es un efecto visual; quitar una pieza mítica lo apaga.");
        Add(HelpTopic.Equipment, "rewards", "Oro y premios de PvP",
            "50 de oro por victoria y 25 por derrota, más el bono del equipo al empezar. El ganador recibe una tirada de objeto. Requiere recompensas habilitadas, cuenta Google verificada, equipos humanos equilibrados y 15 segundos de actividad. Los bonos de oro afectan a recogidas de Supervivencia y al premio final de PvP, nunca a las ventas.");
        foreach (var stat in Enum.GetValues<EquipmentStat>())
            Add(HelpTopic.Equipment, "stat-" + stat, EquipmentCatalogue.StatName(stat), EquipmentCatalogue.StatDescription(stat));
        foreach (var affix in Enum.GetValues<ItemAffixKind>())
        {
            int diamond = affix == ItemAffixKind.GoldBonus ? 10 : 1;
            int mythic = affix == ItemAffixKind.GoldBonus ? 20 : ItemAffixes.IsExtended(affix) || affix == ItemAffixKind.LightningChain ? 2
                : affix == ItemAffixKind.BeamPierce ? 3 : 1;
            string body = "Diamante · " + ItemAffixes.Describe(new(affix, diamond));
            if (diamond != mythic) body += "\n\nMítico · " + ItemAffixes.Describe(new(affix, mythic));
            if (ItemAffixes.IsGold(affix)) body += "\nSolo está activo en el anillo de oro.";
            Add(HelpTopic.Equipment, "affix-" + affix, ItemAffixes.Name(affix), body);
        }

        Add(HelpTopic.Relics, "collect", "Recoger reliquias y conservar sus mejoras",
            "Camina sobre una reliquia para recogerla. Sus efectos se acumulan. En Santuario y Coliseo reaparecen a los 12 s y las mejoras se pierden al morir; en Coliseo cambian de posición y la vida ganada por bajas se conserva. En Supervivencia, oro, equipo y reliquias se conservan entre tus vidas hasta empezar otra partida.");
        foreach (var buff in Enum.GetValues<BuffKind>())
            Add(HelpTopic.Relics, "relic-" + buff, RelicBuffs.Name(buff), RelicBuffs.Description(buff));
        Add(HelpTopic.Wizards, "choose", "Qué cambia al elegir un mago",
            "Elige tu tipo antes de entrar en la partida. Todos pueden usar los ocho elementos. La afinidad aumenta solo su componente elemental, incluida la quemadura. La debilidad también consume más escudo. El tipo se conserva al reaparecer.");
        foreach (var wizard in Enum.GetValues<WizardType>())
            Add(HelpTopic.Wizards, "wizard-" + wizard, WizardTypes.Name(wizard), WizardTypes.Description(wizard));
        return articles.ToArray();
    }

    internal static IEnumerable<HelpArticle> Search(IEnumerable<HelpArticle> articles, string query)
    {
        string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var comparison = CultureInfo.GetCultureInfo("es").CompareInfo;
        return articles.Where(article => terms.All(term => comparison.IndexOf(
            Sections.First(section => section.Id == article.Topic).Title + " " + article.Title + " " + article.Body,
            term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0));
    }
}
