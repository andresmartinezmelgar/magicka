using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private void VerifyCustomTouchRouting()
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        foreach (bool leftHanded in new[] { false, true })
        {
            var controls = new MobileControls(); _ui.AddChild(controls);
            controls.Configure(new TouchSettings { LeftHanded = leftHanded, Positions = new()
            {
                ["Move"] = new(leftHanded ? .25f : .75f, .18f),
                ["Aim"] = new(leftHanded ? .75f : .25f, .18f)
            } });
            var messages = new List<string>(); controls.ActionRequested += m => messages.Add(m.Type);
            var centers = controls.StickCentersForVerification;
            Check(controls.Touch(8, centers[1], true) && messages.Count(m => m == "cast") == 1 && controls.Movement == Vector2.Zero,
                "El joystick de lanzamiento personalizado activa movimiento en vez de lanzar.");
            controls.Touch(9, centers[1], true); controls.Drag(9, centers[1] + Vector2.Right * 30);
            Check(controls.Movement == Vector2.Zero && messages.Count(m => m == "cast") == 1,
                "Un segundo dedo en el joystick ocupado roba el control de movimiento.");
            Check(controls.Touch(10, centers[0], true), "El joystick de movimiento personalizado no responde fuera de su mitad original.");
            controls.Drag(10, centers[0] + Vector2.Right * 30);
            Check(controls.Movement.X > 0 && messages.Count(m => m == "cast") == 1, "Moverse con la distribución personalizada lanza otro hechizo.");
            controls.Drag(8, centers[1] + Vector2.Up * 30);
            Check(controls.AimDirection.Y < -.9f && controls.Movement.X > 0, "Mover y apuntar simultáneamente mezcla los dedos.");
            controls.Touch(10, centers[0], false);
            Check(controls.Movement == Vector2.Zero && !messages.Contains("release"), "Soltar movimiento interrumpe el lanzamiento.");
            controls.Touch(8, centers[1], false);
            Check(messages.Count(m => m == "release") == 1, "Soltar apuntado no libera exactamente una vez.");
            Check(controls.ButtonRectanglesForVerification.Length == 17 && !controls.LayoutRectangles.ContainsKey("TouchSpellAttack") &&
                ((Control)controls.FindChild("TouchElementFire", true, false)).Visible,
                "La selección manual muestra también los accesos automáticos.");

            var world = new GameWorld(new() { Mode = GameMode.Arena });
            var player = world.AddPlayer("Mago de prueba")!;
            player.Health = 300;
            player.Selection.Add(Element.Fire);
            controls.ConfigureSpellShortcuts(new() { Enabled = true });
            Check(controls.ButtonRectanglesForVerification.Length == 11 && !controls.LayoutRectangles.ContainsKey("TouchElementFire") &&
                !((Control)controls.FindChild("TouchElementFire", true, false)).Visible &&
                ((Control)controls.FindChild("TouchSpellAttack", true, false)).Visible,
                "El modo automático conserva elementos manuales visibles o zonas táctiles ocultas.");
            VerifyTouchClearance(controls);
            var snapshot = world.Snapshot();
            controls.Update(snapshot.Players.Single(), snapshot);
            long sequence = 0;
            controls.ActionRequested += message => { message.Sequence = ++sequence; world.ApplyMessage(player.Id, message); };
            messages.Clear();
            void Tap(string name)
            {
                var center = ((Control)controls.FindChild(name, true, false)).GetGlobalRect().GetCenter();
                controls.Touch(11, center, true); controls.Touch(11, center, false);
            }
            Tap("TouchSpellDefense");
            Check(messages.SequenceEqual(new[] { "clear", "element", "element", "element" }) && player.Health == 300 &&
                player.Shield == 0 && player.Selection.Elements.SequenceEqual(new[] { Element.Shield, Element.Earth, Element.Life }),
                "Defensa no prepara un escudo regenerativo, o lo lanza automáticamente.");
            player.Shield = player.ShieldMax = 420; player.ShieldTime = 12;
            snapshot = world.Snapshot(); controls.Update(snapshot.Players.Single(), snapshot);
            Tap("TouchSpellDefense");
            Check(player.Selection.Elements.SequenceEqual(new[] { Element.Life, Element.Life, Element.Life }), "Defensa no cambia a Vida con escudo activo.");

            snapshot = world.Snapshot(); controls.Update(snapshot.Players.Single(), snapshot);
            Tap("TouchSpellAttack");
            Check(controls.AttackShortcutPrepared && player.Selection.Elements.Count > 0, "Ataque no cargó la mezcla recomendada.");
            controls.ConfigureSpellShortcuts(new() { Enabled = false });
            player.Selection.Clear();
            snapshot = world.Snapshot(); controls.Update(snapshot.Players.Single(), snapshot);
            Check(controls.ButtonRectanglesForVerification.Length == 17 && !controls.AttackShortcutPrepared &&
                !controls.LayoutRectangles.ContainsKey("TouchSpellDefense") && controls.LayoutRectangles.ContainsKey("TouchElementFire"),
                "Volver a manual conserva un acceso automático o pierde los botones de elementos.");
            Tap("TouchElementFire");
            Check(player.Selection.Elements.SequenceEqual(new[] { Element.Fire }), "Desactivar los hechizos automáticos bloquea la selección manual.");
            controls.Reset(); _ui.RemoveChild(controls); controls.QueueFree();
        }
        if (Ui.IsMobile)
        {
            UpdateSpellShortcutMenu();
            bool previous = CurrentSpellShortcuts.Enabled;
            try
            {
                _automaticSpellsToggle!.ButtonPressed = !previous;
                Check(CurrentSpellShortcuts.Enabled == !previous &&
                    DevicePreferences.Read<SpellShortcutSettings>(SpellShortcutsFile)?.Enabled == !previous,
                    "El interruptor del menú no aplica y guarda el modo elegido.");
            }
            finally { _automaticSpellsToggle!.ButtonPressed = previous; }
        }
        GD.Print("CUSTOM_TOUCH_ROUTING_OK: joysticks en cualquier mitad, modo zurdo, dedos simultáneos y liberación independiente.");
    }
}
