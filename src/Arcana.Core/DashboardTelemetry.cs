using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Arcana.Core;

/// <summary>Canal privado de observación, separado del histórico anónimo e inmutable.</summary>
public sealed record DashboardPlayer(int Id, string Name, int Team, bool Bot, int WizardType,
    int Kills, int Deaths, bool Alive, double[] Health, double[] Shield);
public sealed record DashboardPing(long Samples, double SumMs, double MinMs, double MaxMs);
public sealed record DashboardRunReport(int SchemaVersion, string RunId, long Revision, string Day,
    GameMode Mode, ArenaMapId Map, string GameVersion, MatchRecordStatus Status, double Seconds,
    bool Network, int ReporterId, DashboardPlayer[] Players, DashboardPing? Ping)
{
    public static DashboardRunReport Capture(MatchStatisticsReport statistics, WorldSnapshot world, int reporterId, DashboardPing? ping)
    {
        statistics.Validate();
        if (world.StatisticsRunId != statistics.RunId || world.Mode != statistics.Mode || world.Map != statistics.Map)
            throw new InvalidDataException("El estado y las estadísticas pertenecen a rondas distintas.");
        return new(1, statistics.RunId, statistics.Revision, statistics.Day, statistics.Mode, statistics.Map,
            statistics.GameVersion, statistics.Status, statistics.Seconds, statistics.Network, reporterId,
            world.Players.Take(32).Select(p => new DashboardPlayer(p.Id,
                new string(p.Name.Where(c => !char.IsControl(c)).Take(64).ToArray()), p.Team, p.Bot,
                (int)p.WizardType, p.Kills, p.Deaths, p.Alive, p.Damage?.Health.ToArray() ?? new double[9],
                p.Damage?.Shield.ToArray() ?? new double[9])).ToArray(), ping);
    }

    public string ToJson()
    {
        if (SchemaVersion != 1 || !Guid.TryParseExact(RunId, "N", out _) || Revision is < 1 or > 1892160000 ||
            !DateOnly.TryParseExact(Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            !Enum.IsDefined(Mode) || !Enum.IsDefined(Map) || !Enum.IsDefined(Status) ||
            !double.IsFinite(Seconds) || Math.Abs(Seconds - Revision / 60d) > .001 || Players.Length > 32 ||
            Players.Select(p => p.Id).Distinct().Count() != Players.Length ||
            Players.Any(p => p.Name.Length > 64 || p.Name.Any(char.IsControl) || p.Kills < 0 || p.Deaths < 0 || p.WizardType is < 0 or > 4 || p.Team is < 0 or > 1 ||
                !new MatchDamageSnapshot(p.Health, p.Shield).Valid()) ||
            Ping is {} ping && (ping.Samples < 0 || !double.IsFinite(ping.SumMs) || ping.SumMs < 0 ||
                !double.IsFinite(ping.MinMs) || ping.MinMs < 0 || !double.IsFinite(ping.MaxMs) || ping.MaxMs < ping.MinMs))
            throw new InvalidDataException("Telemetría del dashboard inválida.");
        string payload = JsonSerializer.Serialize(this, Protocol.Json);
        if (Encoding.UTF8.GetByteCount(payload) > 65536) throw new InvalidDataException("Telemetría del dashboard demasiado grande.");
        return payload;
    }
}

public sealed partial class AccountClient
{
    /// <summary>Solo Google y el rol statisticsReaders pueden escribir o leer este canal.</summary>
    public async Task UploadDashboardTelemetry(DashboardRunReport report, CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        if (!identity.Direct || identity.Reader == null) throw new UnauthorizedAccessException("Se requiere una sesión directa de Google.");
        string payload = report.ToJson();
        // El destino es el proyecto configurado en la sesión, nunca una URL aportada por la partida.
        await identity.Reader.UploadDashboardTelemetry(identity.Uid, identity.AccessToken, report, payload, token).ConfigureAwait(false);
    }
}

public sealed partial class FirebaseProfileReader
{
    internal async Task UploadDashboardTelemetry(string uid, string idToken, DashboardRunReport report, string payload, CancellationToken token)
    {
        if (uid.Length is < 1 or > 128 || uid.Any(c => c == '/' || char.IsControl(c))) throw new ArgumentException("Cuenta inválida.");
        string document = $"projects/{_projectId}/databases/(default)/documents/dashboardTelemetry/{report.RunId}-{uid}";
        var fields = new
        {
            schemaVersion = new { integerValue = "1" }, runId = new { stringValue = report.RunId },
            ownerUid = new { stringValue = uid }, day = new { stringValue = report.Day },
            revision = new { integerValue = report.Revision.ToString(CultureInfo.InvariantCulture) },
            status = new { integerValue = ((int)report.Status).ToString(CultureInfo.InvariantCulture) },
            payload = new { stringValue = payload }
        };
        var body = new { writes = new[] { new { update = new { name = document, fields },
            updateTransforms = new[] { new { fieldPath = "updatedAt", setToServerValue = "REQUEST_TIME" } } } } };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents:commit")
        { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new IOException("Telemetría pendiente (HTTP " + (int)response.StatusCode + ").");
    }
}
