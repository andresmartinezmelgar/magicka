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

            var world = new GameWorld(new() { Mode = GameMode.Arena });
            var player = world.AddPlayer("Mago de prueba")!;
            player.Health = 300;
            player.Selection.Add(Element.Fire);
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

            Element[] favorite = [Element.Fire, Element.Fire, Element.Lightning];
            controls.ConfigureSpellShortcuts(new() { Attack = new() { Automatic = false, Elements = favorite } });
            snapshot = world.Snapshot(); controls.Update(snapshot.Players.Single(), snapshot);
            Tap("TouchSpellAttack");
            Check(controls.AttackShortcutPrepared && player.Selection.Elements.SequenceEqual(favorite), "Ataque no cargó la mezcla guardada.");
            Tap("TouchElementFire");
            Check(player.Selection.Elements.SequenceEqual(new[] { Element.Fire }), "El acceso bloquea la selección manual.");
            controls.Reset(); _ui.RemoveChild(controls); controls.QueueFree();
        }
        var editor = new SpellShortcutSettingsPanel(); _ui.AddChild(editor);
        SpellShortcutSettings? saved = null;
        editor.Build(new(), tutorial: true, settings => { saved = settings; return true; }, () => { });
        var tabs = (TabContainer)editor.FindChild("SpellShortcutTabs", true, false);
        var save = (Button)editor.FindChild("SpellShortcutsSave", true, false);
        void Press(string name) => ((Button)editor.FindChild(name, true, false)).EmitSignal(BaseButton.SignalName.Pressed);
        ((CheckButton)editor.FindChild("SpellShortcutAutomaticDefense", true, false)).ButtonPressed = false;
        Check(save.Disabled, "El editor acepta un acceso personalizado vacío.");
        Press("SpellShortcutDefenseShield"); Press("SpellShortcutDefenseLife");
        tabs.CurrentTab = 1;
        ((CheckButton)editor.FindChild("SpellShortcutAutomaticAttack", true, false)).ButtonPressed = false;
        Press("SpellShortcutAttackFire"); Press("SpellShortcutAttackFire"); Press("SpellShortcutAttackLightning");
        Check(saved == null && !save.Disabled, "El editor aplica cambios antes de guardarlos.");
        Press("SpellShortcutsSave");
        Check(saved != null && saved.Defense.Elements.SequenceEqual(new[] { Element.Shield, Element.Life }) &&
            saved.Attack.Elements.SequenceEqual(new[] { Element.Fire, Element.Fire, Element.Lightning }), "El editor mezcla las dos configuraciones.");
        _ui.RemoveChild(editor); editor.QueueFree();
        GD.Print("CUSTOM_TOUCH_ROUTING_OK: joysticks en cualquier mitad, modo zurdo, dedos simultáneos y liberación independiente.");
    }
}
