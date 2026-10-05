using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private MobileControls? _touch;
    private bool _mobileSmoke;
    private V2 _mobileStart;

    private void PrepareMobileWindow()
    {
        if (!Ui.IsMobile) return;
        GetTree().AutoAcceptQuit = false;
        if (!OS.HasFeature("mobile"))
        {
            GetWindow().ContentScaleSize = new(1440, 720);
            GetWindow().ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
            GetWindow().Size = new(1600, 720);
        }
    }
    private void BuildTouchControls()
    {
        _touch?.Reset(); _touch?.QueueFree(); _touch = null;
        if (!Ui.IsMobile) return;
        _touch = new MobileControls(); _ui.AddChild(_touch);
        _touch.ActionRequested += message =>
        {
            if (!_playing || _paused || _help.Visible) return;
            UpdateTouchAim(); Submit(InputMessage()); Submit(message);
        };
        _touch.HelpRequested += ToggleHelp;
        _touch.MenuRequested += TogglePause;
        _touch.ScoreRequested += () => { if (_hud != null) { StopInput(); _hud.ScoreOpen = !_hud.ScoreOpen; } };
    }
    private void UpdateTouchAim()
    {
        if (_touch == null) return;
        var player = (_connection?.Latest ?? _snapshot)?.Players.FirstOrDefault(p => p.Id == _localId);
        if (player == null) return;
        Vector2 center = GetViewport().GetVisibleRect().Size / 2;
        V2 direction = _renderer.Cursor(center + _touch.AimDirection * 100) - _renderer.Cursor(center);
        if (direction.Length > 0.01f) _aim = player.Position + direction.Normalized * 14;
    }
    private void UpdateTouchVisibility()
    { if (_touch != null) _touch.Visible = _playing && !_paused && !_help.Visible; }
    public override void _Notification(int what)
    {
        if (!Ui.IsMobile || _launcher == null) return;
        if (what == NotificationWMGoBackRequest)
        {
            if (_help.Visible) { _help.Visible = false; UpdateTouchVisibility(); }
            else if (_playing) TogglePause();
            else if (!_launcher.HandleBack()) GetTree().Quit();
        }
        if ((long)what is NotificationApplicationPaused or NotificationApplicationFocusOut && _playing)
        {
            StopInput();
            if (!_paused && !_help.Visible) TogglePause();
        }
    }
    private void CheckMobile()
    {
        try
        {
            var touch = _touch ?? throw new InvalidDataException("Faltan los controles táctiles.");
            var player = _practice!.Players[_localId];
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
            void ReadyForSpell() { for (int i = 0; i < 60 && player.ActionCooldown > 0; i++) _practice.Step(); }
            switch (_renderFrames)
            {
                case 10:
                    Require(width >= 1440 && height >= 720, "El viewport móvil no respeta la escala horizontal.");
                    foreach (var element in Enum.GetValues<Element>())
                    {
                        var rect = ((Control)touch.FindChild("TouchElement" + element, true, false)).GetRect();
                        Require(new Rect2(Vector2.Zero, touch.Size).Encloses(rect), "Un elemento queda fuera de pantalla.");
                    }
                    var viewport = GetViewport().GetVisibleRect();
                    foreach (string name in new[] { "PlayerStatusPanel", "SpellControlsPanel" })
                        Require(viewport.Encloses(((Control)_hud!.FindChild(name, true, false)).GetGlobalRect()), "Un panel del HUD móvil queda fuera de pantalla: " + name);
                    _mobileStart = player.Position;
                    Down(0, move); Tap(Element.Earth); Tap(Element.Earth); Tap(Element.Fire);
                    Down(1, aim);
                    Require(player.Charging?.Elements.Length == 3 && touch.Movement.X > 0.9f, "No se puede mover y cargar tres elementos con varios dedos.");
                    break;
                case 45:
                    Require(player.Position.X > _mobileStart.X + 0.1f && player.ChargeTime > 0.05f, "El joystick no produce movimiento o carga real.");
                    touch.Drag(0, global + new Vector2(400, height - 125));
                    Require(touch.Movement.Length() <= 1.001f, "El joystick no limita su vector fuera del círculo.");
                    Up(0); Up(1);
                    Require(touch.Movement.IsZeroApprox() && player.Charging == null && _practice.Snapshot().Projectiles.Length > 0, "Soltar fuera del control no detiene el movimiento o libera la roca.");
                    break;
                case 60:
                    ReadyForSpell();
                    Submit(new() { Type = "clear" }); Tap(Element.Shield);
                    Down(2, new(width - 310, height - 120)); Up(2);
                    Require(player.Shield > 0, "El botón Sobre ti no crea el escudo.");
                    ReadyForSpell();
                    Down(2, new(width / 2 - 184, height - 200)); Up(2);
                    Require(player.Haste > 0, "La magia especial no se ejecuta por toque.");
                    ReadyForSpell(); Tap(Element.Earth); Down(1, aim);
                    Require(player.Charging != null, "No empieza la carga que se va a cancelar."); Up(1, true);
                    Require(player.Charging == null, "Un toque cancelado deja el hechizo cargándose.");
                    break;
                case 75:
                    ReadyForSpell(); Tap(Element.Earth); Down(0, move); Down(1, aim);
                    Require(player.Charging != null, "No empieza la carga antes de abrir ayuda."); ToggleHelp();
                    Require(!touch.Visible && touch.Movement.IsZeroApprox() && player.Charging == null, "Abrir ayuda no cancela todos los dedos.");
                    Require(!touch.Touch(3, global + move, true), "La ayuda permite que un toque alcance la partida.");
                    ToggleHelp(); Require(touch.Visible, "Cerrar ayuda no restaura los controles.");
                    break;
                case 90:
                    ReadyForSpell(); Tap(Element.Earth); Down(0, move); Down(1, aim);
                    Require(player.Charging != null, "No empieza la carga antes de pasar a segundo plano.");
                    _Notification((int)NotificationApplicationPaused);
                    Require(_paused && !touch.Visible && touch.Movement.IsZeroApprox() && player.Charging == null, "Pasar a segundo plano no detiene la entrada.");
                    _Notification((int)NotificationWMGoBackRequest);
                    Require(!_paused && touch.Visible, "Atrás no cierra la pausa.");
                    _hud!.ScoreOpen = true; _hud.Update(_practice.Snapshot(), _localId, "Prueba móvil", 0.1);
                    Require(_hud.ScoreOpen, "No se puede abrir la tabla sin teclado.");
                    _hud.ScoreOpen = false;
                    CompleteSmoke("MOBILE_SMOKE_OK: multitáctil, tres cargas, roca, escudo, magia, cancelación, ayuda, pausa y Atrás");
                    break;
            }
        }
        catch (Exception e) { GD.PushError("MOBILE_SMOKE_FAILED: " + e.Message); GetTree().Quit(1); }
    }
}
