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
            VerifyFloatingStickOrigins(leftHanded, automatic: false);
            VerifyFloatingStickOrigins(leftHanded, automatic: true);
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
            long initialShortcutTick = snapshot.Tick;
            snapshot = world.Snapshot() with { Tick = initialShortcutTick + 29 };
            controls.Update(snapshot.Players.Single(), snapshot);
            Tap("TouchSpellDefense");
            Check(player.Selection.Elements.SequenceEqual(new[] { Element.Shield, Element.Earth, Element.Life }),
                "La recomendación cambia antes de medio segundo o el botón no carga la mezcla que sigue visible.");
            snapshot = world.Snapshot() with { Tick = initialShortcutTick + 30 };
            controls.Update(snapshot.Players.Single(), snapshot);
            Tap("TouchSpellDefense");
            Check(player.Selection.Elements.SequenceEqual(new[] { Element.Life, Element.Life, Element.Life }), "Defensa no cambia a Vida con escudo activo.");

            var stable = snapshot.Players.Single() with { Position = V2.Zero, Health = player.MaxHealth };
            var ally = stable with { Id = 9001, Name = "Aliado herido", Position = stable.Position + new V2(3, 0), Health = stable.MaxHealth * .5f };
            var assistance = snapshot with { Tick = snapshot.Tick + 30, Mode = GameMode.Duel, Players = [stable, ally] };
            controls.Update(stable, assistance);
            messages.Clear();
            Tap("TouchSpellDefense");
            var defenseHint = (ElementTextLabel)controls.FindChild("TouchSpellDefense", true, false).FindChild("SpellShortcutHint", true, false);
            Check(defenseHint.Text == "Curar aliado" && player.Selection.Elements.SequenceEqual(new[] { Element.Life, Element.Life, Element.Life }) &&
                !messages.Any(m => m is "cast" or "self" or "nova"),
                "Defensa no indica la curación del aliado o lanza Vida sin que el jugador lo ordene.");

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

    private void VerifyFloatingStickOrigins(bool leftHanded, bool automatic)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        var controls = new MobileControls(); _ui.AddChild(controls);
        try
        {
            controls.ConfigureSpellShortcuts(new() { Enabled = automatic });
            controls.Configure(new TouchSettings { LeftHanded = leftHanded, Positions = new()
            {
                ["Move"] = new(leftHanded ? 1 : 0, 1), ["Aim"] = new(leftHanded ? 0 : 1, 1)
            } });
            float radius = controls.StickRadiusForVerification;
            var screen = new Rect2(controls.GlobalPosition + Vector2.One * ScreenPlacement.JoystickEdgeMargin,
                controls.Size - Vector2.One * ScreenPlacement.JoystickEdgeMargin * 2);
            void Safe(Vector2 center) => Check(screen.Grow(.01f).Encloses(new Rect2(center - Vector2.One * radius, Vector2.One * radius * 2)),
                "El círculo del joystick no conserva el margen de 48 píxeles.");
            foreach (var center in controls.StickCentersForVerification) Safe(center);
            for (int stick = 0; stick < 2; stick++)
            {
                var home = controls.StickCentersForVerification[stick];
                Check(controls.Touch(51, home, true), "Un joystick no acepta un contacto libre.");
                Check(controls.ActiveStickCentersForVerification[stick].DistanceTo(home) < .01f,
                    "Un contacto con espacio no se usa como origen.");
                if (stick == 0) Check(controls.Movement == Vector2.Zero, "Un contacto centrado mueve al jugador.");
                controls.Touch(51, home, false);
                float x = (stick == 0) != leftHanded ? 1 : controls.Size.X - 1;
                var edge = controls.GlobalPosition + new Vector2(x, controls.Size.Y - 1);
                Check(controls.Touch(52, edge, true), "El contacto en el borde no captura el joystick.");
                var origin = controls.ActiveStickCentersForVerification[stick]; Safe(origin);
                foreach (var direction in new[] { Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down })
                {
                    Check(controls.Drag(52, origin + direction * radius), "El dedo pierde su joystick al arrastrar.");
                    var actual = stick == 0 ? controls.Movement : controls.AimDirection;
                    Check(actual.Dot(direction) > .99f, "El borde impide mover o apuntar en una dirección completa.");
                    Check(controls.ActiveStickCentersForVerification[stick] == origin, "El origen sigue al dedo durante el arrastre.");
                }
                controls.Touch(52, edge, false);
                Check(controls.ActiveStickCentersForVerification[stick] == controls.StickCentersForVerification[stick],
                    "Soltar no recupera la posición de reposo.");
            }
            Check(controls.Movement == Vector2.Zero, "La prueba deja movimiento activo.");
        }
        finally { controls.Reset(); _ui.RemoveChild(controls); controls.QueueFree(); }
        GD.Print($"FLOATING_STICK_ORIGINS_OK: zurdo={leftHanded}, automático={automatic}, margen=48.");
    }
}
