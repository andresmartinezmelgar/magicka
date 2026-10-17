using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool _aimMarkerSmoke, _aimMarkerRunning;
    private string? _aimMarkerDirectory;

    private async Task CheckAimMarkers()
    {
        try
        {
            var world = _runtime.Practice!;
            var player = world.Players[_runtime.LocalId];
            var touch = _runtime.Touch;
            float scale = touch == null ? 1 : Math.Min(1, touch.Size.Y / 720f);
            Vector2 stick = touch == null ? Vector2.Zero : touch.GlobalPosition + new Vector2(touch.Size.X - 142 * scale, touch.Size.Y - 125 * scale);
            void Require(bool condition, string detail)
            { if (!condition) throw new InvalidDataException("Marcador: " + detail); }
            void Refresh()
            {
                _runtime.Snapshot = world.Snapshot();
                touch?.Update(_runtime.Snapshot.Players.Single(p => p.Id == player.Id), _runtime.Snapshot);
                _runtime.UpdateAimMarker(_runtime.Snapshot);
            }
            void Begin(params Element[] elements)
            {
                touch?.Reset(); _runtime.Submit(new() { Type = "release" });
                _runtime.Submit(new() { Type = "clear" }); player.ActionCooldown = 0;
                foreach (var element in elements)
                {
                    if (touch == null) _runtime.Submit(new() { Type = "element", Element = element });
                    else
                    {
                        var button = (Control)touch.FindChild("TouchElement" + element, true, false);
                        touch.Touch(2, button.GetGlobalRect().GetCenter(), true); touch.Touch(2, Vector2.Zero, false);
                    }
                }
                Refresh();
                if (touch != null) touch.Touch(1, stick + Vector2.Right * 60 * scale, true);
                else
                {
                    Point(new(5, 0));
                    if (elements.Contains(Element.Earth)) _runtime.Submit(new() { Type = "cast" });
                }
                Refresh();
            }
            void Point(V2 offset)
            {
                if (touch != null) touch.Drag(1, stick + new Vector2(offset.X, offset.Y) * 13 * scale);
                else
                {
                    var at = _runtime.Renderer.Camera.UnprojectPosition(ArenaRenderer.World(player.Position + offset));
                    GetViewport().WarpMouse(at); _runtime.Aim = _runtime.Renderer.Cursor(at);
                    _runtime.Submit(_runtime.InputMessage());
                }
                Refresh();
            }
            async Task CaptureMarker(string name)
            {
                if (_aimMarkerDirectory == null) return;
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Directory.CreateDirectory(_aimMarkerDirectory);
                string path = Path.Combine(_aimMarkerDirectory, name + ".png");
                Require(GetViewport().GetTexture().GetImage().SavePng(path) == Error.Ok, "no se guardó " + name);
            }

            foreach (var (name, elements) in new (string, Element[])[]
            {
                ("hielo", [Element.Water, Element.Cold]),
                ("hielo-arcano", [Element.Water, Element.Cold, Element.Arcane]),
                ("roca", [Element.Earth]), ("roca-fuego", [Element.Earth, Element.Fire])
            })
            {
                var before = world.Snapshot().Projectiles.Select(p => p.Id).ToHashSet();
                Begin(elements);
                Require(_runtime.AimMarker.Visible, name + " no muestra la referencia al apuntar.");
                var firstTarget = _runtime.AimMarker.Target;
                Point(new(-5, -2));
                Require(_runtime.AimMarker.Target.DistanceTo(firstTarget) > 20, name + " no mueve el marcador con el gesto.");
                V2 direction = (_runtime.Aim - player.Position).Normalized;
                V2 expected = touch != null ? player.Position + direction * MagickRules.BlinkRange : _runtime.Aim;
                var projected = _runtime.Renderer.Camera.UnprojectPosition(ArenaRenderer.World(expected, 0.05f));
                Require(_runtime.AimMarker.Target.DistanceTo(projected) < 1, name + " no coincide con la dirección del lanzamiento.");
                await CaptureMarker(name);
                if (touch != null) touch.Touch(1, Vector2.Zero, false);
                else
                {
                    if (!elements.Contains(Element.Earth)) _runtime.Submit(new() { Type = "cast" });
                    _runtime.Submit(new() { Type = "release" });
                }
                Refresh();
                var launched = world.Snapshot().Projectiles.Where(p => !before.Contains(p.Id)).ToArray();
                Require(!_runtime.AimMarker.Visible && launched.Length > 0 &&
                    V2.Dot(launched.Aggregate(V2.Zero, (sum, p) => sum + p.Direction).Normalized, direction) > 0.99f,
                    name + " conserva el marcador o dispara en otra dirección.");
            }

            if (touch != null)
            {
                Begin(Element.Water, Element.Cold); Point(new(0, -5));
                int count = world.Snapshot().Projectiles.Length;
                touch.Touch(1, Vector2.Zero, false, true); Refresh();
                Require(!_runtime.AimMarker.Visible && world.Snapshot().Projectiles.Length == count && player.Selection.Elements.Count == 2,
                    "cancelar hielo conserva la referencia, dispara o gasta elementos.");
            }
            Begin(Element.Earth); Point(new(-5, -2));
            for (int i = 0; i < 100; i++) world.Step();
            Refresh();
            Require(player.Charging == null && !_runtime.AimMarker.Visible, "la roca automática conserva la referencia con el dedo pulsado.");
            Begin(Element.Earth); _runtime.ToggleHelp(); Refresh();
            Require(!_runtime.AimMarker.Visible, "abrir ayuda conserva la referencia.");
            _runtime.ToggleHelp();
            Begin(Element.Shield, Element.Water, Element.Cold);
            Require(!_runtime.AimMarker.Visible, "el escudo de hielo se confunde con fragmentos.");
            touch?.Reset(); _runtime.Submit(new() { Type = "clear" }); Refresh();
            if (touch != null)
            {
                player.Energy = 100; player.MagickCooldowns[1] = 0;
                var blink = (Control)touch.FindChild("TouchMagick1", true, false);
                touch.Touch(2, blink.GetGlobalRect().GetCenter(), true); touch.Touch(2, Vector2.Zero, false);
                Refresh(); Require(_runtime.AimMarker.Visible, "Traslación pierde su marcador.");
                await CaptureMarker("traslacion");
                touch.Reset(); Refresh(); Require(!_runtime.AimMarker.Visible, "cancelar Traslación conserva la referencia.");
            }
            _runtime.ReturnToLauncher();
            Require(!_runtime.AimMarker.Visible, "salir de la partida conserva la referencia.");
            CompleteSmoke("AIM_MARKER_SMOKE_OK: hielo, variantes, rocas, dirección final, carga automática, cancelación, ayuda y Traslación.");
        }
        catch (Exception error) { GD.PushError("AIM_MARKER_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); }
    }
}
