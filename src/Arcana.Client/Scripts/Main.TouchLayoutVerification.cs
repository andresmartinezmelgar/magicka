using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private async Task RunTouchLayoutSmoke(string[] arguments)
    {
        try
        {
            if (!Ui.IsMobile) throw new ArgumentException("La revisión del HUD requiere --mobile.");
            ulong start = Time.GetTicksMsec();
            string? directory = arguments.FirstOrDefault(a => a.StartsWith("--touch-layout-captures="))?["--touch-layout-captures=".Length..];
            async Task Frames(int count = 4)
            { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            void Capture(string name)
            {
                if (directory == null || DisplayServer.GetName() == "headless") return;
                Directory.CreateDirectory(directory);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, name + ".png"));
            }
            foreach (var map in Enum.GetValues<ArenaMapId>())
            {
                var mode = map == ArenaMapId.Bastion ? GameMode.Defense : map is ArenaMapId.Citadel or ArenaMapId.Frontline ? GameMode.Warfare : GameMode.Arena;
                StartPractice("Revisión del HUD", 0, mode, 0, map: map);
                while (Loading)
                {
                    if (Time.GetTicksMsec() - start > 45000) throw new TimeoutException("Carga de mapas para revisar el HUD.");
                    await Frames(1);
                }
                await Frames(8); UpdateTouchLayout();
                if (map == ArenaMapId.Bastion)
                {
                    var timer = _touch!.HudAreas.Single(a => a.Name == "HudTopScore").Bounds;
                    var pieces = _touch.EquipmentHudForVerification!.Buttons.Values.Select(b => b.GetGlobalRect()).ToArray();
                    if (pieces.Count(r => r.End.X + ScreenPlacement.MinimumGap <= timer.Position.X + .1f) != 3 ||
                        pieces.Count(r => r.Position.X >= timer.End.X + ScreenPlacement.MinimumGap - .1f) != 3 ||
                        pieces.Any(r => Math.Abs(r.Position.Y - pieces[0].Position.Y) > .1f))
                        throw new InvalidOperationException("El equipo no forma dos grupos de tres junto al temporizador.");
                    var state = _practice!.Snapshot();
                    var interrupted = state with { Players = state.Players.Select(p => p.Id != _localId ? p : p with
                        { Stunned = false, Casting = false, CastInterruption = new(state.Tick, SpellInterruptionCause.ElectricStun, Duration: .1f) }).ToArray() };
                    _hud!.Update(interrupted, _localId, "Revisión", 0);
                    _hud.Update(interrupted, _localId, "Revisión", .5);
                    if (!_hud.ActionNoticeForSmoke.Contains("Aturdimiento eléctrico") ||
                        !((Control)_hud.FindChild("MagickFeedback", true, false)).IsVisibleInTree())
                        throw new InvalidOperationException("El stun breve no deja un aviso visible después de terminar.");
                    await Frames(2); UpdateTouchLayout();
                    Capture("Bastion-interrupcion-electrica");
                }
                _hud!.ActionNotice(string.Join(" ", Enumerable.Repeat("Necesitas más energía para lanzar este hechizo.", 8)));
                await Frames(); UpdateTouchLayout();
                VerifyTouchClearance(_touch!); VerifyTouchLayoutVariants(_touch!);
                Capture(map + "-combate");
                if (_practicePanel?.FindChild("PracticeOutfitPreview", true, false) is CheckButton outfit)
                {
                    outfit.ButtonPressed = true;
                    await Frames(); UpdateTouchLayout();
                    VerifyTouchClearance(_touch!);
                    outfit.ButtonPressed = false;
                    await Frames();
                }
                OpenTouchSettings();
                ((Button)_controlsSettings!.FindChild("TouchEditPositions", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                await Frames();
                var preview = (MobileControls)_controlsSettings.FindChild("MobileControls", true, false);
                VerifyTouchClearance(preview);
                var targets = preview.HudAreas.Select(a => a.Bounds.GetCenter()).ToList();
                if (preview.MapRectangle is { } minimap) targets.Add(minimap.GetCenter());
                targets.Add(preview.StickCentersForVerification[1]);
                string activeButton = preview.AutomaticSpells ? "TouchSpellAttack" : "TouchElementWater";
                targets.Add(((Control)preview.FindChild(activeButton, true, false)).GetGlobalRect().GetCenter());
                targets.Add(new(-500, -500)); targets.Add(preview.Size + new Vector2(500, 500));
                foreach (var target in targets)
                {
                    var button = ((Control)preview.FindChild("TouchSelf", true, false)).GetGlobalRect().GetCenter();
                    Input.ParseInputEvent(new InputEventScreenTouch { Index = 71, Position = button, Pressed = true });
                    Input.ParseInputEvent(new InputEventScreenDrag { Index = 71, Position = target, Relative = target - button });
                    Input.ParseInputEvent(new InputEventScreenTouch { Index = 71, Position = target, Pressed = false });
                    VerifyTouchClearance(preview);
                }
                VerifyIndividualTouchEditing(preview);
                if (map == ArenaMapId.Classic)
                {
                    bool automatic = preview.AutomaticSpells;
                    preview.ConfigureSpellShortcuts(new() { Enabled = !automatic });
                    VerifyIndividualTouchEditing(preview);
                    preview.ConfigureSpellShortcuts(new() { Enabled = automatic });
                }
                await Frames(); Capture(map + "-editor");
                _controlsSettings.HandleBack(); _controlsSettings.HandleBack();
                if (map == ArenaMapId.Classic)
                {
                    var window = GetWindow(); var originalSize = window.Size; var originalScale = window.ContentScaleSize;
                    window.ContentScaleSize = new(800, 600); window.Size = new(800, 600);
                    await Frames(8); UpdateTouchLayout(); VerifyTouchClearance(_touch!);
                    window.ContentScaleSize = originalScale; window.Size = originalSize;
                    await Frames(8); UpdateTouchLayout(); VerifyTouchClearance(_touch!);
                }
                ReturnToLauncher(); await Frames();
            }
            if (_engineLog.ErrorCount != 0) throw new InvalidOperationException("El motor registró errores en la revisión del HUD.");
            GD.Print("TOUCH_LAYOUT_SMOKE_OK: siete mapas, HUD y minimapa, 336 configuraciones manuales y automáticas, arrastres sobre controles, paneles y fuera de pantalla.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("TOUCH_LAYOUT_SMOKE_FAILED: " + error); GetTree().Quit(1); }
    }

    internal static void VerifyTouchClearance(MobileControls controls)
    {
        void Check(bool valid, string reason) { if (!valid) throw new InvalidOperationException("Margen táctil: " + reason); }
        var screen = new Rect2(controls.GlobalPosition, controls.Size);
        var rectangles = controls.LayoutRectangles.Select(p => new TouchHudArea(p.Key,
            new Rect2(p.Value.Position + controls.GlobalPosition, p.Value.Size))).ToList();
        foreach (var action in controls.GetChildren().OfType<PanelContainer>().Where(a => a.Visible))
        {
            var expected = rectangles.Single(r => r.Name == action.Name).Bounds;
            Check(expected.Position.DistanceTo(action.GetGlobalRect().Position) < .1f && expected.Size.DistanceTo(action.GetGlobalRect().Size) < .1f,
                "el área pulsable no coincide con el dibujo de " + action.Name);
        }
        foreach (string key in new[] { "Move", "Aim" })
        {
            var stick = controls.LayoutRectangles[key];
            Check(stick.Position.X >= ScreenPlacement.JoystickEdgeMargin - .1f && stick.Position.Y >= ScreenPlacement.JoystickEdgeMargin - .1f &&
                stick.End.X <= controls.Size.X - ScreenPlacement.JoystickEdgeMargin + .1f &&
                stick.End.Y <= controls.Size.Y - ScreenPlacement.JoystickEdgeMargin + .1f, key + " pierde los 48 px de margen.");
        }
        if (controls.EquipmentHudForVerification is { } equipment)
            foreach (var (key, button) in equipment.Buttons)
            {
                var expected = rectangles.Single(r => r.Name == key).Bounds;
                Check(expected.Position.DistanceTo(button.GetGlobalRect().Position) < .1f && expected.Size.DistanceTo(button.GetGlobalRect().Size) < .1f,
                    "el icono de equipo no coincide con su área pulsable: " + key);
            }
        if (controls.AutomaticSpells && !new[] { "TouchSpellDefense", "TouchSpellAttack", "TouchSelf", "TouchNova" }
            .Any(key => controls.Settings.Positions.ContainsKey(key) || controls.Settings.ControlScales.ContainsKey(key)))
        {
            var defense = controls.LayoutRectangles["TouchSpellDefense"];
            var attack = controls.LayoutRectangles["TouchSpellAttack"];
            var self = controls.LayoutRectangles["TouchSelf"];
            var area = controls.LayoutRectangles["TouchNova"];
            Check(defense.Size.DistanceTo(attack.Size) < .1f && defense.Size.DistanceTo(self.Size) < .1f &&
                defense.Size.DistanceTo(area.Size) < .1f, "la cuadrícula usa botones de tamaños distintos.");
            Check(Math.Abs(defense.Position.Y - attack.Position.Y) < .1f && Math.Abs(self.Position.Y - area.Position.Y) < .1f &&
                Math.Abs(defense.Position.X - self.Position.X) < .1f && Math.Abs(attack.Position.X - area.Position.X) < .1f,
                "Defensa, Ataque, Sobre mí y En área dejan de formar dos filas y dos columnas.");
            Check(defense.End.X + ScreenPlacement.MinimumGap <= attack.Position.X + .1f &&
                defense.End.Y + ScreenPlacement.MinimumGap <= self.Position.Y + .1f,
                "la cuadrícula cambia el orden de los botones o pierde el margen táctil.");
        }
        if (controls.MapRectangle is { } map) rectangles.Add(new("Minimapa", map));
        for (int i = 0; i < rectangles.Count; i++)
        {
            var area = rectangles[i];
            Check(screen.Grow(-ScreenPlacement.MinimumGap + .1f).Encloses(area.Bounds), area.Name + " fuera del borde: " + area.Bounds);
            foreach (var other in rectangles.Skip(i + 1).Concat(controls.HudAreas))
                Check(ScreenPlacement.Separated(TouchHudLayout.Rect(area.Bounds), TouchHudLayout.Rect(other.Bounds)),
                    area.Name + " tapa " + other.Name + ": " + area.Bounds + " / " + other.Bounds);
        }
        Check(controls.FittedScale >= .7f, "el ajuste reduce demasiado el tamaño de los controles.");
        var information = controls.HudAreas.Where(a => a.Name != "Herramientas").ToArray();
        for (int i = 0; i < information.Length; i++)
        {
            Check(screen.Grow(.1f).Encloses(information[i].Bounds), information[i].Name + " fuera de pantalla.");
            foreach (var other in information.Skip(i + 1))
                Check(ScreenPlacement.Separated(TouchHudLayout.Rect(information[i].Bounds), TouchHudLayout.Rect(other.Bounds)),
                    information[i].Name + " tapa " + other.Name + ": " + information[i].Bounds + " / " + other.Bounds);
        }
    }

    private static void VerifyIndividualTouchEditing(MobileControls controls)
    {
        var original = controls.Settings.Normalize();
        void Check(bool valid, string reason) { if (!valid) throw new InvalidOperationException("Edición individual: " + reason); }
        foreach (var key in controls.LayoutRectangles.Keys.ToArray())
        {
            var before = controls.LayoutRectangles.ToDictionary(p => p.Key, p => p.Value);
            float oldScale = controls.Settings.ControlScale(key), nextScale = oldScale * .8f;
            Check(controls.SelectEditorControl(key) && controls.ResizeSelectedControl(nextScale), "no redimensiona " + key);
            var resized = controls.LayoutRectangles[key];
            Check(resized.Size.DistanceTo(before[key].Size * .8f) < .1f, "tamaño incorrecto en " + key);
            foreach (var (other, rect) in before.Where(p => p.Key != key))
                Check(controls.LayoutRectangles[other] == rect, key + " cambia la posición o tamaño de " + other);
            if (key is "Move" or "Aim")
            {
                int index = key == "Move" ? 0 : 1;
                Check(Math.Abs(controls.StickRadiiForVerification[index] * 2 - resized.Size.X) < .1f, "el radio visual no sigue su tamaño.");
            }
            Check(!controls.ResizeSelectedControl(float.NaN) && controls.LayoutRectangles[key] == resized, "acepta un tamaño no válido.");
            var grab = controls.GlobalPosition + resized.Position + Vector2.One * 2;
            var destination = grab + Vector2.One;
            controls._Input(new InputEventScreenTouch { Index = 91, Position = grab, Pressed = true });
            controls._Input(new InputEventScreenDrag { Index = 91, Position = destination, Relative = Vector2.One });
            controls._Input(new InputEventScreenTouch { Index = 91, Position = destination, Pressed = false });
            Check(controls.LayoutRectangles[key].GetCenter().DistanceTo(resized.GetCenter() + Vector2.One) < .1f,
                "el arrastre no mueve solo " + key);
            foreach (var (other, rect) in before.Where(p => p.Key != key))
                Check(controls.LayoutRectangles[other] == rect, "mover " + key + " desplaza " + other);
            var saved = controls.Settings.Normalize();
            var savedRectangles = controls.LayoutRectangles.ToDictionary(p => p.Key, p => p.Value);
            controls.Configure(saved);
            foreach (var (other, rect) in savedRectangles)
                Check(controls.LayoutRectangles[other].Position.DistanceTo(rect.Position) < .1f && controls.LayoutRectangles[other].Size.DistanceTo(rect.Size) < .1f,
                    "recargar " + key + " altera " + other);
            Check(controls.SelectEditorControl(key) && controls.ResizeSelectedControl(oldScale), "no recupera el tamaño de " + key);
        }
        string cornerKey = controls.LayoutRectangles.ContainsKey("TouchElementFire") ? "TouchElementFire" : "TouchSpellAttack";
        controls.SelectEditorControl(cornerKey);
        var cornerRect = controls.LayoutRectangles[cornerKey]; float cornerScale = controls.Settings.ControlScale(cornerKey);
        var cornerStart = controls.GlobalPosition + cornerRect.End - Vector2.One * 4;
        var cornerEnd = cornerStart - cornerRect.Size * .1f;
        controls._Input(new InputEventScreenTouch { Index = 92, Position = cornerStart, Pressed = true });
        controls._Input(new InputEventScreenDrag { Index = 92, Position = cornerEnd, Relative = cornerEnd - cornerStart });
        controls._Input(new InputEventScreenTouch { Index = 92, Position = cornerEnd, Pressed = false });
        Check(Math.Abs(controls.Settings.ControlScale(cornerKey) - cornerScale * .9f) < .01f, "el tirador de esquina no redimensiona el elemento.");
        controls.Configure(original);
        VerifyTouchClearance(controls);
        GD.Print($"TOUCH_INDIVIDUAL_EDIT_OK: {controls.LayoutRectangles.Count} elementos, tamaños independientes y recarga sin mover los demás.");
    }

    private static void VerifyTouchLayoutVariants(MobileControls controls)
    {
        var original = controls.Settings.Normalize();
        bool originalAutomatic = controls.AutomaticSpells;
        int cases = 0;
        foreach (bool automatic in new[] { false, true })
        {
            controls.ConfigureSpellShortcuts(new() { Enabled = automatic });
            foreach (float size in new[] { .75f, 1f, 1.3f })
            foreach (float spacing in new[] { .8f, 1.25f })
            foreach (bool left in new[] { false, true })
            foreach (bool stacked in new[] { false, true })
            {
                var positions = new Dictionary<string, TouchPosition>();
                if (stacked) foreach (string name in controls.LayoutRectangles.Keys) positions[name] = new(.99f, .01f);
                controls.Configure(new() { Size = size, Spacing = spacing, LeftHanded = left, Positions = positions });
                VerifyTouchClearance(controls); cases++;
            }
        }
        controls.ConfigureSpellShortcuts(new() { Enabled = originalAutomatic });
        controls.Configure(original);
        GD.Print($"TOUCH_CLEARANCE_OK: {cases} configuraciones, tamaños extremos, modo zurdo, posiciones apiladas y minimapa con margen de 8 px.");
    }
}
