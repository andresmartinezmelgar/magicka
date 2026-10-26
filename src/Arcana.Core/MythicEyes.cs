namespace Arcana.Core;

/// <summary>Mezcla compacta de las seis piezas equipadas, incluidos los dos anillos.</summary>
public static class MythicEyes
{
    public const int SlotCount = 6, FamilyCount = 9;
    public static ulong Encode(params EquipmentFamily[] families)
    {
        if (families.Length != SlotCount || families.Any(f => !Enum.IsDefined(f))) throw new ArgumentException("Se requieren seis familias válidas.");
        ulong mix = 0;
        foreach (var family in families) mix += 1UL << ((int)family * 4);
        return mix;
    }
    public static bool AllMythic(EquipmentAppearance gear) => gear.Boots == ItemTier.Mythic && gear.Hat == ItemTier.Mythic &&
        gear.Chest == ItemTier.Mythic && gear.Weapon == ItemTier.Mythic && gear.GoldRing == ItemTier.Mythic && gear.MagicRing == ItemTier.Mythic;
    public static bool ValidMix(ulong mix)
    {
        if (mix >> (FamilyCount * 4) != 0) return false;
        int total = 0;
        for (int i = 0; i < FamilyCount; i++)
        {
            int count = (int)((mix >> (i * 4)) & 15);
            if (count > SlotCount) return false;
            total += count;
        }
        return total == SlotCount;
    }
    public static bool Active(EquipmentAppearance? gear) => gear != null && AllMythic(gear) && ValidMix(gear.MythicFamilies);
    public static int Pieces(ulong mix, EquipmentFamily family) => Enum.IsDefined(family) ? (int)((mix >> ((int)family * 4)) & 15) : 0;
    public static float Weight(ulong mix, EquipmentFamily family) => Pieces(mix, family) / (float)SlotCount;
}
