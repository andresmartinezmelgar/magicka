using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private BestiaryPanel? _bestiaryPanel;
    private BestiaryOutbox? _bestiaryOutbox;
    private BestiaryOutbox BestiaryQueue => _bestiaryOutbox ??= new(ProjectSettings.GlobalizePath("user://bestiary-pending"));
    private string _localBestiaryRun = "", _localBestiaryUid = "";
    private long _localBestiarySaved;
    private double _bestiaryClock;
    private bool _syncingBestiary;
    internal int PendingBestiaryRuns
    {
        get
        {
            try { return Accounts.Profile is { } p ? BestiaryQueue.Pending(p.Uid).Length : 0; }
            catch (System.IO.IOException) { return 0; }
        }
    }
    public void OpenBestiary()
    {
        if (_playing || GodotObject.IsInstanceValid(_bestiaryPanel)) return;
        _bestiaryPanel = new BestiaryPanel(); _ui.AddChild(_bestiaryPanel); _bestiaryPanel.Build(this);
        _ = SynchronizeBestiary();
    }
    private void CloseBestiary()
    {
        _bestiaryPanel?.QueueFree(); _bestiaryPanel = null;
    }
    private void BeginLocalBestiary()
    {
        _localBestiaryRun = _practice?.IsDefense == true ? Guid.NewGuid().ToString("N") : "";
        _localBestiaryUid = _localBestiaryRun.Length > 0 ? Accounts.Profile?.Uid ?? "" : "";
        _localBestiarySaved = 0;
    }
    private void CaptureLocalBestiary()
    {
        if (_localBestiaryRun.Length == 0 || _localBestiaryUid.Length == 0 || _practice == null) return;
        var progress = _practice.BestiaryProgress(_localId, _localBestiaryRun, _localBestiaryUid);
        if (progress == null || progress.Kills.Values.Sum() <= _localBestiarySaved) return;
        try { BestiaryQueue.Enqueue(progress); _localBestiarySaved = progress.Kills.Values.Sum(); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        { Diagnostics.Warn("bestiary.cache_failed", "No se pudo guardar el bestiario pendiente; se reintentará.", error); }
    }
    private void TickBestiary(double delta)
    {
        _bestiaryClock += delta;
        if (_bestiaryClock < 10) return;
        _bestiaryClock = 0; CaptureLocalBestiary();
        if (!_syncingBestiary && Accounts.SignedIn && Accounts.ServerAddress.Length > 0) _ = SynchronizeBestiary();
    }
    internal async Task<string> SynchronizeBestiary()
    {
        if (_syncingBestiary) return "Sincronizando bestiario…";
        if (!Accounts.SignedIn) return "Inicia sesión para consultar tus bajas guardadas.";
        _syncingBestiary = true;
        try
        {
            string uid = Accounts.Profile?.Uid ?? "";
            var pending = BestiaryQueue.Pending(uid);
            foreach (var progress in pending)
            {
                if (Accounts.Profile?.Uid != uid) return "La cuenta ha cambiado.";
                await Accounts.SaveBestiary(progress, _lifetime.Token);
                BestiaryQueue.Acknowledge(progress);
            }
            await Accounts.Refresh(_lifetime.Token);
            SaveAccountCache();
            return "Bestiario sincronizado.";
        }
        catch (OperationCanceledException) { return "Sincronización pendiente."; }
        catch (Exception error) when (error is System.IO.IOException or InvalidOperationException or
            HttpRequestException or UnauthorizedAccessException)
        {
            return "Se muestran los datos guardados. Las partidas pendientes se sincronizarán cuando la conexión de cuenta esté disponible.";
        }
        finally { _syncingBestiary = false; }
    }
}
