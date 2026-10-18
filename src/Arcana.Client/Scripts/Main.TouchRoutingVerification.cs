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
            controls.Reset(); _ui.RemoveChild(controls); controls.QueueFree();
        }
        GD.Print("CUSTOM_TOUCH_ROUTING_OK: joysticks en cualquier mitad, modo zurdo, dedos simultáneos y liberación independiente.");
    }
}
