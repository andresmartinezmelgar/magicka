using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Glifos compartidos por inventarios y equipo en partida; una forma por efecto.</summary>
internal static class EquipmentIcons
{
    private static readonly Dictionary<string, Texture2D> Textures = new(StringComparer.Ordinal);
    internal static string AffixFile(ItemAffixKind kind) => kind switch
    {
        ItemAffixKind.BeamPierce => "beam-pierce", ItemAffixKind.LightningChain => "lightning-chain",
        ItemAffixKind.DoubleBurn => "double-burn", ItemAffixKind.WaterShieldPool => "water-shield-pool",
        ItemAffixKind.CoinDividend => "coin-dividend", ItemAffixKind.GoldBonus => "gold-bonus",
        ItemAffixKind.Icebreaker => "icebreaker", ItemAffixKind.SteamImpact => "steam-impact",
        ItemAffixKind.ArcaneExecution => "arcane-execution", ItemAffixKind.ShieldBattery => "shield-battery",
        ItemAffixKind.MendingWard => "mending-ward", ItemAffixKind.ShieldReprisal => "shield-reprisal",
        ItemAffixKind.ShieldLeech => "shield-leech", ItemAffixKind.ShieldReflection => "shield-reflection",
        ItemAffixKind.RockFragmentation => "rock-fragmentation", ItemAffixKind.ArcaneMark => "arcane-mark",
        ItemAffixKind.ArcaneEcho => "arcane-echo", ItemAffixKind.AutomaticRock => "automatic-rock",
        ItemAffixKind.LightningStun => "lightning-stun", ItemAffixKind.WaterSlow => "water-slow",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    private static Texture2D Load(string file)
    {
        if (!Textures.TryGetValue(file, out var texture))
            Textures[file] = texture = GD.Load<Texture2D>("res://Assets/Inventory/Effects/" + file + ".svg")
                ?? throw new InvalidDataException("No se pudo cargar el icono de equipo: " + file);
        return texture;
    }
    internal static Texture2D Affix(ItemAffixKind kind) => Load(AffixFile(kind));
    internal static Texture2D Slot(EquipmentSlot slot) => Load(slot switch
    {
        EquipmentSlot.Boots => "slot-boots", EquipmentSlot.Hat => "slot-hat", EquipmentSlot.Chest => "slot-chest",
        EquipmentSlot.Weapon => "slot-weapon", EquipmentSlot.GoldRing => "slot-gold-ring",
        EquipmentSlot.MagicRing => "slot-magic-ring", _ => throw new ArgumentOutOfRangeException(nameof(slot))
    });
    internal static TextureRect View(Texture2D texture, float side, Color? tint = null) => new()
    {
        Texture = texture, CustomMinimumSize = new(side, side),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        Modulate = tint ?? Ui.Gold, MouseFilter = Control.MouseFilterEnum.Ignore
    };
    internal static Color FamilyColor(EquipmentFamily family) => RingEffects.ElementFor(family) is { } element
        ? Ui.ElementColor(element) : Ui.Text;
    internal static void AddBadges(VBoxContainer target, EquipmentSlot slot, EquipmentFamily family, ItemAffix? affix, int level)
    {
        void Add(Texture2D icon, string name, string description, Element? element = null)
        {
            var row = new HBoxContainer { Name = "EquipmentEffectBadge", TooltipText = description,
                MouseFilter = Control.MouseFilterEnum.Pass };
            row.AddThemeConstantOverride("separation", 8); target.AddChild(row);
            row.AddChild(View(icon, 26));
            if (element != null) row.AddChild(new ElementIcon { Value = element, CustomMinimumSize = new(22, 22) });
            var text = Ui.Label(name, 15, Ui.Gold); text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; text.MouseFilter = Control.MouseFilterEnum.Ignore; row.AddChild(text);
        }
        if (ItemAffixes.Active(affix, slot) is { } active)
            Add(Affix(active.Kind), ItemAffixes.Name(active.Kind), ItemAffixes.Describe(active, level, slot));
        if (slot == EquipmentSlot.GoldRing && affix?.Kind != ItemAffixKind.CoinDividend)
            Add(Affix(ItemAffixKind.CoinDividend), "Monedas extra", RingEffects.Describe(slot, family, level));
        if (slot == EquipmentSlot.MagicRing)
            Add(Slot(slot), RingEffects.ActionName(family), RingEffects.Describe(slot, family, level), RingEffects.ElementFor(family));
    }
}
