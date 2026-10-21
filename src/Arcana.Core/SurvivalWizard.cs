namespace Arcana.Core;

// Datos de combate independientes del motor, sin órdenes de teclado o red.
internal sealed class SurvivalWizard
{
    public int Id = 0;
    public string Name = "";
    public bool Bot = false;
    public WizardType WizardType = WizardType.Balanced;
    public EquipmentModifiers Equipment = EquipmentModifiers.None;
    public V2 Position = V2.Zero;
    public V2 Facing = new(1, 0);
    public float Health = 0;
    public float MaxHealth = 0;
    public float Shield = 0;
    public float ShieldMax = 0;
    public float ShieldTime = 0;
    public float RegenerationRate = 0;
    public float WardTime = 0;
    public float Wet = 0;
    public float Burning = 0;
    public float BurnMultiplier = 0;
    public float HitEffectCooldown = 0;
    public float Chill = 0;
    public float Frozen = 0;
    public float FreezeRecovery = 0;
    public float Haste = 0;
    public float Energy = 0;
    public float Respawn = 0;
    public float Invulnerable = 0;
    public float TeleportCooldown = 0;
    public float ActionCooldown = 0;
    public float ChargeTime = 0;
    public float BotDecision = 0;
    public int ShieldCharges = 0;
    public int BurnOwner = 0;
    public int Kills = 0;
    public int Deaths = 0;
    public int BeamTicks = 0;
    public int BotWallTarget = 0;
    public int SlideSurface = 0;
    public int SlideSide = 0;
    public bool TeleportNeedsExit = false;
    public V2 Knockback = V2.Zero;
    public V2 SlideIntent = V2.Zero;
    public V2 SlideTangent = V2.Zero;
    public int[] BuffStacks = [];
    public float[] MagickCooldowns = [];
    public Element[] Ward = [];
    public Element[] Selection = [];
    public Element[]? Channel = null;
    public Element[]? Charging = null;

    public static SurvivalWizard Capture(Wizard p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Bot = p.Bot,
        WizardType = p.WizardType,
        Equipment = p.Equipment,
        Position = p.Position,
        Facing = p.Facing,
        Health = p.Health,
        MaxHealth = p.MaxHealth,
        Shield = p.Shield,
        ShieldMax = p.ShieldMax,
        ShieldTime = p.ShieldTime,
        RegenerationRate = p.RegenerationRate,
        WardTime = p.WardTime,
        Wet = p.Wet,
        Burning = p.Burning,
        BurnMultiplier = p.BurnMultiplier,
        HitEffectCooldown = p.HitEffectCooldown,
        Chill = p.Chill,
        Frozen = p.Frozen,
        FreezeRecovery = p.FreezeRecovery,
        Haste = p.Haste,
        Energy = p.Energy,
        Respawn = p.Respawn,
        Invulnerable = p.Invulnerable,
        TeleportCooldown = p.TeleportCooldown,
        ActionCooldown = p.ActionCooldown,
        ChargeTime = p.ChargeTime,
        BotDecision = p.BotDecision,
        ShieldCharges = p.ShieldCharges,
        BurnOwner = p.BurnOwner,
        Kills = p.Kills,
        Deaths = p.Deaths,
        BeamTicks = p.BeamTicks,
        BotWallTarget = p.BotWallTarget,
        SlideSurface = p.SlideSurface,
        SlideSide = p.SlideSide,
        TeleportNeedsExit = p.TeleportNeedsExit,
        Knockback = p.Knockback,
        SlideIntent = p.SlideIntent,
        SlideTangent = p.SlideTangent,
        BuffStacks = p.BuffStacks.ToArray(),
        MagickCooldowns = p.MagickCooldowns.ToArray(),
        Ward = p.Ward.ToArray(),
        Selection = p.Selection.Elements.ToArray(),
        Channel = p.Channel?.Elements.ToArray(),
        Charging = p.Charging?.Elements.ToArray(),
    };

