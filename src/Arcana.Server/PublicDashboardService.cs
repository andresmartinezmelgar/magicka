using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    internal Query PublicStatisticsQuery(string collection, string start, int limit) =>
        database.Collection(collection).WhereGreaterThanOrEqualTo("day", start).OrderByDescending("day").Limit(limit);
}

public sealed record PublicTelemetry(DashboardRunReport Report, long UpdatedAt);
public sealed record PublicDashboardData(int SchemaVersion, string Start, string End, bool Partial, bool Stale,
    MatchStatisticsReport[] Reports, PublicTelemetry[] Telemetry);
public sealed record PublicDashboardPage(byte[] Json, byte[] Gzip, string ETag);

/// <summary>Two shared, bounded listeners. HTTP readers never query private collections.</summary>
public sealed class PublicDashboardService(AccountService accounts, ILogger<PublicDashboardService> logger) : BackgroundService
{
    internal const int HistoryLimit = 500, TelemetryLimit = 250, ResponseLimit = 4 * 1024 * 1024;
    internal const int LiveResponseLimit = 1024 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, MatchStatisticsReport> _reports = [];
    private readonly Dictionary<string, PublicTelemetry> _telemetry = [];
    private bool _historyReady, _telemetryReady, _historySynced, _telemetrySynced, _partialHistory, _partialTelemetry, _stale = true;
    private string _start = "", _end = "";
    private PublicDashboardPage? _history, _live;

    public PublicDashboardPage? Page(bool live)
    {
        lock (_gate)
        {
            if (!_historyReady || !_telemetryReady) return null;
            return live ? _live : _history;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (accounts.Repository is not FirestoreAccountRepository repository) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            FirestoreChangeListener? history = null, telemetry = null;
            try
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                lock (_gate)
                {
                    _start = today.AddDays(-29).ToString("yyyy-MM-dd"); _end = today.ToString("yyyy-MM-dd");
                    _stale = true;
                    _historySynced = _telemetrySynced = false;
                }
                // Select only the two statistics collections. Never accounts or inventory.
                history = repository.PublicStatisticsQuery("matchStatistics", _start, HistoryLimit + 1)
                    .Listen(snapshot => Update(snapshot, false), stoppingToken);
                telemetry = repository.PublicStatisticsQuery("dashboardTelemetry", _start, TelemetryLimit + 1)
                    .Listen(snapshot => Update(snapshot, true), stoppingToken);
                while (today == DateOnly.FromDateTime(DateTime.UtcNow))
                {
                    var completed = await Task.WhenAny(history.ListenerTask, telemetry.ListenerTask, Task.Delay(10000, stoppingToken));
                    stoppingToken.ThrowIfCancellationRequested();
                    if (completed == history.ListenerTask || completed == telemetry.ListenerTask)
                    { await completed; throw new IOException("Lectura de estadísticas detenida."); }
                    // Expiry and serialization are shared, never work repeated for each visitor.
                    lock (_gate) BuildPages();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                lock (_gate) { _stale = true; BuildPages(); }
                // Error types only: document IDs, aliases and Firebase credentials stay out of logs.
                logger.LogWarning("Dashboard público pendiente de reconexión ({Type}).", error.GetType().Name);
            }
            finally
            {
                foreach (var listener in new[] { history, telemetry })
                    if (listener != null) try { await listener.StopAsync(stoppingToken); } catch (Exception) { }
                lock (_gate) { _stale = true; _historySynced = _telemetrySynced = false; BuildPages(); }
            }
            try { await Task.Delay(30000, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private void Update(QuerySnapshot snapshot, bool live)
    {
        lock (_gate)
        {
            // IDs remain private cache keys; only validated typed payloads leave this service.
            var ids = snapshot.Documents.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            if (live)
            {
                foreach (var id in _telemetry.Keys.Where(id => !ids.Contains(id)).ToArray()) _telemetry.Remove(id);
                _partialTelemetry = snapshot.Count > TelemetryLimit;
            }
            else
            {
                foreach (var id in _reports.Keys.Where(id => !ids.Contains(id)).ToArray()) _reports.Remove(id);
                _partialHistory = snapshot.Count > HistoryLimit;
            }
            foreach (var change in snapshot.Changes.Where(c => c.ChangeType != DocumentChange.Type.Removed))
            {
                var document = change.Document;
                if (live) _telemetry.Remove(document.Id); else _reports.Remove(document.Id);
                try
                {
                    if (!document.TryGetValue<string>("payload", out var payload)) throw new InvalidDataException();
                    if (live)
                    {
                        if (Encoding.UTF8.GetByteCount(payload) > 65536) throw new InvalidDataException();
                        var report = Protocol.Deserialize<DashboardRunReport>(payload) ?? throw new InvalidDataException();
                        report.ToJson(); // Shape, bounds and numeric validation before projection.
                        if (report.GameVersion == null || !Regex.IsMatch(report.GameVersion, @"^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$"))
                            throw new InvalidDataException();
                        report = report with { Players = report.Players.Select(p => p with { Name = PublicAlias(p.Name) }).ToArray() };
                        long updated = document.TryGetValue<Timestamp>("updatedAt", out var timestamp)
                            ? new DateTimeOffset(timestamp.ToDateTime()).ToUnixTimeMilliseconds() : 0;
                        _telemetry[document.Id] = new(report, updated);
                    }
                    else _reports[document.Id] = MatchStatisticsReport.Parse(payload);
                }
                catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException or ArgumentException or NullReferenceException or InvalidOperationException or InvalidCastException or OverflowException)
                { if (live) _partialTelemetry = true; else _partialHistory = true; }
            }
            if (live) { _telemetryReady = _telemetrySynced = true; _partialTelemetry |= _telemetry.Count < snapshot.Count; }
            else { _historyReady = _historySynced = true; _partialHistory |= _reports.Count < snapshot.Count; }
            _stale = !(_historySynced && _telemetrySynced);
            BuildPages();
        }
    }

    internal static string PublicAlias(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c)).Take(64).ToArray());
        return Regex.IsMatch(clean, @"[^\s@]+@[^\s@]+", RegexOptions.CultureInvariant) ? "Mago" : clean;
    }

