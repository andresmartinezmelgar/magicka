using Arcana.Core;
using Godot;
using System.Globalization;

namespace Arcana.Client;

public partial class MatchStatisticsPanel
{
    private void Versions()
    {
        string selected = _version.GetItemText(_version.Selected);
        _version.Clear(); _version.AddItem("Todas las versiones");
        foreach (string version in _reports.Values.Select(p => p.GameVersion).Distinct().OrderDescending()) _version.AddItem(version);
        for (int i = 0; i < _version.ItemCount; i++) if (_version.GetItemText(i) == selected) _version.Select(i);
    }

    private async Task Fetch(bool reset)
    {
        if (_loading || _deletingHistory) return;
        _loading = true; _more.Disabled = _refresh.Disabled = true;
        string? uid = _main.Accounts.Profile?.Uid;
        try
        {
            string cursor = reset ? "" : _nextPageToken;
            for (int i = 0; i < 10; i++)
            {
                var page = await _main.ReadCloudMatchStatistics(cursor, _lifetime.Token);
                if (_lifetime.IsCancellationRequested || !_main.CanViewMatchStatistics || _main.Accounts.Profile?.Uid != uid) return;
                if (reset && i == 0) { _reports.Clear(); _pageTokens.Clear(); _loadedPages = 0; }
                if (page.NextPageToken.Length > 0 && !_pageTokens.Add(page.NextPageToken))
                    throw new System.IO.InvalidDataException("Firebase devolvió una página repetida.");
                foreach (var report in page.Reports)
                    if (!_reports.TryGetValue(report.RunId, out var previous) || MatchStatisticsReport.IsNewer(report, previous)) _reports[report.RunId] = report;
                _nextPageToken = cursor = page.NextPageToken; _loadedPages++;
                if (cursor.Length == 0 || _loadedPages >= 100) break;
            }
            Versions(); Render();
            _more.Visible = _nextPageToken.Length > 0 && _loadedPages < 100;
            _refresh.Text = "Actualizar desde Firebase";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (_lifetime.IsCancellationRequested) return;
            Versions(); Render();
            _status.Text = error is UnauthorizedAccessException ? error.Message
                : "No se pudo consultar Firebase. Los datos ya cargados se conservan en esta pantalla.";
            _refresh.Text = "Reintentar consulta";
            _more.Visible = _nextPageToken.Length > 0 && _loadedPages < 100;
        }
        finally { _loading = false; if (!_lifetime.IsCancellationRequested) _more.Disabled = _refresh.Disabled = false; }
    }
}
