using System.Globalization;

namespace Arcana.Core;

public sealed record BestiaryStat(string Name, string Value);
public sealed record BestiarySpellDamage(float Minimum, float Maximum, bool PerSecond, int Projectiles);

public static partial class BestiaryCatalogue
{
    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public static BestiarySpellDamage SpellDamage(MinionKind kind, SpellRecipe spell, int difficulty = 1)
    {
        if (!DefenseRules.SpellcastingEnemy(kind) || difficulty < 1) throw new ArgumentOutOfRangeException(nameof(kind));
        float damage = 0;
        foreach (var element in SpellBalance.Offensive)
            damage += SpellBalance.Component(spell, element) * WizardTypes.Outgoing(DefenseRules.EnemyWizardType(kind), element);
        damage *= DefenseRules.EnemyStrength(difficulty) * DefenseRules.EnemySpellPower(kind);
        if (spell.Shape == SpellShape.Rock) return new(damage * DefenseRules.EnemyRockPower, damage * DefenseRules.EnemyRockPower, false, 1);
        if (spell.Shape == SpellShape.Shards) return new(damage * SpellBalance.ShardPower, damage * SpellBalance.ShardPower, false, 3 + spell.Count(Element.Cold));
        if (spell.Shape == SpellShape.Beam) return new(damage * SpellBalance.BeamMinimumDamage,
            damage * SpellBalance.BeamDamageMultiplier(DefenseRules.EnemyChannelSeconds(spell)), true, 1);
        return new(damage, damage, true, 1);
    }

    /// <summary>Cifras frente a un mago equilibrado, seco y sin equipo, antes de potenciadores temporales.</summary>
    public static BestiaryStat[] Stats(MinionKind kind, int difficulty = 1, int defenders = 1)
    {
        if (!Enum.IsDefined(kind) || difficulty < 1 || defenders is < 1 or > DefenseRules.MaxDefenders)
            throw new ArgumentOutOfRangeException(nameof(kind));
        float strength = DefenseRules.EnemyStrength(difficulty);
        var rows = new List<BestiaryStat>();
        void Add(string name, string value) => rows.Add(new(name, value));
        Add("Vida máxima", Number(DefenseRules.MinionHealth(kind, difficulty, defenders)));
        Add("Velocidad", Number(Wizard.BaseMovementSpeed * DefenseRules.MinionSpeed(kind, difficulty)) + " m/s");
        Add("Distancia para atacar", Number(DefenseRules.MinionRange(kind)) + " m");
        Add("Pausa entre ataques", kind == MinionKind.Bomber ? "Una explosión" : Number(DefenseRules.AttackDelay(difficulty)) + " s");
        if (!DefenseRules.SpellcastingEnemy(kind))
            Add(kind == MinionKind.Archer ? "Daño por flecha" : "Daño por golpe",
                Number((kind == MinionKind.Archer ? SpellBalance.Earth * DefenseRules.ArrowPower : DefenseRules.MinionMeleeAttack(kind)) * strength));
        foreach (var spell in DefenseRules.EnemySpells(kind))
        {
            var damage = SpellDamage(kind, spell, difficulty);
            string amount = Number(damage.Minimum) + (damage.Maximum != damage.Minimum ? "–" + Number(damage.Maximum) : "");
            string unit = damage.PerSecond ? " daño/s" : damage.Projectiles > 1 ? " por fragmento ×" + damage.Projectiles : " por impacto";
            string shape = spell.Shape switch { SpellShape.Rock => "Roca", SpellShape.Shards => "Esquirlas", SpellShape.Beam => "Haz", SpellShape.Lightning => "Relámpago", _ => "Cono" };
            Add(shape + " · " + SpellCatalogue.Name(spell.Elements), amount + unit);
        }
        if (DefenseRules.AreaAttackRadius(kind) > 0)
        {
            Add("Radio del ataque de área", Number(DefenseRules.AreaAttackRadius(kind)) + " m");
            Add("Preparación del ataque", Number(DefenseRules.AttackWindup(kind)) + " s");
        }
        if (DefenseRules.SpellcastingEnemy(kind))
        {
            var spells = DefenseRules.EnemySpells(kind);
            if (spells.Any(s => s.Shape == SpellShape.Beam || s.Shape == SpellShape.Lightning)) Add("Duración de haces y relámpagos", Number(DefenseRules.EnemyChannelDuration) + " s");
            if (spells.Any(s => s.Shape == SpellShape.Spray)) Add("Duración de los conos", Number(DefenseRules.EnemySprayDuration) + " s");
            if (spells.Any(s => s.Has(Element.Fire))) Add("Quemadura adicional", Number(SpellBalance.BurnPerSecond * strength * DefenseRules.EnemySpellPower(kind) *
                WizardTypes.Outgoing(DefenseRules.EnemyWizardType(kind), Element.Fire)) + " daño/s durante " + Number(SpellBalance.BurnDuration) + " s");
        }
        if (kind == MinionKind.Shaman)
        {
            Add("Curación a un aliado", Number(DefenseRules.ShamanHealAmount * strength) + " cada " + Number(DefenseRules.ShamanHealInterval) + " s");
            Add("Alcance de curación", Number(DefenseRules.ShamanHealRadius) + " m");
        }
        if (kind == MinionKind.Sentinel)
        {
            Add("Escudo de tierra", Number(DefenseRules.SentinelShield * strength));
            Add("Duración del escudo", Number(DefenseRules.SentinelShieldDuration) + " s");
            Add("Recarga del escudo", Number(DefenseRules.SentinelShieldCooldown) + " s");
        }
        if (kind is MinionKind.Captain or MinionKind.Boss) Add("Refuerzos por llamada",
            "Hasta " + (kind == MinionKind.Boss ? DefenseRules.CommanderSurgeBonus : DefenseRules.CaptainReinforcements) +
            " en " + Number(DefenseRules.CommanderSurgeSeconds) + " s; según límite de enemigos");
        if (WizardTypes.Weakness(DefenseRules.EnemyWizardType(kind)) is { } weakness) Add("Debilidad elemental",
            SpellCatalogue.Name([weakness]) + " · recibe +" + Number((WizardTypes.Bonus - 1) * 100) + " % de daño");
        return rows.ToArray();
    }
}
