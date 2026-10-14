namespace Arcana.Core;

/// <summary>Explicaciones mantenidas junto a los modificadores reales del motor.
/// Los porcentajes de los objetos son multiplicadores, no puntos directos.</summary>
public static partial class EquipmentCatalogue
{
    public static string StatDescription(EquipmentStat stat) => stat switch
    {
        EquipmentStat.MovementSpeed => "Aumenta la velocidad de movimiento del mago. +10 % significa recorrer un 10 % más de distancia por segundo, antes de estados temporales.",
        EquipmentStat.SpellRange => "Aumenta el alcance de los hechizos y ataques afectados por la distancia. No aumenta su daño.",
        EquipmentStat.Defense => "Reduce el daño que recibes, incluso cuando lo absorbe tu escudo. No aumenta los puntos máximos del escudo: Defensa +3,1 % convierte 100 de daño entrante en 96,9 (antes de otras resistencias).",
        EquipmentStat.Power => "Multiplica el daño ofensivo general que recibe este modificador. No aumenta los puntos de escudo.",
        EquipmentStat.HealingPower => "Multiplica la curación que producen tus hechizos y efectos de regeneración compatibles.",
        EquipmentStat.FirePower => "Aumenta el daño de los hechizos de fuego; no aumenta por sí mismo la duración de las quemaduras.",
        EquipmentStat.ShieldCapacity => "Aumenta los puntos máximos de los escudos y las barreras que creas. Un escudo de 420 con +3,1 % de absorción tendrá aproximadamente 433 puntos.",
        EquipmentStat.ShieldDuration => "Alarga la vida de escudos y barreras temporales; no cambia los puntos que absorben.",
        EquipmentStat.EnergyRegeneration => "Multiplica la energía que recuperas automáticamente cada segundo.",
        EquipmentStat.LifeEfficiency => "Reduce el coste energético de la magia de reanimación. No aumenta la curación ni la vida máxima.",
        EquipmentStat.BurnDuration => "Alarga el tiempo durante el que los enemigos permanecen ardiendo después de tus quemaduras.",
        EquipmentStat.WaterPower => "Aumenta el daño de los hechizos de agua.",
        EquipmentStat.ColdPower => "Aumenta el daño de los hechizos de frío; el efecto de ralentización tiene otro atributo.",
        EquipmentStat.LightningPower => "Aumenta el daño de los hechizos de rayo.",
        EquipmentStat.ArcanePower => "Aumenta el daño de los hechizos arcanos.",
        EquipmentStat.EarthPower => "Aumenta el daño de los hechizos de tierra.",
        EquipmentStat.WetDuration => "Alarga el estado Mojado que provocan tus hechizos de agua, sin aumentar directamente su daño.",
        EquipmentStat.ChillPower => "Potencia el enfriamiento que provocas, facilitando ralentizar y congelar a un enemigo.",
        EquipmentStat.FreezeDuration => "Alarga la duración de la congelación que provocas.",
        EquipmentStat.WetLightningPower => "Aumenta el daño adicional de los rayos contra objetivos que ya están Mojados. No afecta a objetivos secos.",
        EquipmentStat.BeamChargeSpeed => "Reduce el tiempo necesario para acumular carga de los hechizos de haz compatibles, al acelerar su progreso.",
        EquipmentStat.RockChargeSpeed => "Acelera la carga de los proyectiles de roca; permite alcanzar la misma carga en menos tiempo.",
        _ => throw new ArgumentOutOfRangeException(nameof(stat))
    };

    public static string StatExample(EquipmentStat stat) => stat switch
    {
        EquipmentStat.Defense => "100 de daño recibido × (1 − 0,031) = 96,9. El escudo sigue mostrando sus puntos originales, pero se desgasta más despacio.",
        EquipmentStat.ShieldCapacity => "420 puntos de escudo × (1 + 0,031) = 433,02 puntos iniciales.",
        EquipmentStat.ShieldDuration => "12 s de duración × (1 + 0,031) = 12,372 s.",
        _ => "Valor aplicado = bonificaciones de piezas y conjuntos, con sus límites. Un +3,1 % equivale a un multiplicador ×1,031 (o ×0,969 para una reducción de coste o daño)."
    };
}
