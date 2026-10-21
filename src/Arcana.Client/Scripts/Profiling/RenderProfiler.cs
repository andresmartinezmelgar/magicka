using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Mide la escena local y conserva la procedencia de los datos y los contadores no disponibles.</summary>
public partial class RenderProfiler : Node
{
    private sealed record Sample(double FrameMs, double ClientCpuMs, double SceneCpuMs, double RenderUpdateCpuMs, double EngineProcessMs, double PhysicsMs,
        double? RenderCpuMs, double? GpuMs, double DrawCalls, double Primitives, double? StaticBytes, long ManagedBytes, double? VideoBytes, double Nodes);
    private readonly List<Sample> _samples = [];
    private readonly Stopwatch _clock = new();
    private ArenaRenderer _renderer = null!;
    private RenderStressScene _scene = null!;
    private WorldSnapshot? _scenerySnapshot;
    private bool _scenery;
    private EngineLogBridge _engineLog = null!;
    private Label _status = null!;
    private Control _overlay = null!;
    private Action<string, JsonElement>? _finished;
    private ArenaMapId _map;
    private string _output = "", _environment = "local";
    private double _warmup = 5, _seconds = 20, _lastTime, _warmStart, _measureStart, _lastCpu, _lastSceneCpu, _lastRenderUpdateCpu;
    private int _frame;
    private int _peakParticles, _peakRings;
    private float _savedZoom;
    private bool _measuring, _stopped, _quit, _initialized, _savedPoints, _savedAudio, _configured;
    internal bool Running => _initialized && !_stopped;
    private SurvivalPerformanceSession? _survival;
    private WorldSnapshot? _survivalFrame;
    internal static bool Requested(string[] args) => args.Contains("--profile-render");

