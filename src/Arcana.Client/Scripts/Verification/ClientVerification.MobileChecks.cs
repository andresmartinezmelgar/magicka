using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool _mobileSmoke;
    private int _mobileFrames;
    private V2 _mobileStart;
    private void CheckMobile()
    {
        try
        {
            var touch = _runtime.Touch ?? throw new InvalidDataException("Faltan los controles táctiles.");
            var player = _runtime.Practice!.Players[_runtime.LocalId];
            float width = touch.Size.X, height = touch.Size.Y;
            Vector2 move = new(222, height - 125), aim = new(width - 62, height - 125);
            Vector2 global = touch.GlobalPosition;
            void Down(int finger, Vector2 at) => touch.Touch(finger, at + global, true);
            void Up(int finger, bool canceled = false) => touch.Touch(finger, new(-100, -100), false, canceled);
            void Tap(Element element)
            {
                var button = (Control)touch.FindChild("TouchElement" + element, true, false);
                Down(2, button.GetRect().GetCenter()); Up(2);
            }
            void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
            void ReadyForSpell() { for (int i = 0; i < 60 && player.ActionCooldown > 0; i++) _runtime.Practice.Step(); }
            // Los controles actúan sobre la simulación; el emulador puede dibujar
            // varios segundos entre fotogramas y liberar automáticamente una roca.
            switch (++_mobileFrames)
            {
                case 10:
                    Require(CheckMobilePalette(), "La paleta móvil no distingue los elementos o pierde contraste.");
                    Require(CheckMobileArea(), "El área izquierda no conserva el origen, los dedos o los botones.");
                    Require(width >= 1440 && height >= 720, "El viewport móvil no respeta la escala horizontal.");
                    foreach (var element in Enum.GetValues<Element>())
                    {
                        var button = (PanelContainer)touch.FindChild("TouchElement" + element, true, false);
                        var rect = button.GetRect();
                        Require(new Rect2(Vector2.Zero, touch.Size).Encloses(rect), "Un elemento queda fuera de pantalla.");
                        Require(button.GetChildren().OfType<VBoxContainer>().Single().GetChildren().OfType<ElementIcon>().Single().Value == element,
                            "Un control móvil no muestra su símbolo elemental.");
                    }
                    var viewport = GetViewport().GetVisibleRect();
                    var metrics = (Control)_runtime.UiRoot.FindChild("NetworkMetricsPanel", true, false);
                    var map = (Control)_runtime.Hud!.FindChild("MiniMapPanel", true, false);
                    Require(viewport.Encloses(metrics.GetGlobalRect()), "El contador de FPS y datos está fuera de pantalla.");
                    Require(!metrics.GetGlobalRect().Intersects(map.GetGlobalRect()),
                        "Las métricas de FPS y datos quedan detrás del minimapa.");
                    var playerHud = (Control)_runtime.Hud.FindChild("PlayerStatusPanel", true, false);
                    Require(!metrics.GetGlobalRect().Intersects(playerHud.GetGlobalRect()),
                        "Las métricas de FPS y datos quedan detrás del panel del jugador.");
                    foreach (string name in new[] { "PlayerStatusPanel", "SpellControlsPanel" })
                        Require(viewport.Encloses(((Control)_runtime.Hud!.FindChild(name, true, false)).GetGlobalRect()), "Un panel del HUD móvil queda fuera de pantalla: " + name);
                    _mobileStart = player.Position;
                    Down(0, move); touch.Drag(0, global + move + new Vector2(80, 0)); Tap(Element.Earth); Tap(Element.Earth); Tap(Element.Fire);
                    Down(1, aim);
                    Require(player.Charging?.Elements.Length == 3 && touch.Movement.X > 0.9f, "No se puede mover y cargar tres elementos con varios dedos.");
                    break;
                case 45:
                    Require(player.Position.X > _mobileStart.X + 0.1f && player.ChargeTime > 0.05f, "El joystick no produce movimiento o carga real.");
                    touch.Drag(0, global + new Vector2(400, height - 125));
                    Require(touch.Movement.Length() <= 1.001f, "El joystick no limita su vector fuera del círculo.");
                    Up(0); Up(1);
                    Require(touch.Movement.IsZeroApprox() && player.Charging == null && _runtime.Practice.Snapshot().Projectiles.Length > 0, "Soltar fuera del control no detiene el movimiento o libera la roca.");
                    break;
                case 60:
                    ReadyForSpell();
                    _runtime.Submit(new() { Type = "clear" }); Tap(Element.Shield);
                    Down(2, new(width - 310, height - 120)); Up(2);
                    Require(player.Shield > 0, "El botón Sobre ti no crea el escudo.");
                    ReadyForSpell();
                    Down(2, new(width / 2 - 184, height - 54)); Up(2);
                    Require(player.Haste > 0, "La magia especial no se ejecuta por toque.");
                    ReadyForSpell(); Tap(Element.Earth); Down(1, aim);
                    Require(player.Charging != null, "No empieza la carga que se va a cancelar."); Up(1, true);
                    Require(player.Charging == null, "Un toque cancelado deja el hechizo cargándose.");
                    ReadyForSpell(); _runtime.Submit(new() { Type = "clear" });
                    Tap(Element.Shield); Tap(Element.Earth); Tap(Element.Fire); Down(1, aim); Up(1);
                    Require(_runtime.Practice.Snapshot().Walls.Any(w => w.Elements.Contains(Element.Earth) && w.Elements.Contains(Element.Fire)),
                        "El control táctil no crea la barrera de piedra y fuego.");
                    ReadyForSpell(); Down(2, new(width / 2 + 61, height - 54)); Up(2);
                    Require(_runtime.Practice.Snapshot().Fields.Any(f => f.Kind == "meteor"), "El toque no lanza el meteorito.");
                    break;
                case 65:
                    // Regression: placing a directional shield used to happen on touch-down,
                    // before the finger had a chance to select its final direction.
                    ReadyForSpell();
                    _runtime.Submit(new() { Type = "clear" });
                    Tap(Element.Shield); Tap(Element.Earth); Tap(Element.Fire);
                    var existingWalls = _runtime.Practice.Snapshot().Walls.Select(w => w.Id).ToHashSet();
                    Vector2 aimCenter = new(width - 142, height - 125);
                    Down(1, aimCenter);
                    Require(touch.BarrierAimPending && player.Selection.Elements.Contains(Element.Shield)
                        && !_runtime.Practice.Snapshot().Walls.Any(w => !existingWalls.Contains(w.Id)),
                        "El escudo se crea antes de terminar de apuntar con el dedo.");
                    touch.Drag(1, global + aimCenter + new Vector2(-75, -25));
                    V2 direction = (_runtime.Aim - player.Position).Normalized;
                    Up(1);
                    var placed = _runtime.Practice.Snapshot().Walls.FirstOrDefault(w => !existingWalls.Contains(w.Id));
                    Require(placed != null && V2.Dot(((placed.A + placed.B) * 0.5f - player.Position).Normalized, direction) > 0.95f
                        && !touch.BarrierAimPending, "El escudo no se coloca en la dirección final del gesto.");
                    ReadyForSpell();
                    _runtime.Submit(new() { Type = "clear" });
                    Tap(Element.Shield); Tap(Element.Earth);
                    var countBeforeCancel = _runtime.Practice.Snapshot().Walls.Length;
                    Down(1, aimCenter); touch.Drag(1, global + aimCenter + Vector2.Up * 65);
                    Up(1, true);
                    Require(!touch.BarrierAimPending && _runtime.Practice.Snapshot().Walls.Length == countBeforeCancel
                        && player.Selection.Elements.Contains(Element.Shield),
                        "Cancelar un gesto coloca un escudo o consume los elementos.");
                    // Use the real touch button so the optimistic local selection and
                    // the authoritative server selection are cleared together.
                    Down(2, new(310, height - 117)); Up(2);
                    Require(player.Selection.Elements.Count == 0, "Vaciar no restablece la selección tras cancelar el escudo.");
                    GD.Print("MOBILE_SHIELD_AIM_OK: el escudo espera al gesto completo, respeta la dirección final y se cancela sin lanzar.");

                    break;
                case 70:
                    Require(_runtime.Renderer.Walls.Values.Any(w => w.Stone && w.HasElementSurface) && _runtime.Renderer.Fields.Values.OfType<MeteorVisual>().Any(),
                        "Los nuevos escudos y el meteorito no llegaron al renderer móvil.");
                    break;
                case 75:
                    ReadyForSpell(); Tap(Element.Earth); Down(0, move); Down(1, aim);
                    Require(player.Charging != null, "No empieza la carga antes de abrir ayuda."); _runtime.ToggleHelp();
                    Require(!touch.Visible && touch.Movement.IsZeroApprox() && player.Charging == null, "Abrir ayuda no cancela todos los dedos.");
                    Require(!touch.Touch(3, global + move, true), "La ayuda permite que un toque alcance la partida.");
                    _runtime.ToggleHelp(); Require(touch.Visible, "Cerrar ayuda no restaura los controles.");
                    break;
                case 90:
                    ReadyForSpell(); Tap(Element.Earth); Down(0, move); Down(1, aim);
                    Require(player.Charging != null, "No empieza la carga antes de pasar a segundo plano.");
                    _runtime.Notify((int)NotificationApplicationPaused);
                    Require(_runtime.Paused && !touch.Visible && touch.Movement.IsZeroApprox() && player.Charging == null, "Pasar a segundo plano no detiene la entrada.");
                    _runtime.Notify((int)NotificationWMGoBackRequest);
                    Require(!_runtime.Paused && touch.Visible, "Atrás no cierra la pausa.");
                    _runtime.Hud!.ScoreOpen = true; _runtime.Hud.Update(_runtime.Practice.Snapshot(), _runtime.LocalId, "Prueba móvil", 0.1);
                    Require(_runtime.Hud.ScoreOpen, "No se puede abrir la tabla sin teclado.");
                    _runtime.Hud.ScoreOpen = false;
                    break;
                case 160:
                    Require(_runtime.Practice.Snapshot().Events.Any(e => e.Kind == "explosion" && e.Text == "Meteoro") &&
                        _runtime.Renderer.Effects.SeenKinds.Contains("explosion") && !_runtime.Renderer.Fields.Values.OfType<MeteorVisual>().Any(),
                        "El meteorito no completó su caída e impacto en el renderer móvil.");
                    // Agua + frío también es instantáneo: antes se disparaba al
                    // apoyar el dedo con una dirección anterior o arbitraria.
                    ReadyForSpell();
                    _runtime.Submit(new() { Type = "clear" });
                    Tap(Element.Water); Tap(Element.Cold);
                    var projectilesBeforeIce = _runtime.Practice.Snapshot().Projectiles.Select(p => p.Id).ToHashSet();
                    Down(1, aimCenter);
                    Require(touch.IceAimPending &&
                        !_runtime.Practice.Snapshot().Projectiles.Any(p => !projectilesBeforeIce.Contains(p.Id)),
                        "El hielo se ha disparado al tocar, antes de terminar de apuntar.");
                    touch.Drag(1, global + aimCenter + new Vector2(-74, -30));
                    V2 iceDirection = (_runtime.Aim - player.Position).Normalized;
                    Up(1);
                    var iceShards = _runtime.Practice.Snapshot().Projectiles
                        .Where(p => !projectilesBeforeIce.Contains(p.Id)).ToArray();
                    Require(iceShards.Length >= 3 &&
                        V2.Dot(iceShards.Aggregate(V2.Zero, (sum, p) => sum + p.Direction).Normalized, iceDirection) > 0.96f &&
                        !touch.IceAimPending,
                        "Los fragmentos de hielo no siguen la dirección final del gesto.");
                    ReadyForSpell();
                    _runtime.Submit(new() { Type = "clear" });
                    Tap(Element.Water); Tap(Element.Cold);
                    var projectileCountOnCancel = _runtime.Practice.Snapshot().Projectiles.Length;
                    Down(1, aimCenter); touch.Drag(1, global + aimCenter + Vector2.Up * 60);
                    Up(1, true);
                    Require(!touch.IceAimPending &&
                        _runtime.Practice.Snapshot().Projectiles.Length == projectileCountOnCancel &&
                        player.Selection.Elements.Contains(Element.Water) &&
                        player.Selection.Elements.Contains(Element.Cold),
                        "Cancelar el gesto de hielo ha lanzado el hechizo o consumido elementos.");
                    _runtime.Submit(new() { Type = "clear" });
                    GD.Print("MOBILE_ICE_AIM_OK: agua y frío esperan a soltar y respetan la dirección final.");
                    Require(CheckTranslationInput(), "Traslación conserva el destino anterior o no continúa desde la llegada.");
                    Require(CheckRespawnInput(), "La reaparición conserva la entrada de la vida anterior.");
                    CompleteSmoke("MOBILE_SMOKE_OK: multitáctil, roca, escudo personal, piedra y fuego, caída e impacto del meteorito, cancelación, ayuda, pausa y Atrás");
                    break;
            }
        }
        catch (Exception e) { GD.PushError("MOBILE_SMOKE_FAILED: " + e.Message); GetTree().Quit(1); }
    }

    private bool CheckMobileArea()
    {
        var touch = _runtime.Touch!;
        var global = touch.GlobalPosition;
        var actions = new List<string>();
        void Observe(ClientMessage message) => actions.Add(message.Type);
        void Require(bool condition, string detail)
        { if (!condition) throw new InvalidDataException("Joystick de área: " + detail); }
        touch.ActionRequested += Observe;
        try
        {
            foreach (var origin in new[] { Vector2.Zero, new Vector2(40, 220), new Vector2(touch.Size.X / 2 - 2, 180), new Vector2(24, touch.Size.Y - 240) })
            {
                touch.Reset();
                Require(touch.Touch(20, global + origin, true) && touch.Movement.IsZeroApprox(), "el contacto inicial debe fijar el centro sin mover.");
                Require(touch.Drag(20, global + origin + new Vector2(41, 0)) && Math.Abs(touch.Movement.X - 0.5f) < 0.001f,
                    "el arrastre no se mide desde el contacto inicial.");
                var movement = touch.Movement;
                touch.Touch(21, global + new Vector2(100, 300), true);
                touch.Drag(21, global + new Vector2(500, 300));
                touch.Touch(21, Vector2.Zero, false);
                Require(touch.Movement == movement, "un segundo dedo sustituye o suelta al primero.");
                touch.Drag(20, global + new Vector2(touch.Size.X - 20, 200));
                Require(Math.Abs(touch.Movement.Length() - 1) < 0.001f && actions.Count == 0, "cruzar al lado derecho cambia de función o supera la velocidad máxima.");
                touch.Touch(20, Vector2.Zero, false, true);
                Require(touch.Movement.IsZeroApprox(), "cancelar fuera del área conserva movimiento.");
            }
            var clear = new Vector2(310, touch.Size.Y - 117);
            Require(touch.Touch(22, global + clear, true) && actions.SequenceEqual(["clear"]) && touch.Movement.IsZeroApprox(),
                "el área de movimiento roba el botón Vaciar.");
            touch.Touch(22, Vector2.Zero, false);
            Require(!touch.Touch(23, global + new Vector2(touch.Size.X * 0.75f, 120), true), "el lado derecho vacío activa movimiento.");
            Require(touch.Touch(24, global + new Vector2(50, 250), true), "no admite un gesto nuevo después de cancelar.");
            touch.Drag(24, global + new Vector2(50, 290));
            touch.Reset();
            Require(touch.Movement.IsZeroApprox() && !touch.Drag(24, global + new Vector2(50, 330)), "Reset conserva un dedo anterior.");
            GD.Print("MOBILE_AREA_OK: origen flotante, mitad izquierda, intensidad, límite circular, propiedad de cada dedo, cancelación y prioridad de botones");
            return true;
        }
        finally { touch.ActionRequested -= Observe; touch.Reset(); }
    }
}