    public Wizard Restore(bool minion, double time)
    {
        var p = new Wizard
        {
            Role = minion ? CombatantRole.Minion : CombatantRole.Player, Team = minion ? 1 : 0,
            Id = Id,
            Name = Name,
            Bot = Bot,
            WizardType = WizardType,
            Equipment = Equipment,
            Position = Position,
            Facing = Facing,
            Health = Health,
            MaxHealth = MaxHealth,
            Shield = Shield,
            ShieldMax = ShieldMax,
            ShieldTime = ShieldTime,
            RegenerationRate = RegenerationRate,
            WardTime = WardTime,
            Wet = Wet,
            Burning = Burning,
            BurnMultiplier = BurnMultiplier,
            HitEffectCooldown = HitEffectCooldown,
            Chill = Chill,
            Frozen = Frozen,
            FreezeRecovery = FreezeRecovery,
            Haste = Haste,
            Energy = Energy,
            Respawn = Respawn,
            Invulnerable = Invulnerable,
            TeleportCooldown = TeleportCooldown,
            ActionCooldown = ActionCooldown,
            ChargeTime = Bot ? ChargeTime : 0,
            BotDecision = BotDecision,
            ShieldCharges = ShieldCharges,
            BurnOwner = BurnOwner,
            Kills = Kills,
            Deaths = Deaths,
            BeamTicks = Bot ? BeamTicks : 0,
            BotWallTarget = BotWallTarget,
            SlideSurface = SlideSurface,
            SlideSide = SlideSide,
            TeleportNeedsExit = TeleportNeedsExit,
            Knockback = Knockback,
            SlideIntent = SlideIntent,
            SlideTangent = SlideTangent,
            Ward = Ward.ToArray(),
            Channel = Bot && Channel != null ? new SpellRecipe(Channel) : null,
            Charging = Bot && Charging != null ? new SpellRecipe(Charging) : null,
            LastInput = time,
        };
        BuffStacks.CopyTo(p.BuffStacks, 0); MagickCooldowns.CopyTo(p.MagickCooldowns, 0);
        foreach (var element in Selection) p.Selection.Add(element);
        p.Input = new() { Type = "input", AimX = Position.X + Facing.X, AimY = Position.Y + Facing.Y };
        return p;
    }

    public bool Valid() => Id > 0 && Id < int.MaxValue - 1 && Name is { Length: > 0 and <= 24 } &&
        Enum.IsDefined(WizardType) && Equipment != null && Position.IsFinite && Facing.IsFinite &&
        Knockback.IsFinite && SlideIntent.IsFinite && SlideTangent.IsFinite &&
        Math.Abs(Position.X) <= 1000 && Math.Abs(Position.Y) <= 1000 &&
        BuffStacks is { Length: RelicBuffs.Count } && BuffStacks.All(n => n >= 0) &&
        MagickCooldowns is { Length: 4 } && MagickCooldowns.All(float.IsFinite) &&
        ValidElements(Ward) && ValidElements(Selection) && (Channel == null || ValidElements(Channel)) &&
        (Charging == null || ValidElements(Charging)) && Kills >= 0 && Deaths is >= 0 and <= DefenseRules.PlayerLives &&
        MaxHealth > 0 && Health <= MaxHealth && ShieldMax >= 0 && Shield <= ShieldMax &&
        float.IsFinite(FreezeRecovery) && FreezeRecovery is >= 0 and <= DefenseRules.FreezeRecoverySeconds &&
        new float[] { Health, MaxHealth, Shield, ShieldMax, ShieldTime, RegenerationRate, WardTime, Wet, Burning, BurnMultiplier, HitEffectCooldown, Chill, Frozen, Haste, Energy, Respawn, Invulnerable, TeleportCooldown, ActionCooldown, ChargeTime, BotDecision }.All(f => float.IsFinite(f) && Math.Abs(f) <= 1e12f);

    internal static bool ValidElements(Element[]? elements) => elements is { Length: <= 3 } && elements.All(Enum.IsDefined);
}
