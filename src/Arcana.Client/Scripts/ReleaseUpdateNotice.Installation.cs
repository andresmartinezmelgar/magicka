using System.Net.Http;
using Godot;
using Arcana.Core;

namespace Arcana.Client;

public partial class ReleaseUpdateNotice
{
    private async Task InstallDownloaded()
    {
        if (_downloadPath.Length == 0 || _installing || _waitingToInstall || _stop.IsCancellationRequested || !CurrentIdentity) return;
        int generation = _progressGeneration;
        _waitingToInstall = true;
        _headline!.Text = "Actualización verificada";
        _subtitle!.Text = "Preparando la instalación.";
        _speedLabel!.Text = "Descarga completada";
        _secondary!.Text = "Volver al juego";
        _primary!.Disabled = true; _secondary!.Disabled = false;
        _primary.Text = "Esperando para instalar…";
        try
        {
            while (!GetParent<Main>().CanApplyUpdates)
            {
                if (generation != _progressGeneration || !GodotObject.IsInstanceValid(_overlay)) return;
                _subtitle!.Text = "La instalación comenzará al terminar la partida y detener el alojamiento local.";
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _stop.Token.ThrowIfCancellationRequested();
                if (!CurrentIdentity) return;
            }
            if (generation != _progressGeneration || !GodotObject.IsInstanceValid(_overlay)) return;
            _waitingToInstall = false; _installing = true;
            _primary.Disabled = _secondary.Disabled = true; _primary.Text = "Instalando…";
            if (_android)
            {
                (_androidInstaller ?? throw new InvalidOperationException("No se ha iniciado el instalador Android."))
                    .Install(_downloadPath, _version, _expectedHash, preview: _activePlan?.Preview == true);
            }
            else
            {
                _subtitle!.Text = "Preparando los archivos y la copia de recuperación.";
                await WindowsUpdateInstaller.PrepareAsync(_downloadPath, Launcher.PackageDirectory(), _version,
                    _expectedHash, _updatesFolder, _stop.Token, preview: _activePlan?.Preview == true);
                GetTree().Quit();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (_windowsDelta) FallbackToFull(_progressGeneration);
            else ShowFailure(_progressGeneration, error.Message);
        }
        finally { if (generation == _progressGeneration) _waitingToInstall = false; }
    }

    private void AndroidInstallStatus(string status, string message)
    {
        if (_stop.IsCancellationRequested) return;
        Diagnostics.Info("update.android_status", message, new Dictionary<string, object?>
            { ["status"] = status, ["version"] = _version, ["asset"] = _assetName });
        if (status == "diagnostic" || !GodotObject.IsInstanceValid(_overlay)) return;
        if (status == "failed") { ShowFailure(_progressGeneration, message); return; }
        if (status == "confirmation")
        {
            _headline!.Text = "Confirma la actualización";
            _primary!.Text = "Confirmación pendiente";
        }
        else if (status == "installing")
        {
            _headline!.Text = "Actualización verificada";
            _primary!.Text = "Instalando…";
        }
        _subtitle!.Text = message;
    }
}