    private static double Number(string[] args, string key, double fallback, double minimum, double maximum)
    {
        var value = args.LastOrDefault(a => a.StartsWith(key));
        return value != null && double.TryParse(value[key.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? Math.Clamp(number, minimum, maximum) : fallback;
    }
    internal void Initialize(ArenaRenderer renderer, Control ui, EngineLogBridge log, string[] args, ArenaMapId map,
        Action<string, JsonElement> finished)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("El perfil gráfico requiere un viewport que renderice.");
        _renderer = renderer; _engineLog = log; _finished = finished; _map = map; _quit = Requested(args);
        var selectedMap = args.LastOrDefault(a => a.StartsWith("--profile-map="));
        if (selectedMap != null && (!Enum.TryParse(selectedMap[14..], true, out _map) || !Enum.IsDefined(_map)))
            throw new ArgumentException("Mapa de perfilado inválido.");
        _warmup = Number(args, "--profile-warmup=", 5, 0, 30); _seconds = Number(args, "--profile-seconds=", 20, 1, 120);
        _output = args.LastOrDefault(a => a.StartsWith("--profile-output="))?[17..] ?? "user://profiles/latest.json";
        _environment = args.LastOrDefault(a => a.StartsWith("--profile-environment="))?[22..] ?? "local";
        if (_environment.Length > 64) throw new ArgumentException("Etiqueta de entorno demasiado larga.");
        if (args.Contains("--profile-survival") || _map == ArenaMapId.Bastion) { _map = ArenaMapId.Bastion; _survival = new(); }
        _scene = new(_survival == null ? _map : ArenaMapId.Classic); SnapshotValidation.Check(_scene.Frame(1));
        _scenery = _survival == null && args.Contains("--profile-scenery");
        if (_scenery) _scenerySnapshot = _scene.Frame(1) with
        { Players = [], Projectiles = [], Walls = [], Fields = [], Beams = [], Events = [], Pickups = [] };
        _savedZoom = _renderer.CurrentZoom; _savedPoints = _renderer.ShowControlPoints; _savedAudio = _renderer.AudioEnabled;
        _configured = true; _renderer.AudioEnabled = false;
        _renderer.Clear(); _renderer.SetPreviewZoom(38); _renderer.ShowControlPoints = true;
        _overlay = new VBoxContainer { Position = new(24, 24) }; ui.AddChild(_overlay);
        _overlay.AddThemeConstantOverride("separation", 10);
        _status = Ui.Label(_scenery ? "Medición gráfica · decoración del mapa" : "Medición gráfica · 16 magos y carga máxima", 20, Ui.Gold); _overlay.AddChild(_status);
        _overlay.AddChild(Ui.Button("Cancelar medición", Cancel));
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        _clock.Start();
        _initialized = true;
        GD.Print($"PROFILE_STARTED: {_map}, {_warmup}s de calentamiento y {_seconds}s de muestras, entorno {_environment}");
    }
    private static double? Available(double value) => double.IsFinite(value) && value > 0 ? value : null;
    private static double Monitor(Performance.Monitor monitor) => Performance.GetMonitor(monitor);
    public override void _Process(double delta)
    {
        if (!Running) return;
        try
        {
            if (_engineLog.ErrorCount > 0) throw new InvalidDataException("El motor registró errores durante el perfilado.");
            double now = _clock.Elapsed.TotalMilliseconds;
            if (_frame == 1) _warmStart = now; // Excluye la creación del mapa y la primera compilación de shaders.
            if (_measuring)
            {
                var viewport = GetViewport().GetViewportRid();
                _samples.Add(new(now - _lastTime, _lastCpu, _lastSceneCpu, _lastRenderUpdateCpu, Monitor(Performance.Monitor.TimeProcess) * 1000,
                    Monitor(Performance.Monitor.TimePhysicsProcess) * 1000, Available(RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport)),
                    Available(RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport)), Monitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
                    Monitor(Performance.Monitor.RenderTotalPrimitivesInFrame), Available(Monitor(Performance.Monitor.MemoryStatic)),
                    GC.GetTotalMemory(false), Available(Monitor(Performance.Monitor.RenderVideoMemUsed)), Monitor(Performance.Monitor.ObjectNodeCount)));
                _peakParticles = Math.Max(_peakParticles, _renderer.Effects.ActiveParticles);
                _peakRings = Math.Max(_peakRings, _renderer.Effects.ActiveRings);
                if (now - _measureStart >= _seconds * 1000 && _samples.Count >= 3) { Save(false); return; }
            }
            else if (_frame >= 1 && now - _warmStart >= _warmup * 1000) { _measuring = true; _measureStart = now; }
            _lastTime = now;
            var began = Stopwatch.GetTimestamp();
            ++_frame;
            var frame = _survival?.Frame(delta) ?? (_scenery ? _scenerySnapshot! : _scene.Frame(_frame));
            _lastSceneCpu = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
            if (_survival != null) _survivalFrame = frame;
            int localId = _survival == null ? 0 : frame.Players.First(p => !p.Bot).Id;
            var renderBegan = Stopwatch.GetTimestamp();
            _renderer.Render(frame, localId, _survival == null ? 1 / 60.0 : delta, true);
            _lastRenderUpdateCpu = Stopwatch.GetElapsedTime(renderBegan).TotalMilliseconds;
            _lastCpu = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
            _status.Text = $"Medición gráfica · {(_survival != null ? "Supervivencia: horda, poderes y portales" : _scenery ? "decoración del mapa" : "16 magos y carga máxima")}\n{(_measuring ? "Midiendo" : "Calentando")} · {_samples.Count} muestras";
        }
        catch (Exception error) { Fail(error); }
    }
    private object? Stats(Func<Sample, double?> value)
    {
        var data = _samples.Select(value).Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToArray();
        if (data.Length == 0) return null;
        double Quantile(double fraction) => data[(int)Math.Ceiling((data.Length - 1) * fraction)];
        return new { samples = data.Length, mean = data.Average(), p50 = Quantile(0.5), p95 = Quantile(0.95), maximum = data[^1] };
    }
    private void Save(bool canceled)
    {
        _stopped = true;
        double measured = _samples.Sum(s => s.FrameMs) / 1000;
        var report = new
        {
            schema = 1, version = BuildInfo.Version, timestampUtc = DateTimeOffset.UtcNow, environment = _environment,
            scope = _survival != null ? "integrated_survival_scene" : _scenery ? "static_map_render_scene" : "synthetic_local_render_scene", canceled, map = _map.ToString(), seed = 271,
            device = new { os = OS.GetName(), model = OS.GetModelName(), gpu = RenderingServer.GetVideoAdapterName(),
                renderer = RenderingServer.GetCurrentRenderingMethod(), driver = RenderingServer.GetCurrentRenderingDriverName(),
                editor = OS.HasFeature("editor"), mobile = Ui.IsMobile, maxFps = Engine.MaxFps,
                windowWidth = GetWindow().Size.X, windowHeight = GetWindow().Size.Y,
                viewportWidth = GetViewport().GetVisibleRect().Size.X, viewportHeight = GetViewport().GetVisibleRect().Size.Y },
            scene = new { players = _survivalFrame?.Players.Length ?? (_scenery ? 0 : RenderStressScene.Players), projectiles = _survivalFrame?.Projectiles.Length ?? (_scenery ? 0 : RenderStressScene.Projectiles), walls = _survivalFrame?.Walls.Length ?? (_scenery ? 0 : RenderStressScene.Walls),
                fields = _survivalFrame?.Fields.Length ?? (_scenery ? 0 : RenderStressScene.Fields), beams = _survivalFrame?.Beams.Length ?? (_scenery ? 0 : RenderStressScene.Beams), eventsPerFrame = _survivalFrame?.Events.Length ?? (_scenery ? 0 : RenderStressScene.Events) },
            survival = _survival == null ? null : new { simulatedTicks = _survival.SimulatedTicks, portalCrossings = _survival.PortalCrossings,
                players = _survival.World.Players.Count, minions = _survival.World.Snapshot().Defense!.Minions.Length, inventoryItems = 96, liveSimulation = true },
            warmupSeconds = _warmup, requestedSeconds = _seconds, measuredSeconds = measured, samples = _samples.Count,
            averageFps = measured > 0 ? _samples.Count / measured : (double?)null,
            metrics = new { frameMs = Stats(s => s.FrameMs), clientCpuMs = Stats(s => s.ClientCpuMs),
                sceneCpuMs = Stats(s => s.SceneCpuMs), renderUpdateCpuMs = Stats(s => s.RenderUpdateCpuMs), engineProcessMs = Stats(s => s.EngineProcessMs),
                physicsMs = Stats(s => s.PhysicsMs), renderCpuMs = Stats(s => s.RenderCpuMs), gpuMs = Stats(s => s.GpuMs),
                drawCalls = Stats(s => s.DrawCalls), primitives = Stats(s => s.Primitives), staticBytes = Stats(s => s.StaticBytes),
                managedBytes = Stats(s => s.ManagedBytes), videoBytes = Stats(s => s.VideoBytes), nodes = Stats(s => s.Nodes) },
            unavailableCountersAreNull = true, particleLimit = CombatEffects.ParticleLimit, ringLimit = CombatEffects.RingLimit,
            peakActiveParticles = _peakParticles, peakActiveRings = _peakRings
        };
        string path = ProjectSettings.GlobalizePath(_output);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, json); File.Move(temporary, path, true);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        Diagnostics.Info("render.profile", "Medición de la escena gráfica.", new Dictionary<string, object?> { ["profile"] = element });
        GD.Print($"PROFILE_OK: {_samples.Count} muestras, {measured:0.00}s, {path}");
        _finished?.Invoke(canceled ? "Medición cancelada; informe parcial guardado." : $"Medición guardada: {_samples.Count / measured:0.0} FPS medios en la escena de carga máxima.", element);
        if (_quit) GetTree().Quit(canceled || _samples.Count < 3 ? 1 : 0);
    }
    private void Fail(Exception error)
    {
        _stopped = true; GD.PushError("PROFILE_FAILED: " + error.Message);
        if (_quit) GetTree().Quit(1); else _finished?.Invoke("No se pudo completar la medición.", default);
    }
    internal void Cancel()
    {
        if (!Running) return;
        try { Save(true); } catch (Exception error) { Fail(error); }
    }
    public override void _ExitTree()
    {
        if (_configured)
        {
            RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), false);
            _renderer.SetPreviewZoom(_savedZoom); _renderer.ShowControlPoints = _savedPoints; _renderer.AudioEnabled = _savedAudio;
        }
        _overlay?.QueueFree();
    }
}
