namespace Arcana.Core;

public sealed class Wizard
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public int Team { get; init; }
    public bool Bot { get; init; }
    public WizardType WizardType { get; init; }
    public V2 Position { get; set; }
    public V2 Facing { get; set; } = new(1, 0);
    public float Health { get; set; } = 1000;
    public const float MaxHealth = 1000;
    public bool Alive => Health > 0;
    public float Shield { get; set; }
    public float ShieldMax { get; set; }
    public float ShieldTime { get; set; }
    public Element[] Ward { get; set; } = [];
    public float WardTime { get; set; }
    public float Wet { get; set; }
    public float Burning { get; set; }
    public int BurnOwner { get; set; }
    public float BurnMultiplier { get; set; } = 1;
    public float HitEffectCooldown { get; set; }
    public float Chill { get; set; }
    public float Frozen { get; set; }
    public float Haste { get; set; }
    public float Energy { get; set; } = 100;
    public float Respawn { get; set; }
    public float Invulnerable { get; set; } = 1;
    public float ActionCooldown { get; set; }
    public float[] MagickCooldowns { get; } = new float[4];
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public ElementBuffer Selection { get; } = new();
    public ClientMessage Input { get; set; } = new() { Type = "input", AimX = 1 };
    public double LastInput { get; set; }
    public long LastSequence { get; set; }
    public SpellRecipe? Channel { get; set; }
    public SpellRecipe? Charging { get; set; }
    public float ChargeTime { get; set; }
    public float BotDecision { get; set; }
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
    public required SpellRecipe Recipe;
    public string Kind = "area";
}
internal sealed class ControlPoint
{
    public int Id;
    public V2 Position;
    public int Owner = -1;
    public float Progress;
}

public sealed class WorldOptions
{
    public GameMode Mode { get; init; } = GameMode.Warfare;
    public int MaxPlayers { get; init; } = 8;
    public bool FriendlyFire { get; init; } = true;
    public int ScoreLimit { get; init; } = 15;
    public int Tickets { get; init; } = 75;
    public int Bots { get; init; }
    public int Seed { get; init; } = 73;
}
