using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private async Task RunEquipmentLaboratorySmoke(string[] arguments)
    {
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
        string? captures = arguments.FirstOrDefault(a => a.StartsWith("--equipment-lab-captures="))?["--equipment-lab-captures=".Length..];
        void Capture(string name)
        {
            if (captures == null || DisplayServer.GetName() == "headless") return;
            Directory.CreateDirectory(captures);
            using var image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng(Path.Combine(captures, name + ".png")) == Error.Ok, "No se pudo capturar el laboratorio.");
        }
        try
        {
            string? requested = arguments.FirstOrDefault(a => a.StartsWith("--smoke-viewport="));
            if (requested != null)
            {
                var parts = requested["--smoke-viewport=".Length..].Split('x');
                var size = new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]));
                GetWindow().ContentScaleSize = size; GetWindow().Size = size;
            }
            await Frames();
            var profile = Accounts.Profile;
            _launcher.OpenMobileSmokePage(2); await Frames();
            var access = (Button)_launcher.FindChild("EquipmentLaboratoryButton", true, false);
            Check(access.IsVisibleInTree() && !access.Disabled, "El laboratorio no es accesible desde IA/Defensa.");
            access.EmitSignal(BaseButton.SignalName.Pressed); await Frames(6);
            var lab = _equipmentLaboratory!;
            Check(GodotObject.IsInstanceValid(lab) && !_launcher.Visible && !_playing,
                "Abrir el laboratorio inicia una partida o no oculta el menú.");
            foreach (var type in Enum.GetValues<WizardType>())
            {
                lab.SelectWizard(type); lab.SetAll(ItemTier.Mythic); lab.SelectPose(0, 0); await Frames();
                Check(lab.Equipped.MythicEquipmentMask == 63 && lab.Avatar.MythicEquipmentMask == 63,
                    "Los seis míticos no coinciden entre combate e inventario.");
                lab.SetAll(null); lab.Advance(1 / 60f);
                Check(lab.Equipped.MythicEquipmentMask == 0 && lab.Avatar.MythicEquipmentMask == 0,
                    "Vaciar el equipo mantiene piezas visibles.");
                foreach (var slot in Enum.GetValues<EquipmentSlot>())
                {
                    foreach (var tier in Enum.GetValues<ItemTier>())
                    {
                        lab.SelectPiece(slot, tier); lab.Advance(1 / 60f);
                        int mask = tier == ItemTier.Mythic ? 1 << (int)slot : 0;
                        Check(lab.Equipped.MythicEquipmentMask == mask && lab.Avatar.MythicEquipmentMask == mask,
                            "La selección de calidad o ranura cambia otra pieza.");
                    }
                    lab.SelectPiece(slot, null);
                }
                lab.SetAll(ItemTier.Mythic);
                for (int facing = 0; facing < 4; facing++)
                foreach (int pose in new[] { 0, 1, 2, 3, 5 })
                {
                    lab.SelectPose(pose, facing); lab.Advance(1 / 60f); lab.Advance(1 / 60f); await Frames(1);
                    int frame = lab.Equipped.Sprite.Frame;
                    Check(frame / 4 == facing && lab.Equipped.MythicEquipmentMask == 63,
                        "La orientación del equipo no sigue al mago.");
                    if (pose == 2) Check(frame % 4 == 3, "Lanzar no selecciona la pose de lanzamiento.");
                    if (pose == 3) Check(frame % 4 == 0, "Congelarse mantiene la marcha.");
                }
                lab.SelectPose(4, 0); lab.Advance(.1f);
                Check(!lab.Equipped.MythicEquipmentVisible, "Morir conserva la indumentaria.");
                lab.Advance(3.1f);
                Check(lab.Equipped.MythicEquipmentVisible && lab.Equipped.MythicEquipmentMask == 63,
                    "Reaparecer no recupera el equipo.");
            }
            lab.SelectWizard(WizardType.Balanced); lab.SelectPose(0, 0); await Frames(); Capture("01-combate-frente");
            lab.SelectPose(2, 1); await Frames(); Capture("02-lanzamiento");
            var tabs = (TabContainer)lab.FindChild("EquipmentLabViews", true, false);
            tabs.CurrentTab = 1; await Frames(); Capture("03-inventario");
            var settings = (Control)lab.FindChild("EquipmentLabSettings", true, false);
            Check(tabs.GetGlobalRect().Grow(1).Encloses(lab.Avatar.GetGlobalRect()),
                "El avatar desborda la vista del inventario.");
            foreach (var button in lab.Avatar.Buttons.Values)
                Check(tabs.GetGlobalRect().Grow(1).Encloses(button.GetGlobalRect()) &&
                    !settings.GetGlobalRect().Intersects(button.GetGlobalRect()),
                    "Una zona del inventario se solapa con los ajustes: " + button.Name);
            var back = (Button)lab.FindChild("EquipmentLabBack", true, false);
            Check(back.IsVisibleInTree() && GetViewport().GetVisibleRect().Grow(1).Encloses(back.GetGlobalRect()),
                "Atrás queda fuera de la pantalla.");
            back.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
            Check(_equipmentLaboratory == null && _launcher.Visible, "Atrás no vuelve al menú.");
            OpenEquipmentLaboratory(); await Frames();
            NavigateBack(); await Frames();
            Check(_equipmentLaboratory == null && _launcher.Visible && ReferenceEquals(profile, Accounts.Profile),
                "Escape/Atrás no cierra el laboratorio o altera el perfil.");
            Check(_engineLog.ErrorCount == 0, "Godot registró errores durante el ensayo.");
            var tree = GetTree(); var errors = _engineLog;
            QueueFree();
            _ = FinishVisualVerificationAfterCleanup(tree, errors,
                "EQUIPMENT_LAB_SMOKE_OK: acceso desde menú, cinco magos, seis ranuras, cinco calidades, cuatro direcciones, animaciones, muerte, reaparición, inventario y Atrás sin alterar la cuenta; limpieza final verificada.",
                "EQUIPMENT_LAB_SMOKE_FAILED");
        }
        catch (Exception error) { GD.PushError("EQUIPMENT_LAB_SMOKE_FAILED: " + error); GetTree().Quit(1); }
    }
}
