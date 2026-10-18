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
                _hud!.ActionNotice(string.Join(" ", Enumerable.Repeat("Necesitas más energía para lanzar este hechizo.", 8)));
                await Frames(); UpdateTouchLayout();
                VerifyTouchClearance(_touch!); VerifyTouchLayoutVariants(_touch!);
                Capture(map + "-combate");
                OpenTouchSettings();
                ((Button)_controlsSettings!.FindChild("TouchEditPositions", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                await Frames();
                var preview = (MobileControls)_controlsSettings.FindChild("MobileControls", true, false);
                VerifyTouchClearance(preview);
                var targets = preview.HudAreas.Select(a => a.Bounds.GetCenter()).ToList();
                if (preview.MapRectangle is { } minimap) targets.Add(minimap.GetCenter());
                targets.Add(preview.StickCentersForVerification[1]);
                targets.Add(((Control)preview.FindChild("TouchElementWater", true, false)).GetGlobalRect().GetCenter());
                targets.Add(new(-500, -500)); targets.Add(preview.Size + new Vector2(500, 500));
                foreach (var target in targets)
                {
                    var button = ((Control)preview.FindChild("TouchSelf", true, false)).GetGlobalRect().GetCenter();
                    Input.ParseInputEvent(new InputEventScreenTouch { Index = 71, Position = button, Pressed = true });
                    Input.ParseInputEvent(new InputEventScreenDrag { Index = 71, Position = target, Relative = target - button });
                    Input.ParseInputEvent(new InputEventScreenTouch { Index = 71, Position = target, Pressed = false });
                    VerifyTouchClearance(preview);
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
            GD.Print("TOUCH_LAYOUT_SMOKE_OK: siete mapas, HUD y minimapa, 168 configuraciones, arrastres sobre controles, paneles y fuera de pantalla.");
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
        foreach (var action in controls.GetChildren().OfType<PanelContainer>())
        {
            var expected = rectangles.Single(r => r.Name == action.Name).Bounds;
            Check(expected.Position.DistanceTo(action.GetGlobalRect().Position) < .1f && expected.Size.DistanceTo(action.GetGlobalRect().Size) < .1f,
                "el área pulsable no coincide con el dibujo de " + action.Name);
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

    private static void VerifyTouchLayoutVariants(MobileControls controls)
    {
        var original = controls.Settings.Normalize();
        int cases = 0;
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
        controls.Configure(original);
        GD.Print($"TOUCH_CLEARANCE_OK: {cases} configuraciones, tamaños extremos, modo zurdo, posiciones apiladas y minimapa con margen de 8 px.");
    }
}
