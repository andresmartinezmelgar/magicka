using Arcana.Core;

namespace Arcana.Client;

public partial class Main
{
    private readonly Dictionary<string, DashboardRunReport> _dashboardPending = new(StringComparer.Ordinal);
    private string _dashboardUid = "", _dashboardRun = "";
    private bool _dashboardSending;
    private double _dashboardClock = 15;

    private void TickDashboard(double delta)
    {
        string uid = Accounts.Profile?.Uid ?? "";
        if (_dashboardUid != uid)
        {
            _dashboardPending.Clear(); _dashboardUid = uid; _dashboardRun = "";
        }
        if (_statisticsOutbox == null || !CanViewMatchStatistics || !Accounts.IsDirectFirebaseSession || TutorialActive) return;
        _dashboardClock += delta;
        if (_dashboardClock < 15) return;
        _dashboardClock = 0;
        if (_playing)
        {
            var report = _practice?.MatchStatistics(_practice.MatchEnded ? MatchRecordStatus.Completed : MatchRecordStatus.Playing)
                ?? _connection?.LatestStatistics;
            if (report != null) CaptureDashboard(report);
        }
        TrySendDashboard();
    }

    private void CaptureDashboard(MatchStatisticsReport report)
    {
        if (_statisticsOutbox == null || !CanViewMatchStatistics || !Accounts.IsDirectFirebaseSession || TutorialActive || report.Revision < 1) return;
        var world = _connection?.Latest ?? _snapshot;
        if (world == null || world.WaitingForPlayers || world.Mode != report.Mode || world.Map != report.Map || world.StatisticsRunId != report.RunId) return;
        if (_dashboardRun != report.RunId)
        {
            _dashboardRun = report.RunId;
            _connection?.ResetDashboardPing();
        }
        // Los snapshots del servidor y sus estadísticas deben corresponder a la misma ronda.
        // Al acabar se solicita el informe final existente, nunca se inventa una victoria.
        if (world.MatchEnded && report.Status == MatchRecordStatus.Playing) return;
        var telemetry = DashboardRunReport.Capture(report, world, _localId, _connection?.DashboardPing);
        if (_dashboardPending.TryGetValue(report.RunId, out var previous) &&
            (previous.Status == MatchRecordStatus.Completed && report.Status != MatchRecordStatus.Completed || previous.Revision > report.Revision)) return;
        if (_dashboardPending.Count >= 8 && !_dashboardPending.ContainsKey(report.RunId))
            _dashboardPending.Remove(_dashboardPending.Keys.First());
        _dashboardPending[report.RunId] = telemetry;
        if (report.Status != MatchRecordStatus.Playing) TrySendDashboard();
    }

    private void TrySendDashboard()
    {
        if (_dashboardSending || _dashboardPending.Count == 0 || !CanViewMatchStatistics || !Accounts.IsDirectFirebaseSession) return;
        var pending = _dashboardPending.First();
        string uid = Accounts.Profile?.Uid ?? "";
        _dashboardSending = true;
        _ = SendDashboard(pending.Key, pending.Value, uid);
    }

    private async Task SendDashboard(string id, DashboardRunReport report, string uid)
    {
        bool sent = false;
        try { await Accounts.UploadDashboardTelemetry(report, _lifetime.Token).ConfigureAwait(false); sent = true; }
        catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        finally
        {
            Post(() =>
            {
                _dashboardSending = false;
                if (sent && uid == _dashboardUid && _dashboardPending.TryGetValue(id, out var latest) && ReferenceEquals(latest, report))
                    _dashboardPending.Remove(id);
            });
        }
    }
}
