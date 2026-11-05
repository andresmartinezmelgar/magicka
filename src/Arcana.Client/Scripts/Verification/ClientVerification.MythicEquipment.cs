using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool CheckMythicEquipmentVisuals()
    {
        static void Check(bool ok, string message)
        { if (!ok) throw new InvalidDataException("MYTHIC_EQUIPMENT_FAILED: " + message); }
        static EquipmentAppearance Single(EquipmentSlot slot, ItemTier tier) => slot switch
        {
            EquipmentSlot.Boots => new(Boots: tier), EquipmentSlot.Hat => new(Hat: tier),
            EquipmentSlot.Chest => new(Chest: tier), EquipmentSlot.Weapon => new(Weapon: tier),
            EquipmentSlot.GoldRing => new() { GoldRing = tier }, _ => new() { MagicRing = tier }
        };
        var full = new EquipmentAppearance(ItemTier.Mythic, ItemTier.Mythic, ItemTier.Mythic, ItemTier.Mythic)
            { GoldRing = ItemTier.Mythic, MagicRing = ItemTier.Mythic };
        var template = _runtime.Snapshot!.Players.First() with { Position = V2.Zero, Facing = new(0, 1),
            Health = 1000, MaxHealth = 1000, Alive = true, Frozen = 0, Casting = false, Equipment = null };
        foreach (var type in Enum.GetValues<WizardType>())
        {
            var player = template with { WizardType = type };
            var wizard = new WizardVisual(); AddChild(wizard); wizard.Build(player, false);
            var avatar = new EquipmentAvatar { Size = new(360, 480) }; AddChild(avatar); avatar.Build(type, _ => { });
            var portrait = new EquipmentAvatar { Size = new(180, 180) }; AddChild(portrait); portrait.BuildPortrait(type, null);
            try
            {
                foreach (var slot in Enum.GetValues<EquipmentSlot>())
                {
                    foreach (var tier in Enum.GetValues<ItemTier>())
                    {
                        var gear = Single(slot, tier);
                        wizard.Update(player with { Equipment = gear }, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                        var item = EquipmentCatalogue.Generate(new(130 + (int)slot), tier: tier, slot: slot, family: EquipmentFamily.Life);
                        avatar.UpdateSlot(slot, item, false); portrait.UpdatePortrait(type, gear);
                        int expected = tier == ItemTier.Mythic ? 1 << (int)slot : 0;
                        Check(wizard.MythicEquipmentMask == expected && avatar.MythicEquipmentMask == expected && portrait.MythicEquipmentMask == expected,
                            "La pieza o calidad no coincide entre combate, inventario y retrato.");
                        Check(wizard.MythicEquipmentVisible == (expected != 0), "Una calidad inferior cambia el sprite.");
                    }
                    avatar.UpdateSlot(slot, null, false);
                    Check(avatar.MythicEquipmentMask == 0, "Desequipar conserva la pieza.");
                }
                foreach (var facing in new V2[] { new(0, 1), new(-1, 0), new(0, -1), new(1, 0) })
                foreach (bool casting in new[] { false, true })
                {
                    wizard.Update(player with { Equipment = full, Facing = facing, Casting = casting }, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                    var sprite = wizard.Sprite;
                    var node = (MeshInstance3D)wizard.FindChild("MythicEquipment", true, false);
                    var atlas = WizardSprites.Load(type); var feet = atlas.Spec.Frames[sprite.Frame];
                    Check(wizard.MythicEquipmentMask == 63 && wizard.MythicEquipmentVisible &&
                        node.Mesh == MythicEquipmentVisuals.Mesh(type, sprite.Frame) && node.GetParent() == sprite.GetParent(),
                        "La indumentaria no sigue la orientación y el lanzamiento.");
                    Check(node.Position.IsEqualApprox(new(-feet.AnchorX * atlas.Spec.PixelSize, feet.AnchorY * atlas.Spec.PixelSize, 0)) &&
                        node.Scale.IsEqualApprox(new(atlas.Spec.PixelSize, -atlas.Spec.PixelSize, 1)),
                        "La indumentaria no comparte el anclaje de pies del sprite.");
                }
                var instance = wizard.FindChild("MythicEquipment", true, false);
                wizard.Update(player with { Equipment = full, Alive = false }, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                Check(!wizard.MythicEquipmentVisible && wizard.MythicEquipmentMask == 0, "El fantasma conserva equipo.");
                wizard.Update(player with { Equipment = full }, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                Check(wizard.MythicEquipmentVisible && wizard.FindChild("MythicEquipment", true, false) == instance,
                    "Reaparecer pierde o duplica la indumentaria.");
                wizard.Update(player with { Equipment = full, Frozen = 1, Casting = true }, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                Check(wizard.Sprite.Frame % 4 == 0 && wizard.MythicEquipmentVisible, "Congelarse pierde el equipo o conserva lanzamiento.");
                wizard.Update(player with { Equipment = null }, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                portrait.UpdatePortrait(type, null);
                Check(!wizard.MythicEquipmentVisible && portrait.MythicEquipmentMask == 0, "Quitar todo el equipo conserva piezas.");
                var compact = new WizardVisual(); AddChild(compact); compact.Build(player, false, true);
                compact.Update(player with { Equipment = full }, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                Check(compact.MythicEquipmentMask == 0, "Un enemigo compacto recibe indumentaria del jugador.");
                compact.QueueFree();
            }
            finally { wizard.QueueFree(); avatar.QueueFree(); portrait.QueueFree(); }
        }
        GD.Print("MYTHIC_EQUIPMENT_OK: cinco magos, seis piezas independientes, cinco calidades, orientación, lanzamiento, inventario, retrato, congelación, muerte y reutilización");
        return true;
    }
}
