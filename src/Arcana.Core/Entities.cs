namespace Arcana.Core;

public sealed class Wizard
{
    internal CombatantRole Role { get; init; }
    public required int Id { get; init; }
    public required string Name { get; init; }
    public int Team { get; internal set; }
    public bool Bot { get; init; }
    public WizardType WizardType { get; init; }
    public EquipmentModifiers Equipment { get; internal set; } = EquipmentModifiers.None;
    public PublicEquipment PublicEquipment { get; internal set; } = PublicEquipment.Empty;
    public V2 Position { get; set; }
    public V2 Facing { get; set; } = new(1, 0);
    public const float BaseMaxHealth = 1000;
    public const float BaseMovementSpeed = 6.2f;
    public float Health { get; set; } = BaseMaxHealth;
    public float MaxHealth { get; set; } = BaseMaxHealth;
    public int[] BuffStacks { get; } = new int[RelicBuffs.Count];
    public bool Alive => Health > 0;
    public float Shield { get; set; }
    public float ShieldMax { get; set; }
    public float ShieldTime { get; set; }
    public int ShieldCharges { get; set; } = 1;
    public float RegenerationRate { get; set; }
    public Element[] Ward { get; set; } = [];
    public float WardTime { get; set; }
    public float Wet { get; set; }
    public float Burning { get; set; }
    public int BurnOwner { get; set; }
    public float BurnMultiplier { get; set; } = 1;
    public float HitEffectCooldown { get; set; }
    public float Chill { get; set; }
    public float Frozen { get; set; }
    internal float FreezeRecovery { get; set; }
    internal Element? VulnerableElement { get; set; }
    public float Haste { get; set; }
    public float Energy { get; set; } = 100;
    public float Respawn { get; set; }
    public float Invulnerable { get; set; } = 1;
    public float TeleportCooldown { get; set; }
    public bool TeleportNeedsExit { get; set; }
    public float ActionCooldown { get; set; }
    public float RingCooldown { get; set; }
    internal float ReprisalCooldown { get; set; }
    public float[] MagickCooldowns { get; } = new float[4];
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public ElementBuffer Selection { get; } = new();
    public ClientMessage Input { get; set; } = new() { Type = "input", AimX = 1 };
    public double LastInput { get; set; }
    public long LastSequence { get; set; }
    public long SelectionSequence { get; set; }
    public SpellRecipe? Channel { get; set; }
    public int BeamTicks { get; set; }
    public float BeamCharge => Math.Clamp(BeamTicks * GameWorld.StepSeconds / SpellBalance.BeamChargeSeconds, 0, 1);
    public SpellRecipe? Charging { get; set; }
    public float ChargeTime { get; set; }
    public float BotDecision { get; set; }
    internal int BotWallTarget { get; set; }
    internal int SlideSurface { get; set; }
    internal int SlideSide { get; set; }
    internal V2 SlideIntent { get; set; }
    internal V2 SlideTangent { get; set; }
    public V2 Knockback { get; set; }
}

internal sealed class Projectile
{
    public int Id;
    public int Owner;
    public V2 Position;
    public V2 Velocity;
    public required SpellRecipe Recipe;
    public float Radius;
    public float Power;
    public float Lifetime = 2.5f;
    public int RockFragmentation;
    public bool IsRockFragment;
}
internal sealed class Wall
{
    public int Id;
    public int Owner;
    public V2 A;
    public V2 B;
    public float Health = 650;
    public float Lifetime = 7;
    public required SpellRecipe Recipe;
}
internal sealed class Field
{
    public int Id;
    public int Owner;
    public V2 Position;
    public float Radius;
    public float Lifetime;
    public float Delay;
    public float TriggerRadius = 0.85f;
    public float Power = 1;
    public bool IsRockFragmentArea;
    public required SpellRecipe Recipe;
    public string Kind = "area";
}
internal sealed class ControlPoint
{
    public int Id;
    public V2 Position;
    public int Owner = -1;
    public float Progress;
    public bool Contested;
}

public sealed class WorldOptions
{
    public DefenseFlow DefenseFlow { get; init; } = DefenseFlow.Continuous;
    public GameMode Mode { get; init; } = GameMode.Warfare;
    public ArenaMapId Map { get; init; } = ArenaMapId.Classic;
    public SurvivalMapSpec? SurvivalMap { get; init; }
    public int MaxPlayers { get; init; } = 8;
    public bool FriendlyFire { get; init; } = true;
    public int ScoreLimit { get; init; } = 15;
    public int Tickets { get; init; } = 75;
    public int Bots { get; init; }
    public int Seed { get; init; } = 73;
    // La colocación cambia entre salas. Una semilla explícita permite reproducir pruebas y capturas.
    public int? PickupSeed { get; init; }
    public int? SpawnSeed { get; init; }
    public int? EnvironmentSeed { get; init; }
}
