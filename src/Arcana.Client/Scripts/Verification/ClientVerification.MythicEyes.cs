using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool CheckMythicEyesVisuals()
    {
        static void Check(bool ok, string message)
        { if (!ok) throw new InvalidDataException("MYTHIC_EYES_FAILED: " + message); }
        var gear = new EquipmentAppearance(ItemTier.Mythic, ItemTier.Mythic, ItemTier.Mythic, ItemTier.Mythic)
        {
            GoldRing = ItemTier.Mythic, MagicRing = ItemTier.Mythic,
            MythicFamilies = MythicEyes.Encode(EquipmentFamily.Life, EquipmentFamily.Life, EquipmentFamily.Life,
                EquipmentFamily.Lightning, EquipmentFamily.Lightning, EquipmentFamily.Fire)
        };
        var template = _runtime.Snapshot!.Players.First() with { Health = 1000, MaxHealth = 1000, Alive = true, Casting = false,
            Frozen = 0, Facing = new(0, 1), Position = V2.Zero, Equipment = gear };
        foreach (var type in Enum.GetValues<WizardType>())
        {
            var atlas = WizardSprites.Load(type);
            for (int frame = 0; frame < WizardSprites.Rows * WizardSprites.Columns; frame++)
            {
                var anchors = atlas.Spec.EyeAnchors![frame];
                if (frame / WizardSprites.Columns == 2) Check(anchors.Length == 0, "Ojos visibles de espaldas.");
                else Check(MythicEyeVisuals.Mesh(atlas, frame).GetFaces().Length == anchors.Length * 6, "Geometría distinta de los anclajes.");
            }
            var player = template with { WizardType = type };
            var wizard = new WizardVisual(); AddChild(wizard); wizard.Build(player, false);
            var avatar = new EquipmentAvatar { Size = new(360, 480) }; AddChild(avatar); avatar.Build(type, _ => { });
            try
            {
                foreach (var (direction, count) in new (V2, int)[] { (new(0, 1), 2), (new(-1, 0), 1), (new(0, -1), 0), (new(1, 0), 1) })
                {
                    player = player with { Facing = direction };
                    wizard.Update(player, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                    Check(wizard.MythicEyeCount == count, "La orientación no respeta la cara visible de " + type);
                    Check(wizard.MythicAuraPieces == 6 && wizard.EquipmentAuraVisible,
                        "El equipo mítico completo no tiene aura en combate.");
                }
                wizard.Update(player with { Equipment = gear with { MagicRing = null, MythicFamilies = 0 } }, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                Check(wizard.MythicEyeCount == 0, "Quitar un anillo mantiene el poder.");
                Check(wizard.MythicAuraPieces == 5, "Quitar un anillo apaga el aura de las otras piezas.");
                var partial = player with { Equipment = new EquipmentAppearance(Hat: ItemTier.Mythic) };
                wizard.Update(partial, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                Check(wizard.MythicEyeCount == 0 && wizard.MythicAuraPieces == 1 && wizard.EquipmentAuraVisible,
                    "Una sola pieza mítica no activa los filamentos.");
                wizard.Update(player with { Health = 0, Alive = false }, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                Check(wizard.MythicEyeCount == 0, "El fantasma conserva los ojos.");
                Check(!wizard.EquipmentAuraVisible && wizard.MythicAuraPieces == 0, "El fantasma conserva el aura.");
                wizard.Update(partial, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                Check(wizard.MythicAuraPieces == 1 && wizard.EquipmentAuraVisible, "Reaparecer pierde el aura.");
                wizard.Update(player with { Equipment = new EquipmentAppearance(Hat: ItemTier.Diamond) }, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                Check(wizard.EquipmentAuraVisible && wizard.MythicAuraPieces == 0, "Diamante conserva rayitos míticos.");
                wizard.Update(player with { Equipment = null }, 1 / 60f, 0, _runtime.Renderer.Camera, 0, 0);
                Check(!wizard.EquipmentAuraVisible, "Desequipar conserva el aura.");
                foreach (var slot in Enum.GetValues<EquipmentSlot>())
                {
                    var item = EquipmentCatalogue.Generate(new(130 + (int)slot), tier: ItemTier.Mythic, slot: slot, family: EquipmentFamily.Life);
                    avatar.UpdateSlot(slot, item, false);
                }
                Check(avatar.MythicEyesVisible, "El inventario no muestra el equipo mítico completo.");
                Check(avatar.MythicAuraPieces == 6, "El inventario no cuenta los seis míticos.");
                avatar.UpdateSlot(EquipmentSlot.GoldRing, null, false);
                Check(!avatar.MythicEyesVisible, "El inventario conserva ojos sin los seis míticos.");
                Check(avatar.MythicAuraPieces == 5, "El inventario pierde los filamentos de otras piezas.");
            }
            finally { wizard.QueueFree(); avatar.QueueFree(); }
        }
        GD.Print("MYTHIC_EYES_OK: cinco magos, 80 poses, anclajes, caras visibles, mezcla, inventario, anillos, muerte y limpieza");
        return true;
    }
}