    private void BuildPages()
    {
        if (!_historyReady || !_telemetryReady) return;
        bool limited = _partialHistory || _partialTelemetry;
        int size = 4096, liveSize = 4096;
        var reports = new List<MatchStatisticsReport>();
        foreach (var report in _reports.Values.Where(r => string.CompareOrdinal(r.Day, _start) >= 0 && string.CompareOrdinal(r.Day, _end) <= 0)
            .GroupBy(r => (r.RunId, r.Day, r.Mode, r.Map, r.GameVersion)).Select(g => g.Aggregate((a, b) => MatchStatisticsReport.IsNewer(b, a) ? b : a))
            .OrderByDescending(r => r.Day).ThenByDescending(r => r.Revision).ThenBy(r => r.RunId).Take(HistoryLimit))
        {
            int bytes = Encoding.UTF8.GetByteCount(report.ToJson());
            if (size + bytes > ResponseLimit) { limited = true; break; }
            size += bytes; reports.Add(report);
        }
        var ended = new List<PublicTelemetry>(); var active = new List<PublicTelemetry>();
        long cutoff = DateTimeOffset.UtcNow.AddSeconds(-90).ToUnixTimeMilliseconds();
        foreach (var entry in _telemetry.Values.Where(t => string.CompareOrdinal(t.Report.Day, _start) >= 0 && string.CompareOrdinal(t.Report.Day, _end) <= 0)
            .OrderByDescending(t => t.UpdatedAt).Take(TelemetryLimit))
        {
            if (entry.Report.Status == MatchRecordStatus.Playing && entry.UpdatedAt < cutoff) continue;
            int bytes = Protocol.SerializeUtf8(entry).Length;
            // A large history must not starve live sessions of their transfer budget.
            if (entry.Report.Status == MatchRecordStatus.Playing)
            {
                if (liveSize + bytes > LiveResponseLimit) { limited = true; continue; }
                liveSize += bytes; active.Add(entry);
            }
            else
            {
                if (size + bytes > ResponseLimit) { limited = true; continue; }
                size += bytes; ended.Add(entry);
            }
        }
        _history = Encode(new(1, _start, _end, limited, _stale, reports.ToArray(), ended.ToArray()), _history);
        _live = Encode(new(1, _start, _end, limited, _stale, [], active.ToArray()), _live);
    }

    private static PublicDashboardPage Encode(PublicDashboardData data, PublicDashboardPage? previous)
    {
        byte[] json = Protocol.SerializeUtf8(data);
        string etag = "W/\"" + Convert.ToHexString(SHA256.HashData(json)) + "\"";
        if (previous?.ETag == etag) return previous;
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) gzip.Write(json);
        return new(json, output.ToArray(), etag);
    }
}
