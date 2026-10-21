using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private MatchStatisticsArchive? _statisticsArchive;
    private MatchStatisticsTransport? _statisticsTransport;
    private MatchStatisticsPanel? _statisticsPanel;
    private string _statisticsProfileUid = "", _statisticsSavedRun = "";
    private long _statisticsSavedRevision = -1;
    private MatchRecordStatus _statisticsSavedStatus;
    private double _statisticsClock = 15, _statisticsSendClock = 30;
    private bool _statisticsSending, _statisticsEnded;
    private ArenaConnection? _statisticsConnection;
    public bool CanViewMatchStatistics => Accounts.Profile?.Uid is
        "hg7F74kCNVQU3tU2itdjWu9SkyS2" or "qRUza2DieeTypFqBIRA5KhbtXoY2";
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
        if (_statisticsProfileUid != (Accounts.Profile?.Uid ?? "")) RefreshStatisticsAccess();
        if (_statisticsArchive == null) return;
        _statisticsClock += delta; _statisticsSendClock += delta;
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
    private void RefreshStatisticsAccess()
    {
        string uid = Accounts.Profile?.Uid ?? "";
        if (_statisticsProfileUid != uid || !CanViewMatchStatistics) CloseMatchStatistics();
        _statisticsProfileUid = uid;
        if (GodotObject.IsInstanceValid(_accountProfilePanel)) _accountProfilePanel!.RefreshStatisticsAccess();
    }
    internal Task<MatchStatisticsReport[]> ReadLocalMatchStatistics(CancellationToken token) =>
        _statisticsArchive is { } archive ? Task.Run(() => archive.LocalReports(1000), token) : Task.FromResult(Array.Empty<MatchStatisticsReport>());
    public void OpenMatchStatistics()
    {
        if (_playing || !CanViewMatchStatistics || GodotObject.IsInstanceValid(_statisticsPanel)) return;
        _statisticsProfileUid = Accounts.Profile!.Uid;
        _statisticsPanel = new(); _ui.AddChild(_statisticsPanel); _statisticsPanel.Build(this, verification: _statisticsSmoke);
    }
    private void CloseMatchStatistics() { _statisticsPanel?.QueueFree(); _statisticsPanel = null; }
}
