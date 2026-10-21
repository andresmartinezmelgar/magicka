using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private MatchStatisticsArchive? _statisticsArchive;
    private MatchStatisticsTransport? _statisticsTransport;
    private MatchStatisticsPanel? _statisticsPanel;
    private string _statisticsReaderUid = "", _statisticsSavedRun = "";
    private long _statisticsSavedRevision = -1;
    private MatchRecordStatus _statisticsSavedStatus;
    private double _statisticsClock = 15, _statisticsSendClock = 30, _statisticsAccessClock = 60;
    private bool _statisticsSending, _statisticsCheckingAccess, _statisticsEnded;
    private ArenaConnection? _statisticsConnection;
    public bool CanViewMatchStatistics => Accounts.IsDirectFirebaseSession && _statisticsReaderUid.Length > 0 && Accounts.Profile?.Uid == _statisticsReaderUid;
    private void InitializeMatchStatistics(string[] arguments)
    {
        if (ClientVerification.Requested(arguments) || RenderProfiler.Requested(arguments) || arguments.Any(a => a.Contains("smoke", StringComparison.Ordinal) || a.Contains("preview", StringComparison.Ordinal) || a.Contains("profiler", StringComparison.Ordinal) || a == "--visual-review")) return;
        _statisticsArchive = new(ProjectSettings.GlobalizePath("user://match-statistics"));
        _statisticsTransport = new(FirebaseProjectId);
        _ = Task.Run(() => { try { _statisticsArchive.RecoverInterrupted(); } catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { } });
    }
    private void SaveStatistics(MatchStatisticsReport report, bool immediate = false)
    {
        if (_statisticsArchive == null || report.Revision == 0 || _statisticsSavedRun == report.RunId && _statisticsSavedRevision == report.Revision && _statisticsSavedStatus == report.Status) return;
        var archive = _statisticsArchive;
        void Save()
        {
            try { archive.Save(report); }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            { Diagnostics.Warn("statistics.save_failed", "No se pudo guardar el informe de partida."); }
        }
        if (immediate) Save(); else _ = Task.Run(Save);
        _statisticsSavedRun = report.RunId; _statisticsSavedRevision = report.Revision; _statisticsSavedStatus = report.Status;
    }
    private void CaptureStatistics(MatchRecordStatus status, bool immediate = false)
    {
        if (!_playing || _statisticsArchive == null) return;
        var report = _practice?.MatchStatistics(status) ?? _connection?.LatestStatistics;
        if (report != null) SaveStatistics(report.Complete ? report : report with { Status = status }, immediate);
    }
    private async Task CloseStatisticsConnection(ArenaConnection connection)
    {
        if (_statisticsArchive != null && connection.Connected && connection.RequestMatchStatistics())
        {
            var previous = connection.LatestStatistics;
            for (int i = 0; i < 20 && ReferenceEquals(previous, connection.LatestStatistics) && connection.Connected; i++) await Task.Delay(50);
        }
        if (connection.LatestStatistics is {} report) SaveStatistics(report.Complete ? report : report with { Status = MatchRecordStatus.Left }, true);
        await connection.DisposeAsync();
    }
    private void TickMatchStatistics(double delta)
    {
        if (_statisticsArchive == null) return;
        if (_statisticsReaderUid.Length > 0 && !CanViewMatchStatistics) { _statisticsReaderUid = ""; CloseMatchStatistics(); _accountProfilePanel?.RefreshStatisticsAccess(); }
        _statisticsClock += delta; _statisticsSendClock += delta; _statisticsAccessClock += delta;
        if (_statisticsAccessClock >= 60 && !_statisticsCheckingAccess && Accounts.IsDirectFirebaseSession)
        { _statisticsAccessClock = 0; _ = RefreshStatisticsAccess(); }
        if (_connection != _statisticsConnection) { _statisticsConnection = _connection; _statisticsClock = 15; _statisticsEnded = false; }
        bool ended = _practice?.MatchEnded == true || _connection?.Latest?.MatchEnded == true;
        bool final = ended && !_statisticsEnded; _statisticsEnded = ended;
        if (_playing && _connection?.LatestStatistics is {} incoming) SaveStatistics(incoming, incoming.Complete);
        if (_playing && (_statisticsClock >= 15 || final))
        {
            _statisticsClock = 0;
            _connection?.RequestMatchStatistics();
            CaptureStatistics(MatchRecordStatus.Playing, final);
        }
        if (_statisticsSendClock >= 30 && !_statisticsSending) { _statisticsSendClock = 0; _ = SendMatchStatistics(); }
    }
    private async Task SendMatchStatistics()
    {
        if (_statisticsArchive == null || _statisticsTransport == null) return;
        _statisticsSending = true;
        try { await _statisticsArchive.Flush(_statisticsTransport.Upload, _lifetime.Token); }
        catch (Exception e) when (e is System.IO.IOException or HttpRequestException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException) { }
        finally { _statisticsSending = false; }
    }
    private async Task RefreshStatisticsAccess()
    {
        if (_statisticsCheckingAccess) return;
        _statisticsCheckingAccess = true;
        string uid = Accounts.Profile?.Uid ?? "";
        bool allowed = false;
        try { allowed = await Accounts.CanReadMatchStatistics(_lifetime.Token); }
        catch (Exception e) when (e is System.IO.IOException or HttpRequestException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or System.Text.Json.JsonException) { }
        finally { _statisticsCheckingAccess = false; }
        Post(() =>
        {
            _statisticsReaderUid = allowed && Accounts.Profile?.Uid == uid ? uid : "";
            if (!CanViewMatchStatistics) CloseMatchStatistics();
            if (GodotObject.IsInstanceValid(_accountProfilePanel)) _accountProfilePanel!.RefreshStatisticsAccess();
        });
    }
    public void OpenMatchStatistics()
    {
        if (_playing || !CanViewMatchStatistics || GodotObject.IsInstanceValid(_statisticsPanel)) return;
        _statisticsPanel = new(); _ui.AddChild(_statisticsPanel); _statisticsPanel.Build(this);
    }
    private void CloseMatchStatistics() { _statisticsPanel?.QueueFree(); _statisticsPanel = null; }
}
