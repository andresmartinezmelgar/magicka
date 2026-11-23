using System.Net.Http;
using Godot;
using Arcana.Core;

namespace Arcana.Client;

public partial class ReleaseUpdateNotice
{
    // Prueba de geometría real sin consultar GitHub: verifica que el aviso es
    // visible y que sus acciones no quedan fuera del viewport ni colapsadas.
    private async void RunUpdateLayoutSmoke()
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ShowNotice("99.0.0", "https://github.com/andresmartinezmelgar/magicka/releases", 12345678);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Rect2 viewport = GetViewport().GetVisibleRect();
            if (_overlay == null || !_overlay.Visible || _overlay.Size.X < 100 || _overlay.Size.Y < 100)
                throw new InvalidOperationException("El overlay de actualización no ocupa el viewport.");
            var card = _overlay.GetNode<PanelContainer>("CenterContainer/ReleaseUpdateCard");
            if (!viewport.Encloses(card.GetGlobalRect()))
                throw new InvalidOperationException("La tarjeta de actualización queda fuera de pantalla.");
            foreach (var button in new[] { _primary, _secondary })
            {
                if (button == null || !button.IsVisibleInTree() || button.Size.X < 90 ||
                    button.Size.Y < 40 || !viewport.Encloses(button.GetGlobalRect()))
                    throw new InvalidOperationException("Los botones del actualizador no son accesibles.");
            }
            if (_downloading || _downloadStop != null || _primary!.Disabled || _secondary!.Disabled ||
                _primary.Text != "Descargar actualización")
                throw new InvalidOperationException("La descarga comienza sin esperar la elección del usuario.");
            GD.Print("UPDATE_NOTICE_CHOICE_OK: espera Descargar actualización o Ahora no sin iniciar la red.");
            GD.Print("UPDATE_NOTICE_LAYOUT_OK");
            _secondary!.EmitSignal(BaseButton.SignalName.Pressed);
            if (_overlay != null) throw new InvalidOperationException("Ahora no no libera el overlay.");
            _activePlan = new("99.0.0", "preview.apk", "https://github.com/andresmartinezmelgar/magicka/releases", "", 12345678)
                { BuildNumber = 72, SourceBranch = "feature/defensa-oleadas", Changes =
                    ["Guía de botín y enemigos descubiertos en Defensa.", "Inventario de partida compartido y mejoras en la tienda."] };
            ShowNotice(_activePlan.Version, _activePlan.DownloadUrl, _activePlan.Bytes);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!_headline!.Text.Contains("prueba") || !_subtitle!.Text.Contains(_activePlan.SourceBranch) || _downloading)
                throw new InvalidOperationException("La preliminar no identifica su origen o descarga sin consentimiento.");
            if (_changes?.Text.Contains(_activePlan.Changes[0]) != true || !_changes.IsVisibleInTree())
                throw new InvalidOperationException("El aviso no muestra las novedades de la versión seleccionada.");
            _identity = "qa-preview-account|preview";
            _pendingUpdate = _activePlan;
            int previousGeneration = _progressGeneration;
            SynchronizeIdentity("");
            if (_overlay != null || _pendingUpdate != null || _activePlan != null || _progressGeneration <= previousGeneration)
                throw new InvalidOperationException("Cambiar de cuenta conserva una actualización del canal anterior.");
            GD.Print("UPDATE_CHANNEL_IDENTITY_OK: identifica la rama y descarta avisos de pruebas al cerrar la sesión.");
            ShowNotice("99.0.1", "https://github.com/andresmartinezmelgar/magicka/releases", 12345678);
            var main = GetParent<Main>(); main.StartPractice("Prueba de salida", 0, GameMode.Duel, 0);
            _downloadPath = System.IO.Path.Combine(_updatesFolder, "qa-never-install.zip");
            _completed = true;
            ShowFailure(_progressGeneration, "INSTALL_FAILED_ABORTED: Permission denied");
            var pending = InstallDownloaded();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!_waitingToInstall || _installing || _secondary!.Disabled)
                throw new InvalidOperationException("La espera de instalación bloquea la salida del aviso.");
            if (_headline!.Text != "Actualización verificada" || _speedLabel!.Text != "Descarga completada")
                throw new InvalidOperationException("El reintento conserva el mensaje de fallo anterior.");
            if (Ui.IsMobile) main._Notification((int)NotificationWMGoBackRequest);
            else main._UnhandledInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape });
            await pending;
            if (_overlay != null || _waitingToInstall || _installing || main.CanApplyUpdates)
                throw new InvalidOperationException("Atrás no cancela solo la espera de instalación o inicia una instalación descartada.");
            main.ReturnToLauncher();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("UPDATE_INSTALL_WAIT_EXIT_OK: Escape/Atrás cierra la espera, conserva la partida y no inicia la instalación descartada.");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("UPDATE_NOTICE_LAYOUT_FAILED: " + error);
            GetTree().Quit(1);
        }
    }
}
