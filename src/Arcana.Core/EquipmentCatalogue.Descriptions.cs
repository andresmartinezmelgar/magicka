namespace Arcana.Core;

/// <summary>Explicaciones mantenidas junto a los modificadores reales del motor.
/// Los porcentajes de los objetos son multiplicadores, no puntos directos.</summary>
public static partial class EquipmentCatalogue
{
    public static string StatDescription(EquipmentStat stat) => stat switch
    {
        EquipmentStat.MovementSpeed => "Aumenta la velocidad al caminar, antes de los estados temporales.",
        EquipmentStat.SpellRange => "Aumenta el alcance de tus hechizos, sin cambiar su daño.",
        EquipmentStat.Defense => "Reduce el daño recibido, también al escudo. No aumenta su capacidad.",
        EquipmentStat.Power => "Aumenta el daño ofensivo general, sin cambiar la capacidad de escudo.",
        EquipmentStat.HealingPower => "Aumenta la curación de tus hechizos y la regeneración compatible.",
        EquipmentStat.FirePower => "Aumenta el daño de fuego, sin alargar las quemaduras.",
        EquipmentStat.ShieldCapacity => "Aumenta los puntos máximos de los escudos y barreras que creas.",
        EquipmentStat.ShieldDuration => "Alarga la duración de escudos y barreras, sin aumentar su capacidad.",
        EquipmentStat.EnergyRegeneration => "Aumenta la energía que recuperas cada segundo.",
        EquipmentStat.LifeEfficiency => "Reduce la energía necesaria para reanimar a un aliado.",
        EquipmentStat.BurnDuration => "Alarga las quemaduras que provocan tus hechizos.",
        EquipmentStat.WaterPower => "Aumenta la potencia de tus hechizos de agua.",
        EquipmentStat.ColdPower => "Aumenta el daño de frío, sin cambiar la ralentización.",
        EquipmentStat.LightningPower => "Aumenta el daño de los hechizos de rayo.",
        EquipmentStat.ArcanePower => "Aumenta el daño de los hechizos arcanos.",
        EquipmentStat.EarthPower => "Aumenta el daño de los hechizos de tierra.",
        EquipmentStat.WetDuration => "Modifica cuánto dura el estado Mojado que aplicas con agua. Mientras está mojado, el objetivo recibe más daño de rayo y se congela más deprisa con frío. Un porcentaje negativo acorta esa duración.",
        EquipmentStat.ChillPower => "Aumenta el enfriamiento para ralentizar y congelar enemigos.",
        EquipmentStat.FreezeDuration => "Alarga la congelación que provocan tus hechizos.",
        EquipmentStat.WetLightningPower => "Aumenta el daño de tus rayos contra objetivos que ya están mojados por agua. Se aplica mientras dure ese estado.",
        EquipmentStat.BeamChargeSpeed => "Acelera la carga de los haces arcanos para alcanzar la misma potencia en menos tiempo.",
        EquipmentStat.RockChargeSpeed => "Permite cargar los proyectiles de roca en menos tiempo.",
        _ => throw new ArgumentOutOfRangeException(nameof(stat))
    };

    public static string StatExample(EquipmentStat stat) => stat switch
    {
        EquipmentStat.Defense => "Defensa +3,1 %: 100 de daño × 0,969 = 96,9, antes de otras resistencias.",
        EquipmentStat.ShieldCapacity => "420 puntos de escudo × (1 + 0,031) = 433,02 puntos iniciales.",
        EquipmentStat.ShieldDuration => "12 s de duración × (1 + 0,031) = 12,372 s.",
        _ => "Se suman piezas y conjuntos, hasta su límite. +3,1 % equivale a ×1,031; para reducir coste o daño, ×0,969."
    };
}
