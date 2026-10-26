using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private MatchStatisticsOutbox? _statisticsOutbox;
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
        _statisticsOutbox = new();
        _statisticsTransport = new(FirebaseProjectId);
    }
    private void QueueStatistics(MatchStatisticsReport report)
    {
        if (_statisticsOutbox == null || report.Status == MatchRecordStatus.Playing || report.Revision == 0 || _statisticsSavedRun == report.RunId && _statisticsSavedRevision == report.Revision && _statisticsSavedStatus == report.Status) return;
        if (!_statisticsOutbox.Enqueue(report)) return;
        _statisticsSavedRun = report.RunId; _statisticsSavedRevision = report.Revision; _statisticsSavedStatus = report.Status;
        // El primer intento comienza al cerrar la partida; el reloj solo controla reintentos.
        if (!_statisticsSending) _ = SendMatchStatistics();
    }
    private void CaptureStatistics(MatchRecordStatus status)
    {
        if (!_playing || _statisticsOutbox == null) return;
        var report = _practice?.MatchStatistics(status) ?? _connection?.LatestStatistics;
        // En red se espera el informe final del servidor, no se marca como final una captura antigua.
        if (_practice == null && status == MatchRecordStatus.Completed && report?.Complete != true) return;
        if (report != null) QueueStatistics(report.Complete ? report : report with { Status = status });
    }
    private async Task CloseStatisticsConnection(ArenaConnection connection)
    {
        if (_statisticsOutbox != null && connection.Connected && connection.RequestMatchStatistics())
        {
            var previous = connection.LatestStatistics;
            for (int i = 0; i < 20 && ReferenceEquals(previous, connection.LatestStatistics) && connection.Connected; i++) await Task.Delay(50);
        }
        if (connection.LatestStatistics is {} report) QueueStatistics(report.Complete ? report : report with { Status = MatchRecordStatus.Left });
        await connection.DisposeAsync();
    }
    private void TickMatchStatistics(double delta)
    {
        if (_statisticsProfileUid != (Accounts.Profile?.Uid ?? "")) RefreshStatisticsAccess();
        if (_statisticsOutbox == null) return;
        _statisticsClock += delta; _statisticsSendClock += delta;
        if (_connection != _statisticsConnection) { _statisticsConnection = _connection; _statisticsClock = 15; _statisticsEnded = false; }
        bool ended = _practice?.MatchEnded == true || _connection?.Latest?.MatchEnded == true;
        bool final = ended && !_statisticsEnded; _statisticsEnded = ended;
        if (_playing && _connection?.LatestStatistics is {} incoming) QueueStatistics(incoming);
        if (_playing && (_statisticsClock >= 15 || final))
        {
            _statisticsClock = 0;
            _connection?.RequestMatchStatistics();
            if (final) CaptureStatistics(MatchRecordStatus.Completed);
        }
        if (_statisticsSendClock >= 30 && !_statisticsSending) { _statisticsSendClock = 0; _ = SendMatchStatistics(); }
    }
    private async Task SendMatchStatistics()
    {
        if (_statisticsOutbox == null || _statisticsTransport == null || _statisticsSending) return;
        _statisticsSending = true; _statisticsSendClock = 0;
        try
        {
            do { await _statisticsOutbox.Flush(_statisticsTransport.Upload, _lifetime.Token); }
            while (_statisticsOutbox.PendingCount > 0 && !_lifetime.IsCancellationRequested);
        }
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
    internal async Task<MatchStatisticsPage> ReadCloudMatchStatistics(string pageToken, CancellationToken token)
    {
        if (!CanViewMatchStatistics) throw new UnauthorizedAccessException("Esta cuenta no tiene acceso a las estadísticas.");
        if (_statisticsSmoke)
        {
            var reports = _statisticsReportsForVerification ?? [];
            return pageToken.Length == 0 ? new(reports.Take(2).ToArray(), reports.Length > 2 ? "qa-next" : "")
                : new(reports.Skip(2).ToArray(), "");
        }
        if (!Accounts.IsDirectFirebaseSession) throw new UnauthorizedAccessException("Inicia sesión con Google para consultar las estadísticas de Firebase.");
        if (pageToken.Length == 0 && !await Accounts.CanReadMatchStatistics(token))
            throw new UnauthorizedAccessException("Esta cuenta no tiene habilitada la lectura de estadísticas en Firebase.");
        return await Accounts.ReadMatchStatistics(pageToken, token);
    }
    internal Task<int> DeleteCloudMatchStatistics(CancellationToken token)
    {
        if (!CanViewMatchStatistics || _statisticsSmoke) throw new UnauthorizedAccessException("Borrado no permitido en esta sesión.");
        return Accounts.DeleteMatchStatistics(token);
    }
    public void OpenMatchStatistics()
    {
        if (_playing || !CanViewMatchStatistics || GodotObject.IsInstanceValid(_statisticsPanel)) return;
        _statisticsProfileUid = Accounts.Profile!.Uid;
        _statisticsPanel = new(); _ui.AddChild(_statisticsPanel); _statisticsPanel.Build(this, verification: _statisticsSmoke);
    }
    private void CloseMatchStatistics() { _statisticsPanel?.QueueFree(); _statisticsPanel = null; }
}
